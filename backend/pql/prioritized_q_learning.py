"""
Prioritised Q-Learning (PQL) — Curriculum Optimiser
====================================================
Implements Prioritised Experience Replay DQN (PER-DQN) for curriculum
sequencing in Quantum Magic Garden.  The agent learns which puzzle orderings
produce the best learning outcomes by prioritising experiences where the
TD-error is highest — i.e., the most surprising / informative transitions.

This complements the RL adaptive agent: while AdaptiveRLAgent controls
difficulty level, PQL decides the *sequence* of puzzle types presented.

References:
    Schaul et al. (2015) "Prioritized Experience Replay" – arXiv:1511.05952
"""

from __future__ import annotations

import logging
import random
from collections import deque
from dataclasses import dataclass, field
from pathlib import Path
from typing import NamedTuple

import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Puzzle types available in the curriculum
# ---------------------------------------------------------------------------

PUZZLE_TYPES = [
    "superposition_intro",
    "hadamard_basic",
    "pauli_x_flip",
    "cnot_entanglement",
    "bell_state",
    "grover_2qubit",
    "grover_3qubit",
    "shor_factor6",
    "shor_factor15",
    "full_circuit_challenge",
]
N_PUZZLE_TYPES = len(PUZZLE_TYPES)

# State: [current_puzzle_idx, mastery_vec (N_PUZZLE_TYPES), time_since_last_error, session_progress]
N_OBS = N_PUZZLE_TYPES + 3
N_ACTIONS = N_PUZZLE_TYPES   # choose which puzzle to present next


# ---------------------------------------------------------------------------
# Segment tree for efficient priority sampling
# ---------------------------------------------------------------------------

class SegmentTree:
    """Min/sum segment tree for O(log n) priority updates and sampling."""

    def __init__(self, capacity: int, operation: str = "sum") -> None:
        self.capacity = capacity
        self._identity = 0.0 if operation == "sum" else float("inf")
        # Initialise tree with identity value so unoccupied slots are neutral
        self._tree = np.full(2 * capacity, self._identity, dtype=np.float64)
        self.op = np.add if operation == "sum" else np.minimum

    def update(self, idx: int, value: float) -> None:
        pos = idx + self.capacity
        self._tree[pos] = value
        pos //= 2
        while pos >= 1:
            self._tree[pos] = self.op(self._tree[2 * pos], self._tree[2 * pos + 1])
            pos //= 2

    def query(self, left: int, right: int) -> float:
        """Query [left, right) range."""
        result = self._identity
        left += self.capacity
        right += self.capacity
        while left < right:
            if left & 1:
                result = self.op(result, self._tree[left])
                left += 1
            if right & 1:
                right -= 1
                result = self.op(result, self._tree[right])
            left //= 2
            right //= 2
        return result

    @property
    def total(self) -> float:
        return float(self._tree[1])

    def retrieve(self, cumsum: float) -> int:
        """Find index where prefix sum reaches cumsum."""
        idx = 1
        while idx < self.capacity:
            if self._tree[2 * idx] > cumsum:
                idx = 2 * idx
            else:
                cumsum -= self._tree[2 * idx]
                idx = 2 * idx + 1
        return idx - self.capacity


# ---------------------------------------------------------------------------
# Prioritised Replay Buffer
# ---------------------------------------------------------------------------

class Transition(NamedTuple):
    obs: np.ndarray
    action: int
    reward: float
    next_obs: np.ndarray
    done: bool


class PrioritisedReplayBuffer:
    """
    Replay buffer with prioritised experience replay.
    Priorities are proportional to |TD error| + ε.
    """

    def __init__(
        self,
        capacity: int = 10_000,
        alpha: float = 0.6,
        beta_start: float = 0.4,
        beta_end: float = 1.0,
        beta_steps: int = 100_000,
        epsilon: float = 1e-6,
    ) -> None:
        self.capacity = capacity
        self.alpha = alpha
        self.epsilon = epsilon
        self.beta = beta_start
        self.beta_end = beta_end
        self.beta_increment = (beta_end - beta_start) / beta_steps

        self._sum_tree = SegmentTree(capacity, "sum")
        self._min_tree = SegmentTree(capacity, "min")
        self._data: list[Transition | None] = [None] * capacity
        self._ptr = 0
        self._size = 0
        self._max_priority = 1.0

    # ------------------------------------------------------------------

    def push(self, transition: Transition) -> None:
        self._data[self._ptr] = transition
        priority = self._max_priority ** self.alpha
        self._sum_tree.update(self._ptr, priority)
        self._min_tree.update(self._ptr, priority)
        self._ptr = (self._ptr + 1) % self.capacity
        self._size = min(self._size + 1, self.capacity)

    def sample(self, batch_size: int) -> tuple:
        """Sample batch_size transitions using priorities. Returns (batch, indices, weights)."""
        indices: list[int] = []
        segment = self._sum_tree.total / batch_size

        for i in range(batch_size):
            lo = segment * i
            hi = segment * (i + 1)
            sample_val = random.uniform(lo, hi)
            indices.append(self._sum_tree.retrieve(sample_val))

        # Importance-sampling weights
        min_prob = self._min_tree.total / self._sum_tree.total
        max_weight = (min_prob * self._size) ** (-self.beta)
        weights = []
        for idx in indices:
            prob = self._sum_tree._tree[idx + self.capacity] / self._sum_tree.total
            weight = (prob * self._size) ** (-self.beta) / max_weight
            weights.append(weight)

        self.beta = min(self.beta_end, self.beta + self.beta_increment)
        batch = [self._data[i] for i in indices]
        return batch, indices, np.array(weights, dtype=np.float32)

    def update_priorities(self, indices: list[int], td_errors: np.ndarray) -> None:
        for idx, err in zip(indices, td_errors):
            priority = (abs(err) + self.epsilon) ** self.alpha
            self._sum_tree.update(idx, priority)
            self._min_tree.update(idx, priority)
            self._max_priority = max(self._max_priority, priority)

    def __len__(self) -> int:
        return self._size


# ---------------------------------------------------------------------------
# Q-Network
# ---------------------------------------------------------------------------

class QNetwork(nn.Module):
    """Dueling DQN network for curriculum sequencing."""

    def __init__(self, n_obs: int = N_OBS, n_actions: int = N_ACTIONS) -> None:
        super().__init__()
        self.shared = nn.Sequential(
            nn.Linear(n_obs, 128), nn.ReLU(),
            nn.Linear(128, 128), nn.ReLU(),
        )
        self.advantage = nn.Sequential(nn.Linear(128, 64), nn.ReLU(), nn.Linear(64, n_actions))
        self.value = nn.Sequential(nn.Linear(128, 64), nn.ReLU(), nn.Linear(64, 1))

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        shared = self.shared(x)
        adv = self.advantage(shared)
        val = self.value(shared)
        return val + adv - adv.mean(dim=-1, keepdim=True)


# ---------------------------------------------------------------------------
# PQL Agent
# ---------------------------------------------------------------------------

@dataclass
class CurriculumAction:
    next_puzzle: str
    puzzle_idx: int
    mastery_estimate: dict[str, float]
    reason: str


class PQLCurriculumAgent:
    """
    Prioritised Q-Learning agent that sequences puzzles to maximise learning.

    The agent's goal: present puzzles in an order that keeps the child in the
    "zone of proximal development" — not too easy, not too hard.

    Usage:
        pql = PQLCurriculumAgent()
        pql.train(n_episodes=500)
        action = pql.recommend_next(mastery_vec, session_progress)
    """

    def __init__(
        self,
        lr: float = 1e-4,
        gamma: float = 0.99,
        batch_size: int = 64,
        buffer_capacity: int = 10_000,
        target_update_freq: int = 500,
        epsilon_start: float = 1.0,
        epsilon_end: float = 0.05,
        epsilon_decay: int = 50_000,
        model_path: str = "backend/pql/pql_model.pt",
    ) -> None:
        self.lr = lr
        self.gamma = gamma
        self.batch_size = batch_size
        self.target_update_freq = target_update_freq
        self.model_path = Path(model_path)

        self.online_net = QNetwork()
        self.target_net = QNetwork()
        self.target_net.load_state_dict(self.online_net.state_dict())
        self.target_net.eval()

        self.optimiser = optim.Adam(self.online_net.parameters(), lr=lr)
        self.buffer = PrioritisedReplayBuffer(capacity=buffer_capacity)

        self.epsilon = epsilon_start
        self.epsilon_end = epsilon_end
        self.epsilon_decay = epsilon_decay
        self._steps = 0

        self._mastery = np.zeros(N_PUZZLE_TYPES, dtype=np.float32)

    # ------------------------------------------------------------------

    def train(self, n_episodes: int = 500) -> dict[str, float]:
        """Self-play training loop using simulated learner model."""
        ep_returns = []
        for episode in range(n_episodes):
            obs = self._reset_episode()
            ep_return = 0.0
            for _ in range(50):
                action = self._select_action(obs)
                next_obs, reward, done = self._sim_step(obs, action)
                self.buffer.push(Transition(obs, action, reward, next_obs, done))
                ep_return += reward
                obs = next_obs

                if len(self.buffer) >= self.batch_size:
                    self._update()

                if done:
                    break

            ep_returns.append(ep_return)
            if episode % 50 == 0:
                avg = np.mean(ep_returns[-50:])
                logger.info("PQL episode=%d avg_return=%.3f eps=%.3f", episode, avg, self.epsilon)

        self._save()
        return {"mean_return": float(np.mean(ep_returns[-100:])), "episodes": n_episodes}

    def recommend_next(
        self,
        mastery_vec: np.ndarray,
        session_progress: float,
        time_since_error: float,
        current_puzzle_idx: int,
    ) -> CurriculumAction:
        """Return the next puzzle recommendation."""
        obs = self._build_obs(mastery_vec, session_progress, time_since_error, current_puzzle_idx)
        obs_t = torch.tensor(obs, dtype=torch.float32).unsqueeze(0)

        self.online_net.eval()
        with torch.no_grad():
            q_vals = self.online_net(obs_t).squeeze().numpy()

        # Mask already-mastered puzzles (mastery > 0.9)
        for i, m in enumerate(mastery_vec):
            if m > 0.9:
                q_vals[i] -= 100.0  # soft mask

        next_puzzle_idx = int(np.argmax(q_vals))
        mastery_dict = {p: float(m) for p, m in zip(PUZZLE_TYPES, mastery_vec)}

        return CurriculumAction(
            next_puzzle=PUZZLE_TYPES[next_puzzle_idx],
            puzzle_idx=next_puzzle_idx,
            mastery_estimate=mastery_dict,
            reason=f"Q-value: {q_vals[next_puzzle_idx]:.3f}",
        )

    def update_mastery(self, puzzle_idx: int, success: bool, mastery_vec: np.ndarray) -> np.ndarray:
        """Update mastery estimate using exponential moving average."""
        delta = 0.1 if success else -0.05
        mastery_vec = mastery_vec.copy()
        mastery_vec[puzzle_idx] = float(np.clip(mastery_vec[puzzle_idx] + delta, 0.0, 1.0))
        return mastery_vec

    # ------------------------------------------------------------------

    def _select_action(self, obs: np.ndarray) -> int:
        self.epsilon = max(
            self.epsilon_end, 1.0 - self._steps / self.epsilon_decay
        )
        if random.random() < self.epsilon:
            return random.randrange(N_ACTIONS)
        obs_t = torch.tensor(obs, dtype=torch.float32).unsqueeze(0)
        self.online_net.eval()
        with torch.no_grad():
            return int(self.online_net(obs_t).argmax(dim=1).item())

    def _update(self) -> None:
        self._steps += 1
        batch, indices, weights = self.buffer.sample(self.batch_size)
        obs_b = torch.tensor(np.stack([t.obs for t in batch]), dtype=torch.float32)
        act_b = torch.tensor([t.action for t in batch], dtype=torch.long)
        rew_b = torch.tensor([t.reward for t in batch], dtype=torch.float32)
        nobs_b = torch.tensor(np.stack([t.next_obs for t in batch]), dtype=torch.float32)
        done_b = torch.tensor([t.done for t in batch], dtype=torch.float32)
        w_b = torch.tensor(weights, dtype=torch.float32)

        self.online_net.train()
        q_curr = self.online_net(obs_b).gather(1, act_b.unsqueeze(1)).squeeze(1)
        with torch.no_grad():
            # Double DQN: online net selects action, target net evaluates
            best_actions = self.online_net(nobs_b).argmax(dim=1)
            q_next = self.target_net(nobs_b).gather(1, best_actions.unsqueeze(1)).squeeze(1)
            q_target = rew_b + self.gamma * q_next * (1 - done_b)

        td_errors = (q_target - q_curr).detach().numpy()
        self.buffer.update_priorities(indices, td_errors)

        loss = (w_b * (q_curr - q_target.detach()) ** 2).mean()
        self.optimiser.zero_grad()
        loss.backward()
        nn.utils.clip_grad_norm_(self.online_net.parameters(), 10.0)
        self.optimiser.step()

        if self._steps % self.target_update_freq == 0:
            self.target_net.load_state_dict(self.online_net.state_dict())

    # ------------------------------------------------------------------

    def _reset_episode(self) -> np.ndarray:
        self._mastery = np.zeros(N_PUZZLE_TYPES, dtype=np.float32)
        return self._build_obs(self._mastery, 0.0, 0.0, 0)

    def _sim_step(self, obs: np.ndarray, action: int) -> tuple[np.ndarray, float, bool]:
        """Simulated learner response for self-play training."""
        mastery_vec = obs[: N_PUZZLE_TYPES].copy()
        session_progress = float(obs[N_PUZZLE_TYPES])

        # Simulate success probability based on mastery
        success_prob = max(0.1, 1.0 - mastery_vec[action] * 0.3)
        success = random.random() < success_prob
        mastery_vec = self.update_mastery(action, success, mastery_vec)

        reward = 1.0 if success else -0.2
        # Bonus for teaching un-mastered puzzles
        if mastery_vec[action] < 0.5:
            reward += 0.3
        # Penalty for over-drilling mastered puzzles
        if mastery_vec[action] > 0.8:
            reward -= 0.4

        session_progress = min(1.0, session_progress + 0.02)
        done = session_progress >= 1.0
        next_obs = self._build_obs(mastery_vec, session_progress, 0.0, action)
        return next_obs, reward, done

    def _build_obs(
        self,
        mastery_vec: np.ndarray,
        session_progress: float,
        time_since_error: float,
        current_puzzle_idx: int,
    ) -> np.ndarray:
        return np.concatenate([
            mastery_vec,
            [session_progress, min(time_since_error / 60.0, 1.0), current_puzzle_idx / N_PUZZLE_TYPES],
        ]).astype(np.float32)

    def _save(self) -> None:
        self.model_path.parent.mkdir(parents=True, exist_ok=True)
        torch.save(self.online_net.state_dict(), self.model_path)
        logger.info("PQL model saved to %s", self.model_path)

    def load(self) -> None:
        if not self.model_path.exists():
            logger.warning("PQL model not found, using untrained network")
            return
        self.online_net.load_state_dict(torch.load(self.model_path, weights_only=True))
        self.target_net.load_state_dict(self.online_net.state_dict())
        logger.info("PQL model loaded from %s", self.model_path)
