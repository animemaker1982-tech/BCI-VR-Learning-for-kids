using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using QuantumMagicGarden.Quantum;
using QuantumMagicGarden.Garden;

namespace QuantumMagicGarden.Puzzles
{
    /// <summary>
    /// Grover's Treasure Hunt Puzzle.
    ///
    /// Goal: apply the Grover oracle + diffusion operator to amplify the
    /// amplitude of the marked "treasure" state, then measure it.
    ///
    /// Difficulty scales the number of qubits (2–4) and number of Grover
    /// iterations the kid must perform using their BCI focus.
    /// </summary>
    public class GroverPuzzle : MonoBehaviour, IPuzzle
    {
        // ── IPuzzle ──────────────────────────────────────────────────────
        public int PuzzleIndex => 5;   // matches PUZZLE_TYPES[5] "grover_2qubit"

        // ── Inspector ────────────────────────────────────────────────────
        [Header("Visual")]
        [SerializeField] private Transform[] treasureFireflies;
        [SerializeField] private Renderer[] fireflySpheres;
        [SerializeField] private ParticleSystem amplifyParticles;
        [SerializeField] private TMPro.TextMeshProUGUI instructionText;
        [SerializeField] private TMPro.TextMeshProUGUI iterationCountText;
        [SerializeField] private TMPro.TextMeshProUGUI probabilityText;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip amplifySound;

        // ── Private ──────────────────────────────────────────────────────
        private QuantumSimulator _sim;
        private int _targetState;
        private int _numQubits;
        private int _optimalIterations;
        private int _groverIterations;
        private int _difficultyLevel;

        private static readonly Color DimColor    = new(0.1f, 0.1f, 0.3f);
        private static readonly Color BrightColor = new(1.0f, 0.9f, 0.2f);

        // ── IPuzzle implementation ────────────────────────────────────────

        public void Initialise(QuantumSimulator sim, int difficultyLevel)
        {
            _difficultyLevel = difficultyLevel;
            _numQubits = difficultyLevel <= 2 ? 2 : (difficultyLevel <= 5 ? 3 : 4);
            _sim = sim;
            _targetState = Random.Range(1, 1 << _numQubits);  // any non-|0⟩ state
            _optimalIterations = Mathf.RoundToInt(
                Mathf.PI / 4f * Mathf.Sqrt(1 << _numQubits)
            );
            _groverIterations = 0;

            // Initialise superposition
            ApplyInitialHadamards();
            UpdateUI();
            UpdateFireflies(_sim.GetProbabilities());

            Debug.Log($"[Grover] n={_numQubits}, target=|{_targetState}⟩, optimal={_optimalIterations} iters");
        }

        public void OnGateApplied(QuantumGate gate, QuantumSimulator sim)
        {
            // Hadamard triggers one full Grover iteration
            if (gate.Type == GateType.Hadamard)
            {
                ApplyGroverIteration();
                UpdateFireflies(sim.GetProbabilities());
                if (amplifyParticles != null) amplifyParticles.Play();
                if (audioSource != null && amplifySound != null)
                    audioSource.PlayOneShot(amplifySound);
            }
            UpdateUI();
        }

        public bool EvaluateOutcome(int[] measuredBits, QuantumSimulator sim)
        {
            int measured = 0;
            for (int q = 0; q < measuredBits.Length; q++)
                measured |= measuredBits[q] << q;

            bool success = measured == _targetState;
            if (instructionText != null)
                instructionText.text = success
                    ? $"🎉 Found the treasure! The magic firefly was at position {_targetState}!"
                    : $"😅 Missed! The treasure was at {_targetState}. Try more Grover iterations!";
            return success;
        }

        public void OnReset(QuantumSimulator sim)
        {
            _groverIterations = 0;
            ApplyInitialHadamards();
            UpdateFireflies(sim.GetProbabilities());
            UpdateUI();
        }

        public void OnDifficultyChanged(int newLevel)
        {
            _difficultyLevel = newLevel;
            // Full reinit handled by GardenController respawning
        }

        // ── Grover circuit ────────────────────────────────────────────────

        private void ApplyInitialHadamards()
        {
            for (int q = 0; q < _numQubits; q++)
                _sim.ApplyGate(QuantumGate.H(q));
        }

        private void ApplyGroverIteration()
        {
            _groverIterations++;
            ApplyOracle(_targetState);
            ApplyDiffusion();
        }

        /// <summary>Phase-flip oracle: flips the sign of |target⟩.</summary>
        private void ApplyOracle(int target)
        {
            // Flip qubits where target bit is 0 (so target → |11…1⟩)
            for (int q = 0; q < _numQubits; q++)
                if (((target >> q) & 1) == 0)
                    _sim.ApplyGate(QuantumGate.X(q));

            // Multi-controlled Z (implemented as H + Toffoli/CNOT chain + H)
            if (_numQubits == 2)
            {
                _sim.ApplyGate(QuantumGate.H(1));
                _sim.ApplyGate(QuantumGate.CNOT(0, 1));
                _sim.ApplyGate(QuantumGate.H(1));
            }
            else if (_numQubits == 3)
            {
                _sim.ApplyGate(QuantumGate.H(2));
                _sim.ApplyGate(QuantumGate.Toffoli(0, 1, 2));
                _sim.ApplyGate(QuantumGate.H(2));
            }

            // Undo bit flips
            for (int q = 0; q < _numQubits; q++)
                if (((target >> q) & 1) == 0)
                    _sim.ApplyGate(QuantumGate.X(q));
        }

        /// <summary>Grover diffusion (inversion about the mean).</summary>
        private void ApplyDiffusion()
        {
            for (int q = 0; q < _numQubits; q++)
            {
                _sim.ApplyGate(QuantumGate.H(q));
                _sim.ApplyGate(QuantumGate.X(q));
            }

            // Multi-controlled phase flip on |0…0⟩
            if (_numQubits == 2)
            {
                _sim.ApplyGate(QuantumGate.H(1));
                _sim.ApplyGate(QuantumGate.CNOT(0, 1));
                _sim.ApplyGate(QuantumGate.H(1));
            }
            else if (_numQubits == 3)
            {
                _sim.ApplyGate(QuantumGate.H(2));
                _sim.ApplyGate(QuantumGate.Toffoli(0, 1, 2));
                _sim.ApplyGate(QuantumGate.H(2));
            }

            for (int q = 0; q < _numQubits; q++)
            {
                _sim.ApplyGate(QuantumGate.X(q));
                _sim.ApplyGate(QuantumGate.H(q));
            }
        }

        // ── Visual updates ────────────────────────────────────────────────

        private void UpdateFireflies(float[] probs)
        {
            for (int i = 0; i < fireflySpheres.Length && i < probs.Length; i++)
            {
                if (fireflySpheres[i] != null)
                {
                    float intensity = Mathf.Sqrt(probs[i]);
                    fireflySpheres[i].material.color = Color.Lerp(DimColor, BrightColor, intensity);
                    float scale = 0.3f + intensity * 1.2f;
                    fireflySpheres[i].transform.localScale = Vector3.one * scale;
                }
            }
        }

        private void UpdateUI()
        {
            if (instructionText != null)
                instructionText.text = _groverIterations == 0
                    ? "🔮 Focus your mind to cast the Grover oracle!\n" +
                      $"The treasure is hidden in {1 << _numQubits} positions.\n" +
                      $"Optimal: {_optimalIterations} iteration{(_optimalIterations > 1 ? "s" : "")}."
                    : $"✨ Grover iteration {_groverIterations}/{_optimalIterations}.\n" +
                      "Watch the bright firefly! Relax to measure when ready.";

            if (iterationCountText != null)
                iterationCountText.text = $"Iterations: {_groverIterations}/{_optimalIterations}";

            if (probabilityText != null)
            {
                float[] probs = _sim.GetProbabilities();
                float targetProb = _targetState < probs.Length ? probs[_targetState] : 0f;
                probabilityText.text = $"Treasure probability: {targetProb * 100f:F1}%";
            }
        }
    }
}
