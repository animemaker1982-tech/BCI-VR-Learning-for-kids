# Quantum Magic Garden 🌿✨

**Mind-powered VR adventures that teach kids quantum computing — using non-invasive BCI on Meta Quest.**

A groundbreaking educational VR experience where children (ages 8–14) wear affordable EEG headsets (OpenBCI, Emotiv, Muse) to control quantum gates with their thoughts. Superposition becomes a blooming flower, entanglement links fireflies across the garden, Grover's search is an epic treasure hunt, and Shor's algorithm cracks "unbreakable" magic locks — all while learning real quantum concepts through play.

Built for **Meta Quest 3/3S standalone**, with optional BCI integration for true "mind magic." Designed to spark quantum literacy in neurodiverse kids, schools, and the next generation of Australian quantum talent.

[![Unity](https://img.shields.io/badge/Engine-Unity_2022.3+-000000?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com)
[![Meta Quest](https://img.shields.io/badge/Platform-Meta_Quest_3-0078D4?style=for-the-badge&logo=meta&logoColor=white)](https://www.meta.com/quest/)
[![BCI](https://img.shields.io/badge/BCI-OpenBCI_/_Emotiv-blueviolet?style=for-the-badge)](https://openbci.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)

## Why This Matters in 2026

Quantum technologies are exploding — Australia leads with Silicon Quantum Computing, UniMelb quantum hubs, and national post-quantum crypto pushes. Yet kids (and most adults) struggle with abstract concepts like superposition, entanglement, Grover's quadratic speedup, and Shor's factoring threat to RSA/Web3 security.

**Quantum Magic Garden** makes the invisible visible and interactive:
- BCI focus → applies Hadamard (superposition sparkles!)
- Relax → measures & collapses reality
- Mind-powered puzzles teach Grover (find hidden treasure √N times faster) and Shor (break magic locks with quantum period-finding)

Perfect for:
- Schools & STEM programs
- Neurodiverse learners (no controllers needed)
- Quantum outreach (post-quantum awareness)
- Edtech SaaS licensing

## Core Features (Current Prototype)

- **Immersive Quantum Garden** — Floating Bloch spheres, probability clouds, entangled fireflies
- **BCI Control** (via LSL streaming):
  - Attention (focus) → cast gates (H, X, CNOT)
  - Relaxation → measure & collapse
- **Quantum Logic Gates Playground**
  - Pauli-X (Flip Spell)
  - Hadamard (Superposition Sparkle)
  - CNOT (Entanglement Link)
- **Grover's Treasure Hunt** — Quadratic speedup visualized as amplifying the "magic firefly"
- **Shor's Master Key Quest** — Factor "unbreakable" crystal locks (toy RSA demo) → shows real-world crypto implications
- **Fallback Modes** — Hand tracking / eye gaze for non-BCI play
- **Gamified Progression** — Levels unlock new visuals & puzzles

## Tech Stack

- **Engine**: Unity 2022.3 LTS (URP) + Meta XR SDK + XR Interaction Toolkit
- **Quantum Simulation**: Lightweight C# statevector (single/multi-qubit toy impl) — extensible to Qiskit/Cirq bridge
- **BCI Integration**: LSL (Lab Streaming Layer) via LSL4Unity asset — supports OpenBCI Cyton/Ganglion, Emotiv EPOC/Insight, Muse
- **Visuals**: Particle systems, bloom/post-processing, custom shaders for Bloch spheres & probability waves
- **Deployment**: Android APK for Quest standalone (via SideQuest or Meta App Lab)

## Quick Start (Developer Setup)

1. Clone repo:
   ```bash
   git clone https://github.com/yourusername/quantum-magic-garden.git
