# Quantum Magic Garden 🌿✨

**Mind-powered VR adventures that teach kids quantum computing — using non-invasive BCI on Meta Quest.**

## Vision

Quantum Magic Garden is an immersive educational XR concept designed to help children explore quantum computing through play, imagination, and embodied interaction.

Instead of teaching quantum concepts as abstract theory alone, the experience turns them into something visual, intuitive, and interactive. Children enter a living world where focus, curiosity, and experimentation help them understand ideas like superposition, measurement, entanglement, and quantum search.

Built for **Meta Quest 3/3S standalone**, the long-term vision includes optional **non-invasive BCI integration** so players can interact using cognitive states such as focus and relaxation, with fallback support for hand tracking and eye gaze.

## Why This Matters

Quantum technologies are shaping the future, but most people encounter them only as intimidating abstractions. Quantum Magic Garden aims to close that gap by making advanced ideas feel approachable, playful, and alive.

This project is especially compelling for:

- **Schools and STEM programs**
- **Neurodiverse learners** who may benefit from alternative interaction models
- **Quantum outreach and public education**
- **Edtech licensing and immersive curriculum opportunities**

## Core Experience

- **Immersive Quantum Garden** with rich environmental storytelling
- **BCI-assisted interaction** through EEG/LSL pipelines
- **Fallback input modes** such as hand tracking and eye gaze
- **Quantum logic playground** for gates like H, X, and CNOT
- **Puzzle-based learning** inspired by Grover's and Shor's algorithms
- **Gamified progression** that rewards exploration and understanding

## Architecture Overview

The project concept is strongest when understood as a layered system:

### 1. Experience Layer
The child-facing VR world: environments, quests, progression, and emotional engagement.

### 2. Interaction Layer
BCI inputs, focus/relaxation mapping, and fallback interaction paths like hand tracking and eye gaze.

### 3. Simulation Layer
Toy quantum state logic and gate behavior that support educational mechanics.

### 4. Visual Learning Layer
Effects, metaphors, particles, and spatial representations that make invisible concepts visible.

### 5. Platform Layer
Unity, Meta Quest deployment, performance constraints, and XR runtime integration.

## Tech Direction

- **Engine**: Unity 2022.3 LTS (URP)
- **Platform**: Meta Quest 3/3S standalone
- **XR Tooling**: Meta XR SDK + XR Interaction Toolkit
- **BCI Integration**: LSL / EEG-compatible streaming
- **Simulation**: Lightweight C# quantum state systems
- **Visuals**: Particle systems, post-processing, shaders, interactive environmental feedback

## Builder's Note

This repository reflects more than a product idea. It reflects a builder mindset rooted in resilience, imagination, and long-term vision.

Last year, I was in a wheelchair and had to learn how to walk again after being told I might never walk again. That experience changed how I approach everything: one step at a time, one system at a time, one breakthrough at a time.

I bring that same mindset into what I build.

I believe technology should not only work — it should invite people into possibility.

## Collaboration

If you are interested in immersive learning, BCI, quantum education, XR systems, partnerships, or helping shape the future direction of this project, feel free to reach out.

**Email**: lucasfaure936@gmail.com

## Published Offering

This repository is the public-facing overview for **Quantum Magic Garden** and its related QST trading service work.

The project is now being presented for **acquisition or strategic partnership** with a **starting price of USD $100,000**.

Potential buyers or partners may be interested in:

- The Quantum Magic Garden concept and brand direction
- The educational XR + BCI product vision
- The guarded QST Coinbase trading service backend
- Early-stage collaboration, licensing, or commercialization opportunities

Serious inquiries can be sent directly to **lucasfaure936@gmail.com**.

## Repository Note

This repository currently also contains a separate static outreach landing page in `index.html` and `styles.css`. That page is a lightweight public-facing collaboration draft and is distinct from the core Quantum Magic Garden product vision.


## QST Coinbase Trading Service

This repository now also includes a standalone backend service at `backend/qst-coinbase-agent` for guarded Coinbase trading workflows.

The service is designed to keep live trading disabled until all of the following are true:

- EODHD historical data has been imported
- Coinbase API credentials are present in environment variables
- Risk limits have been configured
- Manual approval has been recorded
- `COINBASE_ALLOW_LIVE_TRADING=true` has been set explicitly

### Service endpoints

- `GET /health`
- `GET /api/status`
- `POST /api/historical-data/upload`
- `POST /api/config/coinbase`
- `POST /api/config/risk`
- `POST /api/trading/approve`
- `POST /api/trading/start`
- `POST /api/trading/stop`
- `POST /api/orders/market`

### Running the service

```bash
cd backend/qst-coinbase-agent
export COINBASE_API_KEY_NAME="your-coinbase-api-key-name"
export COINBASE_PRIVATE_KEY="-----BEGIN PRIVATE KEY-----..."
export COINBASE_ALLOW_LIVE_TRADING=false
npm start
```

Use `npm test` in the same directory to run the built-in Node.js test suite.
