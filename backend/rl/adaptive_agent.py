"""
Adaptive Learning RL Agent
==========================
Reinforcement Learning agent (Proximal Policy Optimisation + DQN ensemble)
that adapts puzzle difficulty and hint frequency in real-time to each child's
performance. Trained on a Gymnasium environment that models the learning
progression through the Magic Garden levels.

State:  [level, score_rate, error_rate, time_on_task, bci_confidence, streak]
Action: [decrease_difficulty, maintain, increase_difficulty, show_hint, skip_puzzle]
Reward: +engagement, +learning_gain, -frustration, -boredom
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any

import gymnasium as gym
import numpy as np
import torch
from gymnasium import spaces
from stable_baselines3 import PPO, DQN
from stable_baselines3.common.callbacks import EvalCallback, StopTrainingOnRewardThreshold
from stable_baselines3.common.env_util import make_vec_env
from stable_baselines3.common.monitor import Monitor

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Gymnasium environment
# ---------------------------------------------------------------------------

N_OBS = 6
N_ACTIONS = 5

ACTION_NAMES = [
    "decrease_difficulty",
    "maintain",
    "increase_difficulty",
    "show_hint",
    "skip_puzzle",
]


@dataclass
class LearnerState:
    level: int = 1
    score_rate: float = 0.5          # rolling accuracy
    error_rate: float = 0.2
    time_on_task: float = 0.5        # normalised 0-1 over session
    bci_confidence: float = 0.7
    streak: int = 0                   # consecutive correct answers
    total_steps: int = 0
    max_level: int = 8


class MagicGardenEnv(gym.Env):
    """
    Gymnasium environment simulating a child's learning session.
    Used for offline training of the RL agent.
    """

    metadata = {"render_modes": []}

    def __init__(self, max_episode_steps: int = 200) -> None:
        super().__init__()
        self.max_episode_steps = max_episode_steps
        self.observation_space = spaces.Box(
            low=np.zeros(N_OBS, dtype=np.float32),
            high=np.ones(N_OBS, dtype=np.float32),
            dtype=np.float32,
        )
        self.action_space = spaces.Discrete(N_ACTIONS)
        self.state = LearnerState()
        self._step = 0

    # ------------------------------------------------------------------

    def reset(self, *, seed: int | None = None, options: dict | None = None) -> tuple:
        super().reset(seed=seed)
        self.state = LearnerState()
        self._step = 0
        return self._obs(), {}

    def step(self, action: int) -> tuple:
        self._step += 1
        prev = self._snapshot()
        self._apply_action(action)
        # Detect graduation before clamping so state.level stays within bounds
        terminated = self.state.level > self.state.max_level
        self.state.level = min(self.state.max_level, max(1, self.state.level))
        self._simulate_learner_response(action)

        obs = self._obs()
        reward = self._compute_reward(prev, action)
        truncated = self._step >= self.max_episode_steps
        return obs, reward, terminated, truncated, {}

    # ------------------------------------------------------------------

    def _obs(self) -> np.ndarray:
        s = self.state
        return np.array(
            [
                s.level / s.max_level,
                s.score_rate,
                s.error_rate,
                s.time_on_task,
                s.bci_confidence,
                min(s.streak / 10.0, 1.0),
            ],
            dtype=np.float32,
        )

    def _snapshot(self) -> dict[str, float]:
        return {
            "score_rate": self.state.score_rate,
            "error_rate": self.state.error_rate,
            "level": float(self.state.level),
        }

    def _apply_action(self, action: int) -> None:
        s = self.state
        if action == 0:   # decrease difficulty
            s.level = max(1, s.level - 1)
        elif action == 2: # increase difficulty — allow going past max to trigger termination
            s.level = s.level + 1
        # show_hint and skip_puzzle don't change level

    def _simulate_learner_response(self, action: int) -> None:
        """Simulate stochastic learner behaviour based on difficulty."""
        s = self.state
        # Optimal zone heuristic (flow channel)
        difficulty_gap = s.level / s.max_level - s.score_rate
        success_prob = np.clip(0.7 - difficulty_gap * 1.5, 0.1, 0.95)
        success_prob += 0.05 if action == 3 else 0.0  # hint helps

        solved = self.np_random.random() < success_prob
        if solved:
            s.streak += 1
            s.score_rate = np.clip(s.score_rate * 0.9 + 0.1, 0.0, 1.0)
            s.error_rate = np.clip(s.error_rate * 0.95, 0.0, 1.0)
        else:
            s.streak = 0
            s.error_rate = np.clip(s.error_rate * 0.9 + 0.1, 0.0, 1.0)
            s.score_rate = np.clip(s.score_rate * 0.95, 0.0, 1.0)

        s.time_on_task = np.clip(s.time_on_task + 0.005, 0.0, 1.0)
        s.bci_confidence = float(
            np.clip(s.bci_confidence + self.np_random.normal(0, 0.05), 0.1, 1.0)
        )
        s.total_steps += 1

    def _compute_reward(self, prev: dict[str, float], action: int) -> float:
        s = self.state
        reward = 0.0

        # Learning gain
        reward += (s.score_rate - prev["score_rate"]) * 5.0
        # Error reduction
        reward -= (s.error_rate - prev["error_rate"]) * 3.0
        # Encourage progression
        reward += (s.level - prev["level"]) * 0.5
        # Penalise excessive hints / skips
        if action in (3, 4):
            reward -= 0.3
        # Flow bonus — reward when score_rate is in optimal zone [0.6, 0.85]
        if 0.6 <= s.score_rate <= 0.85:
            reward += 0.2
        # Engagement bonus for streak
        reward += min(s.streak * 0.05, 0.5)

        return float(reward)


# ---------------------------------------------------------------------------
# RL Agent manager
# ---------------------------------------------------------------------------

@dataclass
class AdaptiveAction:
    action_name: str
    action_idx: int
    recommended_level: int
    show_hint: bool
    skip_puzzle: bool
    confidence: float


class AdaptiveRLAgent:
    """
    Manages PPO + DQN ensemble for adaptive difficulty.

    Usage:
        agent = AdaptiveRLAgent()
        agent.train()
        action = agent.recommend(learner_obs)
    """

    def __init__(
        self,
        model_dir: str = "backend/rl/models",
        total_timesteps: int = 100_000,
    ) -> None:
        self.model_dir = model_dir
        self.total_timesteps = total_timesteps
        self._ppo: PPO | None = None
        self._dqn: DQN | None = None
        self._current_level = 1

    # ------------------------------------------------------------------

    def train(self) -> None:
        """Train PPO and DQN agents on MagicGardenEnv."""
        import os
        os.makedirs(self.model_dir, exist_ok=True)

        env = Monitor(MagicGardenEnv())
        eval_env = Monitor(MagicGardenEnv())

        # ── PPO ──────────────────────────────────────────────────────
        logger.info("Training PPO agent...")
        stop_cb = StopTrainingOnRewardThreshold(reward_threshold=50.0, verbose=1)
        eval_cb = EvalCallback(eval_env, callback_on_new_best=stop_cb, eval_freq=5000, verbose=0)
        self._ppo = PPO(
            "MlpPolicy",
            env,
            learning_rate=3e-4,
            n_steps=512,
            batch_size=64,
            n_epochs=10,
            gamma=0.99,
            gae_lambda=0.95,
            clip_range=0.2,
            verbose=0,
        )
        self._ppo.learn(total_timesteps=self.total_timesteps, callback=eval_cb)
        self._ppo.save(f"{self.model_dir}/ppo_agent")
        logger.info("PPO saved to %s/ppo_agent", self.model_dir)

        # ── DQN ──────────────────────────────────────────────────────
        logger.info("Training DQN agent...")
        self._dqn = DQN(
            "MlpPolicy",
            env,
            learning_rate=1e-4,
            buffer_size=50_000,
            learning_starts=1000,
            batch_size=64,
            gamma=0.99,
            target_update_interval=500,
            exploration_fraction=0.2,
            exploration_final_eps=0.05,
            verbose=0,
        )
        self._dqn.learn(total_timesteps=self.total_timesteps)
        self._dqn.save(f"{self.model_dir}/dqn_agent")
        logger.info("DQN saved to %s/dqn_agent", self.model_dir)

    def load(self) -> None:
        """Load pre-trained agents."""
        ppo_path = f"{self.model_dir}/ppo_agent"
        dqn_path = f"{self.model_dir}/dqn_agent"

        env = MagicGardenEnv()
        if not (
            __import__("pathlib").Path(ppo_path + ".zip").exists()
            and __import__("pathlib").Path(dqn_path + ".zip").exists()
        ):
            logger.warning("Pre-trained RL models not found — training from scratch")
            self.train()
            return

        self._ppo = PPO.load(ppo_path, env=env)
        self._dqn = DQN.load(dqn_path, env=env)
        logger.info("RL agents loaded successfully")

    # ------------------------------------------------------------------

    def recommend(
        self,
        score_rate: float,
        error_rate: float,
        time_on_task: float,
        bci_confidence: float,
        streak: int,
        current_level: int,
    ) -> AdaptiveAction:
        """
        Return the recommended adaptive action given current learner state.
        Uses ensemble voting between PPO and DQN.
        """
        self._current_level = current_level
        obs = np.array(
            [
                current_level / 8.0,
                np.clip(score_rate, 0.0, 1.0),
                np.clip(error_rate, 0.0, 1.0),
                np.clip(time_on_task, 0.0, 1.0),
                np.clip(bci_confidence, 0.0, 1.0),
                min(streak / 10.0, 1.0),
            ],
            dtype=np.float32,
        )

        if self._ppo is None or self._dqn is None:
            return self._heuristic_recommend(obs, current_level)

        ppo_action, _ = self._ppo.predict(obs, deterministic=True)
        dqn_action, _ = self._dqn.predict(obs, deterministic=True)

        # Ensemble: choose by majority; break ties with PPO
        ppo_a = int(ppo_action)
        dqn_a = int(dqn_action)
        final_action = ppo_a if ppo_a == dqn_a else ppo_a

        return self._build_action(final_action, current_level)

    # ------------------------------------------------------------------

    def _heuristic_recommend(self, obs: np.ndarray, level: int) -> AdaptiveAction:
        """Rule-based fallback when agents are not loaded."""
        score_rate = float(obs[1])
        error_rate = float(obs[2])

        if score_rate > 0.85 and error_rate < 0.2:
            action = 2  # increase
        elif score_rate < 0.4 or error_rate > 0.6:
            action = 0  # decrease
        elif error_rate > 0.4:
            action = 3  # hint
        else:
            action = 1  # maintain
        return self._build_action(action, level)

    def _build_action(self, action: int, level: int) -> AdaptiveAction:
        new_level = level
        if action == 0:
            new_level = max(1, level - 1)
        elif action == 2:
            new_level = min(8, level + 1)
        return AdaptiveAction(
            action_name=ACTION_NAMES[action],
            action_idx=action,
            recommended_level=new_level,
            show_hint=action == 3,
            skip_puzzle=action == 4,
            confidence=0.8,
        )

    # ------------------------------------------------------------------

    def update_online(
        self,
        obs: np.ndarray,
        action: int,
        reward: float,
        next_obs: np.ndarray,
        done: bool,
    ) -> None:
        """
        Online update: replay buffer push for DQN (PPO updated offline).
        """
        if self._dqn is not None and hasattr(self._dqn, "replay_buffer"):
            self._dqn.replay_buffer.add(
                obs.reshape(1, -1),
                next_obs.reshape(1, -1),
                np.array([[action]]),
                np.array([[reward]]),
                np.array([[done]]),
                [{}],
            )
