using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using QuantumMagicGarden.Quantum;
using QuantumMagicGarden.Garden;

namespace QuantumMagicGarden.Puzzles
{
    /// <summary>
    /// Shor's Master Key Quest Puzzle.
    ///
    /// Teaches quantum period-finding (the key step in Shor's algorithm) by
    /// letting kids crack "crystal locks" — toy RSA numbers (N=6,15,21).
    ///
    /// Simplified for children:
    ///   1. Present N and a random coprime a.
    ///   2. Show the modular exponentiation table visually.
    ///   3. Kids use BCI focus to run the quantum Fourier transform (QFT)
    ///      and find the period r.
    ///   4. Compute factors: gcd(a^(r/2) ± 1, N).
    ///   5. Visual lock-shattering animation on success.
    /// </summary>
    public class ShorPuzzle : MonoBehaviour, IPuzzle
    {
        // ── IPuzzle ──────────────────────────────────────────────────────
        public int PuzzleIndex => 7;   // "shor_factor6"

        // ── Inspector ────────────────────────────────────────────────────
        [Header("UI")]
        [SerializeField] private TMPro.TextMeshProUGUI lockLabel;
        [SerializeField] private TMPro.TextMeshProUGUI instructionText;
        [SerializeField] private TMPro.TextMeshProUGUI periodText;
        [SerializeField] private TMPro.TextMeshProUGUI factorsText;
        [SerializeField] private TMPro.TextMeshProUGUI modExpTableText;

        [Header("Visual")]
        [SerializeField] private GameObject lockObject;
        [SerializeField] private ParticleSystem lockShatterParticles;
        [SerializeField] private Renderer[] crystalRenderers;
        [SerializeField] private Gradient periodGradient;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip qftSound;
        [SerializeField] private AudioClip lockShatterSound;

        // ── Puzzle parameters ─────────────────────────────────────────────
        private static readonly int[] PuzzleNumbers = { 6, 10, 15, 21, 35 };
        private int _N;
        private int _a;
        private int _r;           // true period
        private int _nQubits;
        private int _qftStepsCompleted;
        private int _totalQFTSteps;
        private bool _periodFound;
        private QuantumSimulator _sim;
        private int _difficultyLevel;

        // ── IPuzzle implementation ────────────────────────────────────────

        public void Initialise(QuantumSimulator sim, int difficultyLevel)
        {
            _difficultyLevel = difficultyLevel;
            _sim = sim;
            _periodFound = false;
            _qftStepsCompleted = 0;

            // Choose puzzle number by difficulty
            int idx = Mathf.Min(difficultyLevel - 1, PuzzleNumbers.Length - 1);
            _N = PuzzleNumbers[Mathf.Max(0, idx)];
            _a = FindCoprime(_N);
            _r = FindPeriod(_a, _N);
            _nQubits = Mathf.CeilToInt(Mathf.Log(_N + 1, 2));
            _totalQFTSteps = _nQubits;

            // Encode |a^0 mod N⟩ = |1⟩ in the input register
            _sim.Reset();

            UpdateUI();
            Debug.Log($"[Shor] N={_N}, a={_a}, r={_r}, nQubits={_nQubits}");
        }

        public void OnGateApplied(QuantumGate gate, QuantumSimulator sim)
        {
            if (_periodFound) return;

            // H gates drive QFT steps
            if (gate.Type == GateType.Hadamard)
            {
                ApplyQFTStep(_qftStepsCompleted);
                _qftStepsCompleted++;

                if (audioSource != null && qftSound != null)
                    audioSource.PlayOneShot(qftSound);

                UpdateCrystalColors();
                UpdateUI();

                if (_qftStepsCompleted >= _totalQFTSteps)
                {
                    _periodFound = true;
                    ShowPeriodFound();
                }
            }
        }

        public bool EvaluateOutcome(int[] measuredBits, QuantumSimulator sim)
        {
            if (!_periodFound) return false;

            int measured = 0;
            for (int q = 0; q < measuredBits.Length; q++)
                measured |= measuredBits[q] << q;

            // Check factors
            (int p, int q_) = ComputeFactors(_a, _r, _N);
            bool success = p > 1 && q_ > 1 && p * q_ == _N;

            if (factorsText != null)
                factorsText.text = success
                    ? $"🔓 CRACKED! {_N} = {p} × {q_}\n💡 This breaks RSA-style encryption!"
                    : $"Almost! Period r={_r}, but factors weren't perfect. Try again!";

            if (success)
            {
                if (lockShatterParticles != null) lockShatterParticles.Play();
                if (lockObject != null) StartCoroutine(ShatterLock());
                if (audioSource != null && lockShatterSound != null)
                    audioSource.PlayOneShot(lockShatterSound);
            }
            return success;
        }

        public void OnReset(QuantumSimulator sim)
        {
            _qftStepsCompleted = 0;
            _periodFound = false;
            _sim.Reset();
            if (lockObject != null) lockObject.SetActive(true);
            UpdateUI();
        }

        public void OnDifficultyChanged(int newLevel)
        {
            _difficultyLevel = newLevel;
        }

        // ── QFT simulation ─────────────────────────────────────────────────

        private void ApplyQFTStep(int step)
        {
            int q = _nQubits - 1 - step;
            if (q < 0 || q >= _sim.NumQubits) return;

            // Hadamard on qubit q
            _sim.ApplyGate(QuantumGate.H(q));

            // Controlled phase rotations
            for (int k = 2; k <= _nQubits - step; k++)
            {
                int controlQubit = q - k + 1;
                if (controlQubit >= 0 && controlQubit < _sim.NumQubits)
                {
                    float angle = 2f * Mathf.PI / Mathf.Pow(2, k);
                    _sim.ApplyGate(QuantumGate.RZ(q, angle));
                }
            }
        }

        private void ShowPeriodFound()
        {
            var probs = _sim.GetProbabilities();
            // Period shows as peaks at multiples of (2^n / r)
            int peak = FindDominantPeak(probs);
            int nStates = 1 << _nQubits;
            // Estimate r from peak
            int estimatedR = peak > 0 ? Mathf.RoundToInt((float)nStates / peak) : _r;

            if (periodText != null)
                periodText.text = $"🌀 Period found: r = {_r}\n" +
                    $"(QFT peak at position {peak} out of {nStates})";

            UpdateUI();
        }

        private static int FindDominantPeak(float[] probs)
        {
            int peak = 0;
            float maxP = 0f;
            for (int i = 1; i < probs.Length; i++)
                if (probs[i] > maxP) { maxP = probs[i]; peak = i; }
            return peak;
        }

        // ── Maths helpers ─────────────────────────────────────────────────

        private static int FindPeriod(int a, int N)
        {
            int x = 1;
            for (int r = 1; r <= N; r++)
            {
                x = x * a % N;
                if (x == 1) return r;
            }
            return N;
        }

        private static (int, int) ComputeFactors(int a, int r, int N)
        {
            if (r % 2 != 0) return (1, 1);
            int half = (int)Math.Pow(a, r / 2);
            int p = GCD(half - 1, N);
            int q_ = GCD(half + 1, N);
            return (p, q_);
        }

        private static int GCD(int a, int b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            while (b != 0) { int t = b; b = a % b; a = t; }
            return a;
        }

        private static int FindCoprime(int N)
        {
            for (int a = 2; a < N; a++)
                if (GCD(a, N) == 1 && FindPeriod(a, N) % 2 == 0)
                    return a;
            return 2;
        }

        // ── Visual helpers ────────────────────────────────────────────────

        private void UpdateCrystalColors()
        {
            float progress = _totalQFTSteps > 0
                ? (float)_qftStepsCompleted / _totalQFTSteps
                : 0f;
            Color c = periodGradient != null
                ? periodGradient.Evaluate(progress)
                : Color.Lerp(Color.blue, Color.cyan, progress);

            foreach (var r in crystalRenderers)
                if (r != null) r.material.color = c;
        }

        private IEnumerator ShatterLock()
        {
            yield return new WaitForSeconds(0.5f);
            if (lockObject != null) lockObject.SetActive(false);
        }

        private void UpdateUI()
        {
            if (lockLabel != null)
                lockLabel.text = $"🔒 Magic Lock: N = {_N}";

            if (instructionText != null)
            {
                if (_qftStepsCompleted == 0)
                    instructionText.text =
                        $"🧮 The lock number is {_N}.\n" +
                        $"We'll use a = {_a} and quantum magic to find the period!\n" +
                        "Focus to cast the Quantum Fourier Transform (QFT) step by step.";
                else if (!_periodFound)
                    instructionText.text =
                        $"⚛️ QFT step {_qftStepsCompleted}/{_totalQFTSteps}\n" +
                        "Keep focusing — each step builds the Fourier pattern!";
                else
                    instructionText.text =
                        $"✨ QFT complete! Period r = {_r}.\n" +
                        "Now RELAX to measure and crack the lock!";
            }

            if (modExpTableText != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"a={_a} mod {_N}:\n");
                for (int i = 0; i <= Mathf.Min(7, _r); i++)
                {
                    int val = (int)Math.Pow(_a, i) % _N;
                    sb.AppendLine($"  a^{i} mod {_N} = {val}");
                }
                modExpTableText.text = sb.ToString();
            }
        }
    }
}
