"""
Tests for Adaptive RL Agent + MagicGardenEnv
"""
import numpy as np
import pytest
import gymnasium as gym

from backend.rl.adaptive_agent import (
    AdaptiveRLAgent,
    MagicGardenEnv,
    AdaptiveAction,
    ACTION_NAMES,
    N_OBS,
    N_ACTIONS,
)


# ── Environment ───────────────────────────────────────────────────────────────

@pytest.fixture
def env():
    return MagicGardenEnv()


def test_env_obs_space(env):
    assert env.observation_space.shape == (N_OBS,)
    assert env.action_space.n == N_ACTIONS


def test_env_reset_obs_range(env):
    obs, _ = env.reset(seed=42)
    assert obs.shape == (N_OBS,)
    assert np.all(obs >= 0.0)
    assert np.all(obs <= 1.0)


def test_env_step_valid_actions(env):
    env.reset(seed=0)
    for action in range(N_ACTIONS):
        obs, _ = env.reset(seed=action)
        next_obs, reward, terminated, truncated, info = env.step(action)
        assert next_obs.shape == (N_OBS,)
        assert isinstance(reward, float)
        assert isinstance(terminated, bool)


def test_env_level_increase_on_good_performance(env):
    obs, _ = env.reset(seed=0)
    env.state.score_rate = 0.95
    env.state.error_rate = 0.05
    initial_level = env.state.level
    env.step(2)  # increase difficulty
    assert env.state.level >= initial_level


def test_env_level_clamp(env):
    env.reset()
    env.state.level = 8
    env.step(2)
    assert env.state.level <= 8


def test_env_terminates_at_max_level(env):
    obs, _ = env.reset()
    env.state.level = 8
    obs, reward, terminated, truncated, _ = env.step(2)
    assert terminated


# ── Heuristic agent ───────────────────────────────────────────────────────────

@pytest.fixture
def agent():
    return AdaptiveRLAgent()


def test_agent_heuristic_low_performance(agent):
    action = agent.recommend(
        score_rate=0.2, error_rate=0.7, time_on_task=0.3,
        bci_confidence=0.5, streak=0, current_level=3
    )
    assert isinstance(action, AdaptiveAction)
    assert action.action_name in ACTION_NAMES
    # Should decrease or show hint when performance is low
    assert action.action_name in ("decrease_difficulty", "show_hint", "maintain")


def test_agent_heuristic_high_performance(agent):
    action = agent.recommend(
        score_rate=0.95, error_rate=0.05, time_on_task=0.5,
        bci_confidence=0.9, streak=10, current_level=2
    )
    assert action.action_name in ("increase_difficulty", "maintain")


def test_agent_returns_valid_level(agent):
    for level in range(1, 9):
        action = agent.recommend(0.5, 0.3, 0.4, 0.7, 2, level)
        assert 1 <= action.recommended_level <= 8


def test_agent_online_update_does_not_crash(agent):
    obs      = np.zeros(N_OBS, dtype=np.float32)
    next_obs = np.ones(N_OBS, dtype=np.float32) * 0.5
    # Should not raise even without loaded models
    agent.update_online(obs, 1, 0.5, next_obs, False)
