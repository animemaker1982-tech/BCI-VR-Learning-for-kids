"""
Tests for QAOA Circuit Optimizer
"""
import numpy as np
import pytest

from backend.qaoa.circuit_optimizer import (
    QAOAOptimizer,
    PuzzleCircuit,
    _build_cost_hamiltonian,
    _build_mixer_hamiltonian,
)


@pytest.fixture
def optimizer():
    return QAOAOptimizer(max_iterations=30, step_size=0.1)


# ── Hamiltonian construction ─────────────────────────────────────────────────

def test_cost_hamiltonian_2qubit():
    target = np.array([0, 0, 0, 1], dtype=complex)  # |11⟩
    h = _build_cost_hamiltonian(2, target)
    assert h is not None
    assert len(h.coeffs) == 2


def test_mixer_hamiltonian_3qubit():
    h = _build_mixer_hamiltonian(3)
    assert len(h.coeffs) == 3


# ── Puzzle generation ────────────────────────────────────────────────────────

def test_grover_puzzle_structure(optimizer):
    puzzle = optimizer.generate_grover_puzzle(n_qubits=3)
    assert puzzle.n_qubits == 3
    assert len(puzzle.target_state) == 8
    assert abs(np.linalg.norm(puzzle.target_state) - 1.0) < 1e-6


def test_entanglement_puzzle_bell_state(optimizer):
    puzzle = optimizer.generate_entanglement_puzzle()
    assert puzzle.n_qubits == 2
    # Bell state |Φ+⟩ = [1/√2, 0, 0, 1/√2]
    assert abs(puzzle.target_state[0].real - 1 / np.sqrt(2)) < 1e-6
    assert abs(puzzle.target_state[3].real - 1 / np.sqrt(2)) < 1e-6


def test_shor_puzzle_uniform(optimizer):
    puzzle = optimizer.generate_shor_puzzle(n_qubits=3)
    expected_amp = 1.0 / np.sqrt(8)
    assert all(abs(a.real - expected_amp) < 1e-6 for a in puzzle.target_state)


# ── Optimisation ─────────────────────────────────────────────────────────────

def test_qaoa_optimise_returns_result(optimizer):
    puzzle = optimizer.generate_entanglement_puzzle()
    puzzle.p_layers = 1
    result = optimizer.optimise(puzzle)
    assert result is not None
    assert result.optimal_params is not None
    assert len(result.gate_sequence) > 0
    assert 0.0 <= result.target_state_probability <= 1.0


def test_qaoa_gate_sequence_respects_max_depth(optimizer):
    puzzle = optimizer.generate_grover_puzzle(n_qubits=2)
    puzzle.max_depth = 4
    puzzle.p_layers = 1
    result = optimizer.optimise(puzzle)
    assert len(result.gate_sequence) <= puzzle.max_depth


def test_qaoa_circuit_depth_consistent(optimizer):
    puzzle = optimizer.generate_entanglement_puzzle()
    puzzle.p_layers = 2
    result = optimizer.optimise(puzzle)
    # circuit_depth = 2*p + 1 (layers + initial H)
    assert result.circuit_depth == 2 * puzzle.p_layers + 1
