using System.Collections;
using UnityEngine;
using QuantumMagicGarden.Quantum;
using QuantumMagicGarden.Garden;

namespace QuantumMagicGarden.Puzzles
{
    /// <summary>
    /// Entanglement Puzzle — create a Bell state by entangling two fireflies.
    ///
    /// Goal: apply H to qubit 0, then CNOT(0,1) to create |Φ+⟩ = (|00⟩+|11⟩)/√2.
    /// When measured, both fireflies always flash the same colour — demonstrating
    /// quantum entanglement in a kid-friendly way.
    /// </summary>
    public class EntanglementPuzzle : MonoBehaviour, IPuzzle
    {
        public int PuzzleIndex => 3;   // "cnot_entanglement"

        [Header("Visual")]
        [SerializeField] private Renderer firefly0;
        [SerializeField] private Renderer firefly1;
        [SerializeField] private LineRenderer entanglementLink;
        [SerializeField] private ParticleSystem entangleParticles;
        [SerializeField] private TMPro.TextMeshProUGUI instructionText;
        [SerializeField] private TMPro.TextMeshProUGUI stateText;

        private QuantumSimulator _sim;
        private bool _hadamardApplied;
        private bool _cnotApplied;

        private static readonly Color[] StateColors =
        {
            new(0.2f, 0.2f, 1.0f),   // |00⟩ — blue
            new(1.0f, 0.5f, 0.0f),   // |01⟩ — orange
            new(0.0f, 1.0f, 0.5f),   // |10⟩ — green
            new(1.0f, 0.9f, 0.0f),   // |11⟩ — gold
        };

        public void Initialise(QuantumSimulator sim, int difficultyLevel)
        {
            _sim = sim;
            _hadamardApplied = false;
            _cnotApplied = false;
            UpdateUI();
            UpdateFireflies(_sim.GetProbabilities());
            SetLinkVisible(false);
        }

        public void OnGateApplied(QuantumGate gate, QuantumSimulator sim)
        {
            if (gate.Type == GateType.Hadamard && !_hadamardApplied)
            {
                _hadamardApplied = true;
                if (entangleParticles != null) entangleParticles.Play();
            }
            else if (gate.Type == GateType.CNOT && _hadamardApplied && !_cnotApplied)
            {
                _cnotApplied = true;
                SetLinkVisible(true);
                StartCoroutine(PulseLink());
            }
            UpdateFireflies(sim.GetProbabilities());
            UpdateUI();
        }

        public bool EvaluateOutcome(int[] measuredBits, QuantumSimulator sim)
        {
            // Bell state: q0 and q1 should always be equal after measurement
            bool entangled = measuredBits.Length >= 2 && measuredBits[0] == measuredBits[1];

            if (instructionText != null)
                instructionText.text = entangled
                    ? "🔗 The fireflies are ENTANGLED!\n" +
                      $"Both measured: |{measuredBits[0]}{measuredBits[1]}⟩\n" +
                      "No matter where they are — they always match!"
                    : "The fireflies aren't entangled yet.\n" +
                      "First apply H to qubit 0, then CNOT!";

            return entangled && _hadamardApplied && _cnotApplied;
        }

        public void OnReset(QuantumSimulator sim)
        {
            _hadamardApplied = false;
            _cnotApplied = false;
            SetLinkVisible(false);
            UpdateFireflies(sim.GetProbabilities());
            UpdateUI();
        }

        public void OnDifficultyChanged(int newLevel) { }

        // ── Helpers ───────────────────────────────────────────────────────

        private void UpdateFireflies(float[] probs)
        {
            if (probs.Length < 4) return;
            // Blend colours by probability
            Color c0 = Color.black, c1 = Color.black;
            for (int i = 0; i < 4; i++)
            {
                c0 += StateColors[i] * probs[i] * ((i & 2) == 0 ? 1.5f : 0.5f);
                c1 += StateColors[i] * probs[i] * ((i & 1) == 0 ? 1.5f : 0.5f);
            }
            if (firefly0 != null) firefly0.material.color = c0;
            if (firefly1 != null) firefly1.material.color = c1;
        }

        private void SetLinkVisible(bool visible)
        {
            if (entanglementLink != null) entanglementLink.enabled = visible;
        }

        private IEnumerator PulseLink()
        {
            if (entanglementLink == null) yield break;
            for (int i = 0; i < 5; i++)
            {
                entanglementLink.startWidth = 0.05f + i * 0.02f;
                yield return new WaitForSeconds(0.15f);
            }
        }

        private void UpdateUI()
        {
            if (instructionText != null)
            {
                if (!_hadamardApplied)
                    instructionText.text = "🦋 Two fireflies drift apart.\n" +
                        "Focus to apply Hadamard to Firefly 1 — give it superposition!";
                else if (!_cnotApplied)
                    instructionText.text = "✨ Firefly 1 is in superposition!\n" +
                        "Now focus again — CNOT will entangle both fireflies forever.";
                else
                    instructionText.text = "🔗 The fireflies are entangled!\n" +
                        "Relax to measure them — they'll always agree!";
            }

            if (stateText != null)
            {
                var probs = _sim.GetProbabilities();
                string stateStr = "";
                for (int i = 0; i < probs.Length; i++)
                    if (probs[i] > 0.01f)
                        stateStr += $"|{i:b2}⟩ ×{probs[i]*100f:F0}%  ";
                stateText.text = stateStr.Trim();
            }
        }
    }

    // ── Superposition intro puzzle ─────────────────────────────────────────

    /// <summary>
    /// First puzzle: apply Hadamard to see a single qubit blossom into superposition.
    /// </summary>
    public class SuperpositionPuzzle : MonoBehaviour, IPuzzle
    {
        public int PuzzleIndex => 0;

        [SerializeField] private Renderer flowerRenderer;
        [SerializeField] private TMPro.TextMeshProUGUI instructionText;
        [SerializeField] private ParticleSystem bloomParticles;

        private QuantumSimulator _sim;
        private bool _hadamardApplied;

        private static readonly Color ClosedColor = new(0.2f, 0.1f, 0.4f);
        private static readonly Color BloomColor  = new(1.0f, 0.6f, 0.9f);

        public void Initialise(QuantumSimulator sim, int difficultyLevel)
        {
            _sim = sim;
            _hadamardApplied = false;
            UpdateFlower(0f);
            UpdateUI();
        }

        public void OnGateApplied(QuantumGate gate, QuantumSimulator sim)
        {
            if (gate.Type == GateType.Hadamard)
            {
                _hadamardApplied = true;
                UpdateFlower(0.5f);
                if (bloomParticles != null) bloomParticles.Play();
            }
            else if (gate.Type == GateType.PauliX)
            {
                UpdateFlower(1.0f);
            }
            UpdateUI();
        }

        public bool EvaluateOutcome(int[] measuredBits, QuantumSimulator sim)
        {
            bool success = _hadamardApplied && measuredBits.Length > 0;
            if (instructionText != null)
                instructionText.text = success
                    ? $"🌸 The flower measured: |{measuredBits[0]}⟩\n" +
                      "Both outcomes are possible — that's superposition!"
                    : "First apply Hadamard to make the flower bloom!";
            return success;
        }

        public void OnReset(QuantumSimulator sim)
        {
            _hadamardApplied = false;
            UpdateFlower(0f);
            UpdateUI();
        }

        public void OnDifficultyChanged(int newLevel) { }

        private void UpdateFlower(float t)
        {
            if (flowerRenderer == null) return;
            flowerRenderer.material.color = Color.Lerp(ClosedColor, BloomColor, t);
            float scale = 0.5f + t * 1.0f;
            flowerRenderer.transform.localScale = Vector3.one * scale;
        }

        private void UpdateUI()
        {
            if (instructionText == null) return;
            instructionText.text = _hadamardApplied
                ? "🌸 The flower is in SUPERPOSITION — both open and closed at once!\n" +
                  "Relax to observe (measure) it."
                : "🌱 A quantum flower waits to bloom.\n" +
                  "Focus your mind to apply Hadamard — the Superposition Spell!";
        }
    }
}
