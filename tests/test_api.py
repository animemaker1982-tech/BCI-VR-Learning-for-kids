"""
Integration tests for the FastAPI backend
"""
import numpy as np
import pytest
from asgi_lifespan import LifespanManager
from httpx import AsyncClient, ASGITransport

from backend.api.main import app


@pytest.fixture(scope="module")
async def client():
    """Start the app lifespan (initialises global ML components) then yield a client."""
    async with LifespanManager(app) as manager:
        async with AsyncClient(
            transport=ASGITransport(app=manager.app), base_url="http://test"
        ) as c:
            yield c


# ── Health ────────────────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_health(client):
    resp = await client.get("/health")
    assert resp.status_code == 200
    data = resp.json()
    assert data["status"] == "ok"


# ── BCI classify ──────────────────────────────────────────────────────────────

def _make_eeg(channels=3, samples=512):
    rng = np.random.default_rng(42)
    return rng.standard_normal((channels, samples)).tolist()


@pytest.mark.asyncio
async def test_bci_classify_returns_state(client):
    payload = {"eeg_data": _make_eeg(), "sfreq": 256.0, "session_id": "test-sess"}
    resp = await client.post("/api/bci/classify", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data["state"] in ("focus", "relax", "neutral")
    assert 0.0 <= data["confidence"] <= 1.0
    assert data["gate_action"] in ("H", "MEASURE", "NONE")


@pytest.mark.asyncio
async def test_bci_classify_invalid_eeg(client):
    """Too few samples should return 422."""
    payload = {"eeg_data": [[1.0, 2.0, 3.0]], "sfreq": 256.0}
    resp = await client.post("/api/bci/classify", json=payload)
    assert resp.status_code == 422


# ── QAOA optimise ─────────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_qaoa_grover(client):
    payload = {"puzzle_type": "grover", "n_qubits": 2, "p_layers": 1}
    resp = await client.post("/api/qaoa/optimise", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data["puzzle_type"] == "grover"
    assert len(data["gate_sequence"]) > 0
    assert data["circuit_depth"] >= 1


@pytest.mark.asyncio
async def test_qaoa_entanglement(client):
    payload = {"puzzle_type": "entanglement", "n_qubits": 2, "p_layers": 1}
    resp = await client.post("/api/qaoa/optimise", json=payload)
    assert resp.status_code == 200


@pytest.mark.asyncio
async def test_qaoa_invalid_type(client):
    payload = {"puzzle_type": "INVALID"}
    resp = await client.post("/api/qaoa/optimise", json=payload)
    assert resp.status_code == 422


# ── RL recommend ──────────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_rl_recommend_returns_action(client):
    payload = {
        "session_id": "sess-1",
        "score_rate": 0.7,
        "error_rate": 0.3,
        "time_on_task": 0.4,
        "bci_confidence": 0.8,
        "streak": 3,
        "current_level": 2,
    }
    resp = await client.post("/api/rl/recommend", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data["action_name"] in (
        "decrease_difficulty", "maintain", "increase_difficulty", "show_hint", "skip_puzzle"
    )
    assert 1 <= data["recommended_level"] <= 8


# ── PQL next-puzzle ───────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_pql_next_puzzle(client):
    payload = {
        "session_id": "sess-2",
        "mastery_vec": [0.0] * 10,
        "session_progress": 0.2,
        "time_since_error": 30.0,
        "current_puzzle_idx": 0,
    }
    resp = await client.post("/api/pql/next-puzzle", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert "next_puzzle" in data
    assert "puzzle_idx" in data


@pytest.mark.asyncio
async def test_pql_wrong_mastery_vec_length(client):
    payload = {
        "session_id": "sess-3",
        "mastery_vec": [0.5] * 5,   # wrong length
        "session_progress": 0.1,
    }
    resp = await client.post("/api/pql/next-puzzle", json=payload)
    assert resp.status_code == 422
