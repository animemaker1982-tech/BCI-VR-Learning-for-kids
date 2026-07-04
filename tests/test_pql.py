"""
Tests for Prioritised Q-Learning (PQL) Curriculum Agent
"""
import numpy as np
import pytest

from backend.pql.prioritized_q_learning import (
    PQLCurriculumAgent,
    PrioritisedReplayBuffer,
    SegmentTree,
    Transition,
    N_OBS,
    N_PUZZLE_TYPES,
    PUZZLE_TYPES,
)


# ── Segment tree ──────────────────────────────────────────────────────────────

def test_segment_tree_sum_single():
    tree = SegmentTree(8, "sum")
    tree.update(3, 2.5)
    assert abs(tree.total - 2.5) < 1e-6


def test_segment_tree_sum_multiple():
    tree = SegmentTree(8, "sum")
    for i in range(4):
        tree.update(i, float(i + 1))
    assert abs(tree.total - 10.0) < 1e-6


def test_segment_tree_retrieve():
    tree = SegmentTree(8, "sum")
    for i in range(4):
        tree.update(i, 1.0)
    idx = tree.retrieve(2.5)
    assert 0 <= idx < 4


# ── Replay buffer ─────────────────────────────────────────────────────────────

@pytest.fixture
def buffer():
    return PrioritisedReplayBuffer(capacity=100)


def test_buffer_push_and_len(buffer):
    for i in range(10):
        t = Transition(
            obs=np.zeros(N_OBS, dtype=np.float32),
            action=0,
            reward=1.0,
            next_obs=np.zeros(N_OBS, dtype=np.float32),
            done=False,
        )
        buffer.push(t)
    assert len(buffer) == 10


def test_buffer_sample_returns_correct_size(buffer):
    for _ in range(20):
        buffer.push(Transition(
            np.zeros(N_OBS, np.float32), 0, 0.0,
            np.zeros(N_OBS, np.float32), False,
        ))
    batch, indices, weights = buffer.sample(8)
    assert len(batch) == 8
    assert len(indices) == 8
    assert weights.shape == (8,)


def test_buffer_priority_update(buffer):
    for _ in range(20):
        buffer.push(Transition(
            np.zeros(N_OBS, np.float32), 0, 0.0,
            np.zeros(N_OBS, np.float32), False,
        ))
    _, indices, _ = buffer.sample(5)
    errors = np.random.rand(5)
    buffer.update_priorities(indices, errors)  # should not raise


# ── PQL Agent ────────────────────────────────────────────────────────────────

@pytest.fixture
def agent():
    return PQLCurriculumAgent()


def test_agent_recommend_returns_valid_puzzle(agent):
    mastery = np.zeros(N_PUZZLE_TYPES, dtype=np.float32)
    action = agent.recommend_next(mastery, 0.1, 0.0, 0)
    assert action.next_puzzle in PUZZLE_TYPES
    assert 0 <= action.puzzle_idx < N_PUZZLE_TYPES


def test_agent_recommend_avoids_mastered_puzzles(agent):
    mastery = np.ones(N_PUZZLE_TYPES, dtype=np.float32)
    mastery[3] = 0.0   # only puzzle 3 not mastered
    action = agent.recommend_next(mastery, 0.5, 5.0, 0)
    assert action.puzzle_idx == 3


def test_agent_mastery_update_success(agent):
    mastery = np.array([0.5] * N_PUZZLE_TYPES, dtype=np.float32)
    new_mastery = agent.update_mastery(2, True, mastery)
    assert new_mastery[2] > 0.5
    assert new_mastery[2] <= 1.0


def test_agent_mastery_update_failure(agent):
    mastery = np.array([0.5] * N_PUZZLE_TYPES, dtype=np.float32)
    new_mastery = agent.update_mastery(2, False, mastery)
    assert new_mastery[2] < 0.5
    assert new_mastery[2] >= 0.0


def test_agent_mastery_clamp(agent):
    mastery = np.ones(N_PUZZLE_TYPES, dtype=np.float32)
    new_mastery = agent.update_mastery(0, True, mastery)
    assert new_mastery[0] <= 1.0
    mastery = np.zeros(N_PUZZLE_TYPES, dtype=np.float32)
    new_mastery = agent.update_mastery(0, False, mastery)
    assert new_mastery[0] >= 0.0


def test_short_training(agent):
    """Run a very short training loop to verify no crash."""
    metrics = agent.train(n_episodes=5)
    assert "mean_return" in metrics
    assert "episodes" in metrics
