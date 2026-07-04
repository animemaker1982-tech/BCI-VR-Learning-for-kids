using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using LSL;                        // LSL4Unity asset

namespace QuantumMagicGarden.BCI
{
    /// <summary>
    /// Manages the BCI (Brain-Computer Interface) data pipeline.
    /// 
    /// Responsibilities:
    ///   1. Discover and connect to an LSL EEG stream (OpenBCI, Muse, Emotiv)
    ///   2. Buffer incoming EEG data into 1-second windows
    ///   3. Send windows to the Python backend for QML classification
    ///   4. Expose the current MentalState to the rest of the game
    /// 
    /// Fallback: if no LSL stream is found after <see cref="lslTimeoutSeconds"/>,
    /// the manager enters simulation mode (alpha/beta rule-based).
    /// </summary>
    public class BCIManager : MonoBehaviour
    {
        // ── Inspector fields ────────────────────────────────────────────
        [Header("LSL Settings")]
        [SerializeField] private string streamType = "EEG";
        [SerializeField] private int requiredChannels = 3;
        [SerializeField] private float sampleRate = 256f;
        [SerializeField] private float lslTimeoutSeconds = 8f;
        [SerializeField] private float classifyIntervalSeconds = 1f;

        [Header("Backend")]
        [SerializeField] private string backendUrl = "http://localhost:8000";

        [Header("Simulation")]
        [SerializeField] private bool forceSimulation = false;
        [SerializeField] private float simFocusCyclePeriod = 10f;

        // ── Public state ────────────────────────────────────────────────
        public static BCIManager Instance { get; private set; }

        public MentalState CurrentState { get; private set; } = MentalState.Neutral;
        public float StateConfidence { get; private set; } = 0.5f;
        public string SuggestedGateAction { get; private set; } = "NONE";
        public bool IsConnected { get; private set; } = false;
        public bool IsSimulating { get; private set; } = false;

        // ── Events ──────────────────────────────────────────────────────
        public event Action<MentalState, float> OnStateChanged;
        public event Action<string> OnGateActionTriggered;
        public event Action OnBCIConnected;
        public event Action OnBCIDisconnected;

        // ── Private ─────────────────────────────────────────────────────
        private liblsl.StreamInlet _inlet;
        private readonly Queue<float[]> _sampleBuffer = new();
        private int _bufferWindowSamples;
        private float _classifyTimer;
        private string _sessionId;
        private BackendClient _backendClient;
        private MentalState _prevState = MentalState.Neutral;

        // ── Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _sessionId = System.Guid.NewGuid().ToString();
            _bufferWindowSamples = Mathf.RoundToInt(sampleRate);
            _backendClient = new BackendClient(backendUrl);
        }

        private IEnumerator Start()
        {
            if (forceSimulation)
            {
                EnterSimulation();
                yield break;
            }

            yield return StartCoroutine(TryConnectLSL());

            if (!IsConnected)
            {
                Debug.LogWarning("[BCIManager] No LSL stream found — entering simulation mode.");
                EnterSimulation();
            }
        }

        private void Update()
        {
            if (IsConnected && _inlet != null)
                PullSamples();

            _classifyTimer += Time.deltaTime;
            if (_classifyTimer >= classifyIntervalSeconds)
            {
                _classifyTimer = 0f;
                _ = ClassifyAsync();
            }

            if (IsSimulating)
                UpdateSimulation();
        }

        private void OnDestroy()
        {
            _inlet?.close_stream();
            _backendClient?.Dispose();
        }

        // ── LSL connection ──────────────────────────────────────────────

        private IEnumerator TryConnectLSL()
        {
            Debug.Log("[BCIManager] Searching for LSL EEG stream…");
            float elapsed = 0f;

            while (elapsed < lslTimeoutSeconds)
            {
                var results = liblsl.resolve_stream("type", streamType, 1, 0.5f);
                if (results != null && results.Length > 0)
                {
                    _inlet = new liblsl.StreamInlet(results[0]);
                    _inlet.open_stream();
                    IsConnected = true;
                    Debug.Log($"[BCIManager] Connected to LSL stream: {results[0].name()}");
                    OnBCIConnected?.Invoke();
                    yield break;
                }
                elapsed += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
        }

        // ── Sample buffering ────────────────────────────────────────────

        private void PullSamples()
        {
            var chunk = new float[requiredChannels, 128];
            var timestamps = new double[128];
            int pulled = _inlet.pull_chunk(chunk, timestamps);

            for (int s = 0; s < pulled; s++)
            {
                var sample = new float[requiredChannels];
                for (int ch = 0; ch < requiredChannels; ch++)
                    sample[ch] = chunk[ch, s];
                _sampleBuffer.Enqueue(sample);
            }

            // Keep buffer at 2 seconds max
            while (_sampleBuffer.Count > _bufferWindowSamples * 2)
                _sampleBuffer.Dequeue();
        }

        private float[,] GetEEGWindow()
        {
            if (_sampleBuffer.Count < _bufferWindowSamples)
                return null;

            var window = new float[requiredChannels, _bufferWindowSamples];
            var samples = new List<float[]>(_sampleBuffer);
            int start = samples.Count - _bufferWindowSamples;

            for (int s = 0; s < _bufferWindowSamples; s++)
                for (int ch = 0; ch < requiredChannels; ch++)
                    window[ch, s] = samples[start + s][ch];

            return window;
        }

        // ── Classification ──────────────────────────────────────────────

        private async Task ClassifyAsync()
        {
            if (IsSimulating) return;

            var window = GetEEGWindow();
            if (window == null) return;

            try
            {
                var result = await _backendClient.ClassifyBCIAsync(_sessionId, window, sampleRate);
                ApplyPrediction(result.state, result.confidence, result.gate_action);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BCIManager] Classification failed: {ex.Message} — using heuristic");
                ApplyHeuristicClassification(window);
            }
        }

        private void ApplyPrediction(string state, float confidence, string gateAction)
        {
            MentalState newState = state switch
            {
                "focus"   => MentalState.Focus,
                "relax"   => MentalState.Relax,
                _         => MentalState.Neutral,
            };

            CurrentState = newState;
            StateConfidence = confidence;
            SuggestedGateAction = gateAction;

            if (newState != _prevState)
            {
                _prevState = newState;
                OnStateChanged?.Invoke(newState, confidence);
                if (gateAction != "NONE")
                    OnGateActionTriggered?.Invoke(gateAction);
            }
        }

        private void ApplyHeuristicClassification(float[,] window)
        {
            // Simple alpha/beta power ratio across channel 0
            int n = window.GetLength(1);
            float alphaPow = BandPower(window, 0, n, 8, 13, sampleRate);
            float betaPow  = BandPower(window, 0, n, 13, 30, sampleRate);

            if (betaPow > alphaPow * 1.2f)
                ApplyPrediction("focus", 0.65f, "H");
            else if (alphaPow > betaPow * 1.2f)
                ApplyPrediction("relax", 0.65f, "MEASURE");
            else
                ApplyPrediction("neutral", 0.5f, "NONE");
        }

        private static float BandPower(float[,] w, int ch, int n, float lo, float hi, float fs)
        {
            // Simple Welch-style power estimate via FFT
            var sig = new float[n];
            for (int i = 0; i < n; i++) sig[i] = w[ch, i];
            // Remove DC
            float mean = 0;
            foreach (var s in sig) mean += s;
            mean /= n;
            for (int i = 0; i < n; i++) sig[i] -= mean;

            // FFT magnitude accumulator over frequency band
            double power = 0.0;
            int count = 0;
            for (int k = 1; k < n / 2; k++)
            {
                float freq = k * fs / n;
                if (freq >= lo && freq < hi)
                {
                    double re = 0, im = 0;
                    for (int t = 0; t < n; t++)
                    {
                        double angle = 2 * Math.PI * k * t / n;
                        re += sig[t] * Math.Cos(angle);
                        im -= sig[t] * Math.Sin(angle);
                    }
                    power += (re * re + im * im) / (n * n);
                    count++;
                }
            }
            return count > 0 ? (float)(power / count) : 0f;
        }

        // ── Simulation mode ─────────────────────────────────────────────

        private float _simTimer;

        private void EnterSimulation()
        {
            IsSimulating = true;
            IsConnected = false;
            Debug.Log("[BCIManager] Simulation mode active.");
        }

        private void UpdateSimulation()
        {
            _simTimer += Time.deltaTime;
            float phase = (_simTimer % simFocusCyclePeriod) / simFocusCyclePeriod;

            if (phase < 0.4f)
                ApplyPrediction("focus", 0.75f + UnityEngine.Random.Range(-0.05f, 0.05f), "H");
            else if (phase < 0.7f)
                ApplyPrediction("neutral", 0.5f, "NONE");
            else
                ApplyPrediction("relax", 0.70f, "MEASURE");
        }
    }

    public enum MentalState { Focus, Relax, Neutral }
}
