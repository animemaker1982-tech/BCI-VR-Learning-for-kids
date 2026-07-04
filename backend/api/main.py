"""
FastAPI Backend — Quantum Magic Garden
======================================
REST + WebSocket API that connects the Unity VR frontend to the Python
quantum ML backend:

  POST /api/bci/classify         — classify EEG segment → mental state
  POST /api/qaoa/optimise        — run QAOA on a puzzle circuit
  POST /api/rl/recommend         — get adaptive difficulty recommendation
  POST /api/pql/next-puzzle      — get next curriculum puzzle
  POST /api/rl/feedback          — push outcome feedback for online learning
  WS   /ws/session/{session_id}  — real-time BCI event stream
  GET  /health                   — liveness probe
"""

from __future__ import annotations

import asyncio
import logging
import os
import uuid
from contextlib import asynccontextmanager
from typing import Any

import numpy as np
import structlog
from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from prometheus_fastapi_instrumentator import Instrumentator
from pydantic import BaseModel, Field

# Internal modules
import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).parent.parent))

from qaoa.circuit_optimizer import QAOAOptimizer, PuzzleCircuit
from qml.bci_classifier import BCIClassifier, extract_band_powers
from rl.adaptive_agent import AdaptiveRLAgent
from pql.prioritized_q_learning import PQLCurriculumAgent

log = structlog.get_logger()

# ---------------------------------------------------------------------------
# Startup / shutdown
# ---------------------------------------------------------------------------

qaoa_optimizer: QAOAOptimizer
bci_classifier: BCIClassifier
rl_agent: AdaptiveRLAgent
pql_agent: PQLCurriculumAgent


@asynccontextmanager
async def lifespan(app: FastAPI):
    global qaoa_optimizer, bci_classifier, rl_agent, pql_agent

    log.info("Initialising Quantum Magic Garden backend…")

    qaoa_optimizer = QAOAOptimizer()

    bci_classifier = BCIClassifier()
    try:
        bci_classifier.load()
    except FileNotFoundError:
        log.warning("No pretrained QML model — using rule-based BCI classifier")

    rl_agent = AdaptiveRLAgent()
    try:
        rl_agent.load()
    except Exception:
        log.warning("RL agents not available — using heuristic recommendations")

    pql_agent = PQLCurriculumAgent()
    pql_agent.load()

    log.info("Backend ready ✅")
    yield
    log.info("Backend shutting down…")


app = FastAPI(
    title="Quantum Magic Garden API",
    version="1.0.0",
    description="BCI + Quantum ML backend for the Magic Garden VR learning app",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

Instrumentator().instrument(app).expose(app)

# ---------------------------------------------------------------------------
# Schemas
# ---------------------------------------------------------------------------

class BCIRequest(BaseModel):
    eeg_data: list[list[float]] = Field(
        description="EEG data: shape [channels][samples]. Min 3 channels, 256+ samples."
    )
    sfreq: float = Field(default=256.0, description="Sampling frequency in Hz")
    session_id: str = Field(default_factory=lambda: str(uuid.uuid4()))


class BCIResponse(BaseModel):
    session_id: str
    state: str
    confidence: float
    probabilities: dict[str, float]
    gate_action: str


class QAOARequest(BaseModel):
    puzzle_type: str = Field(
        description="One of: grover, shor, entanglement",
        default="grover",
    )
    n_qubits: int = Field(default=3, ge=2, le=6)
    p_layers: int = Field(default=3, ge=1, le=5)


class QAOAResponse(BaseModel):
    puzzle_type: str
    optimal_cost: float
    target_state_probability: float
    circuit_depth: int
    gate_sequence: list[str]
    converged: bool
    iterations: int


class RLRequest(BaseModel):
    session_id: str
    score_rate: float = Field(ge=0.0, le=1.0)
    error_rate: float = Field(ge=0.0, le=1.0)
    time_on_task: float = Field(ge=0.0, le=1.0)
    bci_confidence: float = Field(ge=0.0, le=1.0)
    streak: int = Field(ge=0)
    current_level: int = Field(ge=1, le=8)


class RLResponse(BaseModel):
    session_id: str
    action_name: str
    recommended_level: int
    show_hint: bool
    skip_puzzle: bool
    confidence: float


class PQLRequest(BaseModel):
    session_id: str
    mastery_vec: list[float] = Field(description="Mastery scores for each puzzle type (0–1)")
    session_progress: float = Field(ge=0.0, le=1.0)
    time_since_error: float = Field(default=0.0, description="Seconds since last error")
    current_puzzle_idx: int = Field(default=0, ge=0)


class PQLResponse(BaseModel):
    session_id: str
    next_puzzle: str
    puzzle_idx: int
    mastery_estimate: dict[str, float]
    reason: str


class FeedbackRequest(BaseModel):
    session_id: str
    puzzle_idx: int
    success: bool
    mastery_vec: list[float]
    rl_action: int
    reward: float
    obs: list[float]
    next_obs: list[float]


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------

@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok", "service": "quantum-magic-garden-backend"}


@app.post("/api/bci/classify", response_model=BCIResponse)
async def classify_bci(req: BCIRequest) -> BCIResponse:
    """Classify an EEG segment into focus / relax / neutral."""
    try:
        eeg = np.array(req.eeg_data, dtype=np.float32)
        if eeg.ndim != 2 or eeg.shape[0] < 1 or eeg.shape[1] < 64:
            raise HTTPException(status_code=422, detail="EEG must be [channels × samples ≥ 64]")
        prediction = bci_classifier.predict(eeg, sfreq=req.sfreq)
    except HTTPException:
        raise
    except Exception as exc:
        log.error("BCI classification error", exc=str(exc))
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    return BCIResponse(
        session_id=req.session_id,
        state=prediction.state,
        confidence=prediction.confidence,
        probabilities=prediction.probabilities,
        gate_action=prediction.gate_action,
    )


@app.post("/api/qaoa/optimise", response_model=QAOAResponse)
async def optimise_qaoa(req: QAOARequest) -> QAOAResponse:
    """Run QAOA optimisation for the specified puzzle type."""
    try:
        ptype = req.puzzle_type.lower()
        if ptype == "grover":
            puzzle = qaoa_optimizer.generate_grover_puzzle(req.n_qubits)
        elif ptype == "shor":
            puzzle = qaoa_optimizer.generate_shor_puzzle(req.n_qubits)
        elif ptype == "entanglement":
            puzzle = qaoa_optimizer.generate_entanglement_puzzle()
        else:
            raise HTTPException(status_code=422, detail=f"Unknown puzzle_type: {req.puzzle_type}")

        puzzle.p_layers = req.p_layers
        # Run in executor to avoid blocking the event loop
        result = await asyncio.get_event_loop().run_in_executor(
            None, qaoa_optimizer.optimise, puzzle
        )
    except HTTPException:
        raise
    except Exception as exc:
        log.error("QAOA optimisation error", exc=str(exc))
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    return QAOAResponse(
        puzzle_type=req.puzzle_type,
        optimal_cost=result.optimal_cost,
        target_state_probability=result.target_state_probability,
        circuit_depth=result.circuit_depth,
        gate_sequence=result.gate_sequence,
        converged=result.converged,
        iterations=result.iterations,
    )


@app.post("/api/rl/recommend", response_model=RLResponse)
async def rl_recommend(req: RLRequest) -> RLResponse:
    """Get adaptive difficulty recommendation for the current learner state."""
    action = rl_agent.recommend(
        score_rate=req.score_rate,
        error_rate=req.error_rate,
        time_on_task=req.time_on_task,
        bci_confidence=req.bci_confidence,
        streak=req.streak,
        current_level=req.current_level,
    )
    return RLResponse(
        session_id=req.session_id,
        action_name=action.action_name,
        recommended_level=action.recommended_level,
        show_hint=action.show_hint,
        skip_puzzle=action.skip_puzzle,
        confidence=action.confidence,
    )


@app.post("/api/pql/next-puzzle", response_model=PQLResponse)
async def pql_next_puzzle(req: PQLRequest) -> PQLResponse:
    """Recommend the next puzzle using Prioritised Q-Learning curriculum."""
    mastery = np.array(req.mastery_vec, dtype=np.float32)
    if len(mastery) != 10:
        raise HTTPException(status_code=422, detail="mastery_vec must have exactly 10 elements")

    action = pql_agent.recommend_next(
        mastery_vec=mastery,
        session_progress=req.session_progress,
        time_since_error=req.time_since_error,
        current_puzzle_idx=req.current_puzzle_idx,
    )
    return PQLResponse(
        session_id=req.session_id,
        next_puzzle=action.next_puzzle,
        puzzle_idx=action.puzzle_idx,
        mastery_estimate=action.mastery_estimate,
        reason=action.reason,
    )


@app.post("/api/rl/feedback")
async def rl_feedback(req: FeedbackRequest) -> dict[str, str]:
    """Push outcome feedback to online-update the DQN replay buffer."""
    obs = np.array(req.obs, dtype=np.float32)
    next_obs = np.array(req.next_obs, dtype=np.float32)
    rl_agent.update_online(obs, req.rl_action, req.reward, next_obs, False)

    mastery = np.array(req.mastery_vec, dtype=np.float32)
    pql_agent.update_mastery(req.puzzle_idx, req.success, mastery)

    return {"status": "ok"}


# ---------------------------------------------------------------------------
# WebSocket — real-time BCI stream
# ---------------------------------------------------------------------------

_ws_connections: dict[str, WebSocket] = {}


@app.websocket("/ws/session/{session_id}")
async def ws_bci_stream(websocket: WebSocket, session_id: str) -> None:
    """
    WebSocket endpoint for real-time BCI streaming.
    Unity sends JSON messages with EEG chunks; server responds with predictions.

    Message format (client → server):
        {"eeg": [[...], [...]], "sfreq": 256.0}
    Response (server → client):
        {"state": "focus", "confidence": 0.87, "gate_action": "H"}
    """
    await websocket.accept()
    _ws_connections[session_id] = websocket
    log.info("WS connected", session_id=session_id)

    try:
        while True:
            data = await websocket.receive_json()
            eeg = np.array(data["eeg"], dtype=np.float32)
            sfreq = float(data.get("sfreq", 256.0))
            pred = bci_classifier.predict(eeg, sfreq)
            await websocket.send_json({
                "state": pred.state,
                "confidence": pred.confidence,
                "probabilities": pred.probabilities,
                "gate_action": pred.gate_action,
            })
    except WebSocketDisconnect:
        log.info("WS disconnected", session_id=session_id)
    finally:
        _ws_connections.pop(session_id, None)
