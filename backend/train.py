"""
Standalone training script — pre-trains RL and PQL agents.
Run via: python -m backend.train
Or via docker compose: docker compose --profile train up trainer
"""
from __future__ import annotations

import logging

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

from backend.rl.adaptive_agent import AdaptiveRLAgent
from backend.pql.prioritized_q_learning import PQLCurriculumAgent


def main() -> None:
    logging.info("Training RL agent (PPO + DQN)…")
    rl = AdaptiveRLAgent(total_timesteps=50_000)
    rl.train()

    logging.info("Training PQL curriculum agent…")
    pql = PQLCurriculumAgent()
    pql.train(n_episodes=200)

    logging.info("Training complete! Models saved.")


if __name__ == "__main__":
    main()
