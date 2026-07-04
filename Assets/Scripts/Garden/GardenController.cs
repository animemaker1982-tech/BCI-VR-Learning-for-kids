using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using QuantumMagicGarden.BCI;
using QuantumMagicGarden.Learning;
using QuantumMagicGarden.Quantum;

namespace QuantumMagicGarden.Garden
{
    /// <summary>
    /// GardenController — the central game loop for Quantum Magic Garden.
    ///
    /// Responsibilities:
    ///  - Manage game states (Idle → ActivePuzzle → Measuring → Celebrating → NextPuzzle)
    ///  - Bridge BCI events (focus/relax) → QuantumSimulator gate applications
    ///  - Spawn and activate puzzle GameObjects based on AdaptiveLearningManager recommendations
    ///  - Trigger visual feedback (particles, Bloch sphere updates)
    ///  - Drive audio and haptics
    /// </summary>
    public class GardenController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────
        [Header("Puzzle Prefabs")]
        [SerializeField] private GameObject superpositionPuzzlePrefab;
        [SerializeField] private GameObject entanglementPuzzlePrefab;
        [SerializeField] private GameObject groverPuzzlePrefab;
        [SerializeField] private GameObject shorPuzzlePrefab;

        [Header("Scene References")]
        [SerializeField] private Transform puzzleSpawnPoint;
        [SerializeField] private BlochSphereVisualizer[] blochSpheres;
        [SerializeField] private ParticleSystem gateParticles;
        [SerializeField] private ParticleSystem measureParticles;
        [SerializeField] private ParticleSystem celebrationParticles;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip focusSound;
        [SerializeField] private AudioClip gateAppliedSound;
        [SerializeField] private AudioClip measureSound;
        [SerializeField] private AudioClip successSound;
        [SerializeField] private AudioClip failSound;

        [Header("Settings")]
        [SerializeField] private float gateCooldownSeconds = 1.5f;
        [SerializeField] private float measureHoldSeconds  = 0.8f;
        [SerializeField] private int   numSimulatedQubits  = 2;

        // ── State machine ─────────────────────────────────────────────────
        public enum GameState { Idle, ActivePuzzle, Measuring, Celebrating, Transitioning }
        public GameState CurrentState { get; private set; } = GameState.Idle;

        // ── Private ───────────────────────────────────────────────────────
        private QuantumSimulator _sim;
        private GameObject _activePuzzle;
        private IPuzzle    _activePuzzleScript;
        private float _gateCooldownTimer;
        private bool  _measureHeld;
        private float _measureHoldTimer;
        private int   _currentLevel = 1;

        private static readonly Dictionary<string, System.Type> PuzzleTypeMap = new()
        {
            { "superposition_intro", typeof(Puzzles.SuperpositionPuzzle) },
            { "hadamard_basic",      typeof(Puzzles.SuperpositionPuzzle) },
            { "cnot_entanglement",   typeof(Puzzles.EntanglementPuzzle) },
            { "bell_state",          typeof(Puzzles.EntanglementPuzzle) },
            { "grover_2qubit",       typeof(Puzzles.GroverPuzzle) },
            { "grover_3qubit",       typeof(Puzzles.GroverPuzzle) },
            { "shor_factor6",        typeof(Puzzles.ShorPuzzle) },
            { "shor_factor15",       typeof(Puzzles.ShorPuzzle) },
        };

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            _sim = new QuantumSimulator(numSimulatedQubits);
        }

        private void OnEnable()
        {
            if (BCIManager.Instance != null)
            {
                BCIManager.Instance.OnGateActionTriggered += OnBCIGateAction;
                BCIManager.Instance.OnStateChanged        += OnBCIStateChanged;
            }
            if (AdaptiveLearningManager.Instance != null)
            {
                AdaptiveLearningManager.Instance.OnNextPuzzleRecommended += OnNextPuzzleRecommended;
                AdaptiveLearningManager.Instance.OnLevelChanged           += OnLevelChanged;
            }
        }

        private void OnDisable()
        {
            if (BCIManager.Instance != null)
            {
                BCIManager.Instance.OnGateActionTriggered -= OnBCIGateAction;
                BCIManager.Instance.OnStateChanged        -= OnBCIStateChanged;
            }
            if (AdaptiveLearningManager.Instance != null)
            {
                AdaptiveLearningManager.Instance.OnNextPuzzleRecommended -= OnNextPuzzleRecommended;
                AdaptiveLearningManager.Instance.OnLevelChanged           -= OnLevelChanged;
            }
        }

        private void Start()
        {
            StartCoroutine(StartSessionCoroutine());
        }

        private void Update()
        {
            if (_gateCooldownTimer > 0f)
                _gateCooldownTimer -= Time.deltaTime;

            if (CurrentState == GameState.Measuring)
            {
                _measureHoldTimer += Time.deltaTime;
                if (_measureHoldTimer >= measureHoldSeconds)
                    ExecuteMeasurement();
            }
        }

        // ── Session start ─────────────────────────────────────────────────

        private IEnumerator StartSessionCoroutine()
        {
            yield return new WaitForSeconds(1.5f);  // let BCI + backend initialise
            TransitionToState(GameState.Idle);
            yield return new WaitForSeconds(1f);
            SpawnPuzzle("superposition_intro");
        }

        // ── BCI event handlers ────────────────────────────────────────────

        private void OnBCIStateChanged(MentalState state, float confidence)
        {
            if (state == MentalState.Focus && confidence > 0.6f)
                PlaySound(focusSound, 0.4f);

            if (state == MentalState.Relax && CurrentState == GameState.ActivePuzzle)
            {
                _measureHeld = true;
                _measureHoldTimer = 0f;
                TransitionToState(GameState.Measuring);
            }
            else if (state != MentalState.Relax && _measureHeld)
            {
                _measureHeld = false;
                if (CurrentState == GameState.Measuring)
                    TransitionToState(GameState.ActivePuzzle);
            }
        }

        private void OnBCIGateAction(string gateLabel)
        {
            if (CurrentState != GameState.ActivePuzzle) return;
            if (_gateCooldownTimer > 0f) return;
            if (gateLabel == "NONE" || gateLabel == "MEASURE") return;

            try
            {
                var gate = QuantumGate.FromString(gateLabel, 0, 1);
                ApplyGateToSimulator(gate);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Garden] Gate parse error: {ex.Message}");
            }
        }

        // ── Gate application ──────────────────────────────────────────────

        private void ApplyGateToSimulator(QuantumGate gate)
        {
            _sim.ApplyGate(gate);
            _gateCooldownTimer = gateCooldownSeconds;

            // Update Bloch sphere visuals
            RefreshBlochSpheres();

            // VFX
            if (gateParticles != null) gateParticles.Play();
            PlaySound(gateAppliedSound);

            // Notify puzzle
            _activePuzzleScript?.OnGateApplied(gate, _sim);

            Debug.Log($"[Garden] Applied {gate.DisplayName} — State updated.");
        }

        // ── Measurement ───────────────────────────────────────────────────

        private void ExecuteMeasurement()
        {
            if (CurrentState != GameState.Measuring) return;

            var outcomes = _sim.MeasureAll();
            PlaySound(measureSound);
            if (measureParticles != null) measureParticles.Play();
            RefreshBlochSpheres();

            Debug.Log($"[Garden] Measured: [{string.Join(",", outcomes)}]");

            bool success = _activePuzzleScript?.EvaluateOutcome(outcomes, _sim) ?? false;
            RecordAndTransition(success);
        }

        private void RecordAndTransition(bool success)
        {
            AdaptiveLearningManager.Instance?.RecordPuzzleOutcome(
                _activePuzzleScript?.PuzzleIndex ?? 0,
                success,
                success ? 1f : -0.2f
            );

            if (success)
            {
                PlaySound(successSound);
                if (celebrationParticles != null) celebrationParticles.Play();
                TransitionToState(GameState.Celebrating);
                StartCoroutine(CelebrationCoroutine());
            }
            else
            {
                PlaySound(failSound);
                _sim.Reset();
                RefreshBlochSpheres();
                TransitionToState(GameState.ActivePuzzle);
                _activePuzzleScript?.OnReset(_sim);
            }
        }

        private IEnumerator CelebrationCoroutine()
        {
            yield return new WaitForSeconds(3f);
            // Puzzle will be swapped by AdaptiveLearningManager.OnNextPuzzleRecommended
            TransitionToState(GameState.Transitioning);
        }

        // ── Puzzle spawning ───────────────────────────────────────────────

        private void SpawnPuzzle(string puzzleKey)
        {
            if (_activePuzzle != null)
            {
                Destroy(_activePuzzle);
                _activePuzzle = null;
                _activePuzzleScript = null;
            }

            _sim = new QuantumSimulator(numSimulatedQubits);
            RefreshBlochSpheres();

            GameObject prefab = GetPrefabForPuzzle(puzzleKey);
            if (prefab == null)
            {
                Debug.LogError($"[Garden] No prefab for puzzle '{puzzleKey}'");
                return;
            }

            _activePuzzle = Instantiate(prefab, puzzleSpawnPoint.position, puzzleSpawnPoint.rotation);
            _activePuzzleScript = _activePuzzle.GetComponent<IPuzzle>();
            _activePuzzleScript?.Initialise(_sim, _currentLevel);

            TransitionToState(GameState.ActivePuzzle);
            Debug.Log($"[Garden] Puzzle spawned: {puzzleKey}");
        }

        private GameObject GetPrefabForPuzzle(string key)
        {
            if (key.Contains("grover"))   return groverPuzzlePrefab;
            if (key.Contains("shor"))     return shorPuzzlePrefab;
            if (key.Contains("entangle") || key.Contains("cnot") || key.Contains("bell"))
                return entanglementPuzzlePrefab;
            return superpositionPuzzlePrefab;
        }

        // ── Event callbacks ───────────────────────────────────────────────

        private void OnNextPuzzleRecommended(string puzzleKey)
        {
            if (CurrentState == GameState.Celebrating || CurrentState == GameState.Transitioning)
                StartCoroutine(DelayedSpawn(puzzleKey, 1f));
        }

        private IEnumerator DelayedSpawn(string puzzleKey, float delay)
        {
            yield return new WaitForSeconds(delay);
            SpawnPuzzle(puzzleKey);
        }

        private void OnLevelChanged(int newLevel)
        {
            _currentLevel = newLevel;
            _activePuzzleScript?.OnDifficultyChanged(newLevel);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void RefreshBlochSpheres()
        {
            if (blochSpheres == null) return;
            for (int q = 0; q < blochSpheres.Length && q < _sim.NumQubits; q++)
            {
                var (bx, by, bz) = _sim.BlochVector(q);
                blochSpheres[q]?.UpdateState(bx, by, bz, _sim.GetProbabilities());
            }
        }

        private void TransitionToState(GameState newState)
        {
            CurrentState = newState;
            Debug.Log($"[Garden] → {newState}");
        }

        private void PlaySound(AudioClip clip, float volume = 1f)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip, volume);
        }
    }

    // ── IPuzzle interface ─────────────────────────────────────────────────

    public interface IPuzzle
    {
        int PuzzleIndex { get; }
        void Initialise(QuantumSimulator sim, int difficultyLevel);
        void OnGateApplied(QuantumGate gate, QuantumSimulator sim);
        bool EvaluateOutcome(int[] measuredBits, QuantumSimulator sim);
        void OnReset(QuantumSimulator sim);
        void OnDifficultyChanged(int newLevel);
    }
}
