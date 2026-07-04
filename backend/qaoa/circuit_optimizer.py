"""
QAOA Circuit Optimizer
======================
Uses Quantum Approximate Optimization Algorithm (PennyLane) to find optimal
gate sequences for puzzle states and minimise circuit depth while maximising
the probability of target quantum states — driving the Magic Garden puzzle
generation and hint system.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any

import numpy as np
import pennylane as qml
from pennylane import numpy as pnp

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Data types
# ---------------------------------------------------------------------------

@dataclass
class QAOAResult:
    optimal_params: np.ndarray
    optimal_cost: float
    iterations: int
    circuit_depth: int
    target_state_probability: float
    gate_sequence: list[str]
    converged: bool


@dataclass
class PuzzleCircuit:
    """Represents a quantum puzzle circuit to be optimised."""
    n_qubits: int
    target_state: np.ndarray          # target statevector
    available_gates: list[str] = field(default_factory=lambda: ["H", "X", "CNOT", "RZ", "T"])
    max_depth: int = 6
    p_layers: int = 3                 # QAOA layers


# ---------------------------------------------------------------------------
# QAOA cost / mixer Hamiltonians
# ---------------------------------------------------------------------------

def _build_cost_hamiltonian(n_qubits: int, target_state: np.ndarray) -> qml.Hamiltonian:
    """
    Construct a cost Hamiltonian whose ground state corresponds to `target_state`.
    Uses a diagonal Hamiltonian H = sum_i c_i * Z_i derived from the target bitstring.
    """
    target_probs = np.abs(target_state) ** 2
    dominant_idx = int(np.argmax(target_probs))
    bitstring = format(dominant_idx, f"0{n_qubits}b")

    coeffs: list[float] = []
    obs: list[Any] = []
    for i, bit in enumerate(bitstring):
        sign = -1.0 if bit == "1" else 1.0
        coeffs.append(sign * 0.5)
        obs.append(qml.PauliZ(i))

    return qml.Hamiltonian(coeffs, obs)


def _build_mixer_hamiltonian(n_qubits: int) -> qml.Hamiltonian:
    """Standard transverse-field mixer: H_B = sum_i X_i."""
    coeffs = [-1.0] * n_qubits
    obs = [qml.PauliX(i) for i in range(n_qubits)]
    return qml.Hamiltonian(coeffs, obs)


# ---------------------------------------------------------------------------
# QAOA circuit
# ---------------------------------------------------------------------------

def _make_qaoa_circuit(
    n_qubits: int,
    p: int,
    cost_h: qml.Hamiltonian,
    mixer_h: qml.Hamiltonian,
) -> tuple[qml.QNode, qml.Device]:
    """Return (QNode, device) for QAOA with `p` layers."""
    dev = qml.device("default.qubit", wires=n_qubits)

    @qml.qnode(dev)
    def circuit(gammas: pnp.ndarray, betas: pnp.ndarray) -> float:
        # Initial superposition
        for wire in range(n_qubits):
            qml.Hadamard(wires=wire)
        # QAOA layers
        for layer in range(p):
            qml.ApproxTimeEvolution(cost_h, gammas[layer], 1)
            qml.ApproxTimeEvolution(mixer_h, betas[layer], 1)
        return qml.expval(cost_h)

    return circuit, dev


# ---------------------------------------------------------------------------
# Optimiser
# ---------------------------------------------------------------------------

class QAOAOptimizer:
    """
    Optimises quantum gate sequences for Magic Garden puzzles.

    Usage:
        opt = QAOAOptimizer()
        result = opt.optimise(puzzle_circuit)
    """

    def __init__(
        self,
        max_iterations: int = 200,
        step_size: float = 0.05,
        convergence_tol: float = 1e-4,
    ) -> None:
        self.max_iterations = max_iterations
        self.step_size = step_size
        self.convergence_tol = convergence_tol

    # ------------------------------------------------------------------

    def optimise(self, puzzle: PuzzleCircuit) -> QAOAResult:
        """Run QAOA optimisation for the given puzzle circuit."""
        logger.info(
            "Starting QAOA optimisation: n_qubits=%d, p=%d", puzzle.n_qubits, puzzle.p_layers
        )

        cost_h = _build_cost_hamiltonian(puzzle.n_qubits, puzzle.target_state)
        mixer_h = _build_mixer_hamiltonian(puzzle.n_qubits)
        circuit, dev = _make_qaoa_circuit(puzzle.n_qubits, puzzle.p_layers, cost_h, mixer_h)

        # Initialise parameters
        gammas = pnp.random.uniform(0, np.pi, puzzle.p_layers, requires_grad=True)
        betas = pnp.random.uniform(0, np.pi / 2, puzzle.p_layers, requires_grad=True)

        optimiser = qml.AdamOptimizer(stepsize=self.step_size)

        prev_cost = float("inf")
        converged = False

        for iteration in range(self.max_iterations):
            (gammas, betas), cost = optimiser.step_and_cost(circuit, gammas, betas)
            cost_val = float(cost)

            if iteration % 20 == 0:
                logger.debug("iter=%d cost=%.6f", iteration, cost_val)

            if abs(prev_cost - cost_val) < self.convergence_tol:
                converged = True
                logger.info("QAOA converged at iteration %d, cost=%.6f", iteration, cost_val)
                break
            prev_cost = cost_val

        optimal_params = np.concatenate([np.array(gammas), np.array(betas)])
        gate_sequence = self._extract_gate_sequence(
            puzzle, np.array(gammas), np.array(betas)
        )
        target_prob = self._compute_target_probability(
            puzzle.n_qubits,
            puzzle.p_layers,
            puzzle.target_state,
            np.array(gammas),
            np.array(betas),
            cost_h,
            mixer_h,
        )

        return QAOAResult(
            optimal_params=optimal_params,
            optimal_cost=float(prev_cost),
            iterations=iteration + 1,
            circuit_depth=2 * puzzle.p_layers + 1,
            target_state_probability=target_prob,
            gate_sequence=gate_sequence,
            converged=converged,
        )

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _extract_gate_sequence(
        self,
        puzzle: PuzzleCircuit,
        gammas: np.ndarray,
        betas: np.ndarray,
    ) -> list[str]:
        """Translate QAOA angles into human-readable gate labels for the Unity UI."""
        gates: list[str] = ["H"] * puzzle.n_qubits  # initial superposition layer
        for layer in range(puzzle.p_layers):
            g, b = gammas[layer], betas[layer]
            if abs(g) > np.pi / 2:
                gates.append("CNOT")
            elif abs(g) > np.pi / 4:
                gates.append("RZ")
            else:
                gates.append("X")
            if abs(b) > np.pi / 4:
                gates.append("H")
            else:
                gates.append("T")
        return gates[: puzzle.max_depth]

    @staticmethod
    def _compute_target_probability(
        n_qubits: int,
        p: int,
        target_state: np.ndarray,
        gammas: np.ndarray,
        betas: np.ndarray,
        cost_h: qml.Hamiltonian,
        mixer_h: qml.Hamiltonian,
    ) -> float:
        """Compute ⟨ψ_QAOA|target⟩² for the optimised circuit."""
        dev = qml.device("default.qubit", wires=n_qubits)

        @qml.qnode(dev)
        def state_circuit(g: np.ndarray, b: np.ndarray) -> np.ndarray:
            for wire in range(n_qubits):
                qml.Hadamard(wires=wire)
            for layer in range(p):
                qml.ApproxTimeEvolution(cost_h, g[layer], 1)
                qml.ApproxTimeEvolution(mixer_h, b[layer], 1)
            return qml.state()

        final_state = np.array(state_circuit(gammas, betas))
        overlap = np.abs(np.dot(np.conj(target_state), final_state)) ** 2
        return float(overlap)

    # ------------------------------------------------------------------
    # Puzzle-level helpers used by GardenController backend
    # ------------------------------------------------------------------

    def generate_grover_puzzle(self, n_qubits: int = 3) -> PuzzleCircuit:
        """Generate a Grover search puzzle targeting the |111⟩ state."""
        dim = 2 ** n_qubits
        target = np.zeros(dim, dtype=complex)
        target[-1] = 1.0  # |111⟩
        return PuzzleCircuit(n_qubits=n_qubits, target_state=target, p_layers=2)

    def generate_shor_puzzle(self, n_qubits: int = 4) -> PuzzleCircuit:
        """
        Generate a Shor period-finding puzzle.
        Target state is a uniform superposition — kids learn to 'find the period'.
        """
        dim = 2 ** n_qubits
        target = np.ones(dim, dtype=complex) / np.sqrt(dim)
        return PuzzleCircuit(n_qubits=n_qubits, target_state=target, p_layers=3)

    def generate_entanglement_puzzle(self) -> PuzzleCircuit:
        """Generate a Bell-state entanglement puzzle targeting |Φ+⟩."""
        target = np.array([1 / np.sqrt(2), 0, 0, 1 / np.sqrt(2)], dtype=complex)
        return PuzzleCircuit(n_qubits=2, target_state=target, p_layers=2)
