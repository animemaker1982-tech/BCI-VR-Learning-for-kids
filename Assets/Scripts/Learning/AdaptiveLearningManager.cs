using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using QuantumMagicGarden.BCI;
using QuantumMagicGarden.Quantum;

namespace QuantumMagicGarden.Learning
{
    /// <summary>
    /// Adaptive Learning Manager — talks to the Python RL + PQL backend every
    /// <see cref="feedbackIntervalSeconds"/> to receive difficulty adjustments and
    /// curriculum recommendations.  Exposes events that GardenController listens to.
    /// </summary>
    public class AdaptiveLearningManager : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────
        [Header("Settings")]
        [SerializeField] private float feedbackIntervalSeconds = 30f;
        [SerializeField] private int maxLevel = 8;

        [Header("Backend")]
        [SerializeField] private string backendUrl = "http://localhost:8000";

        // ── Public state ─────────────────────────────────────────────────
        public static AdaptiveLearningManager Instance { get; private set; }

        public int CurrentLevel     { get; private set; } = 1;
        public bool ShowHint        { get; private set; } = false;
        public string NextPuzzle    { get; private set; } = "superposition_intro";
        public float SessionXP      { get; private set; } = 0f;
        public int TotalAnswers     { get; private set; } = 0;
        public int CorrectAnswers   { get; private set; } = 0;
        public int CurrentStreak    { get; private set; } = 0;

        // ── Events ───────────────────────────────────────────────────────
        public event Action<int>    OnLevelChanged;
        public event Action<string> OnNextPuzzleRecommended;
        public event Action<bool>   OnHintToggled;
        public event Action<float>  OnXPGained;

        // ── Private ──────────────────────────────────────────────────────
        private string _sessionId;
        private BackendClient _client;
        private float _feedbackTimer;
        private float _sessionStartTime;
        private float[] _masteryVec = new float[10];
        private float _timeSinceLastError = 0f;
        private int _currentPuzzleIdx = 0;
        private List<PuzzleOutcome> _pendingOutcomes = new();

        // ── Lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _sessionId = System.Guid.NewGuid().ToString();
            _client = new BackendClient(backendUrl);
        }

        private void Start()
        {
            _sessionStartTime = Time.time;
            _ = FetchInitialRecommendationsAsync();
        }

        private void Update()
        {
            _feedbackTimer += Time.deltaTime;
            _timeSinceLastError += Time.deltaTime;

            if (_feedbackTimer >= feedbackIntervalSeconds)
            {
                _feedbackTimer = 0f;
                _ = SendFeedbackAsync();
            }
        }

        private void OnDestroy()
        {
            _client?.Dispose();
        }

        // ── Public API (called by puzzles) ────────────────────────────────

        public void RecordPuzzleOutcome(int puzzleIdx, bool success, float reward)
        {
            TotalAnswers++;
            if (success)
            {
                CorrectAnswers++;
                CurrentStreak++;
                SessionXP += CalculateXP(success, CurrentStreak, CurrentLevel);
                OnXPGained?.Invoke(SessionXP);
            }
            else
            {
                CurrentStreak = 0;
                _timeSinceLastError = 0f;
            }

            // Update mastery immediately with fast EMA
            float delta = success ? 0.1f : -0.05f;
            _masteryVec[puzzleIdx] = Mathf.Clamp01(_masteryVec[puzzleIdx] + delta);

            _pendingOutcomes.Add(new PuzzleOutcome
            {
                PuzzleIdx = puzzleIdx,
                Success = success,
                Reward = reward,
            });

            _ = RequestNextPuzzleAsync(puzzleIdx);
        }

        public float GetScoreRate()
        {
            return TotalAnswers == 0 ? 0.5f : (float)CorrectAnswers / TotalAnswers;
        }

        public float GetErrorRate()
        {
            return TotalAnswers == 0 ? 0.2f : 1f - GetScoreRate();
        }

        public float GetTimeOnTask()
        {
            float elapsed = Time.time - _sessionStartTime;
            return Mathf.Clamp01(elapsed / 1800f);  // normalised over 30-min session
        }

        // ── Backend calls ─────────────────────────────────────────────────

        private async Task FetchInitialRecommendationsAsync()
        {
            try
            {
                var bciConf = BCIManager.Instance != null ? BCIManager.Instance.StateConfidence : 0.5f;

                // RL difficulty recommendation
                var rlResp = await _client.GetRLRecommendationAsync(new RLRequest
                {
                    session_id    = _sessionId,
                    score_rate    = GetScoreRate(),
                    error_rate    = GetErrorRate(),
                    time_on_task  = GetTimeOnTask(),
                    bci_confidence = bciConf,
                    streak         = CurrentStreak,
                    current_level  = CurrentLevel,
                });
                ApplyRLResponse(rlResp);

                // PQL curriculum recommendation
                var pqlResp = await _client.GetPQLRecommendationAsync(new PQLRequest
                {
                    session_id         = _sessionId,
                    mastery_vec        = _masteryVec,
                    session_progress   = GetTimeOnTask(),
                    time_since_error   = _timeSinceLastError,
                    current_puzzle_idx = _currentPuzzleIdx,
                });
                ApplyPQLResponse(pqlResp);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveLearning] Backend unavailable: {ex.Message} — using defaults");
                ApplyHeuristicRecommendation();
            }
        }

        private async Task SendFeedbackAsync()
        {
            if (_pendingOutcomes.Count == 0) return;

            try
            {
                var bciConf = BCIManager.Instance != null ? BCIManager.Instance.StateConfidence : 0.5f;

                var rlResp = await _client.GetRLRecommendationAsync(new RLRequest
                {
                    session_id    = _sessionId,
                    score_rate    = GetScoreRate(),
                    error_rate    = GetErrorRate(),
                    time_on_task  = GetTimeOnTask(),
                    bci_confidence = bciConf,
                    streak         = CurrentStreak,
                    current_level  = CurrentLevel,
                });
                ApplyRLResponse(rlResp);
                _pendingOutcomes.Clear();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveLearning] Feedback send failed: {ex.Message}");
            }
        }

        private async Task RequestNextPuzzleAsync(int completedPuzzleIdx)
        {
            try
            {
                var pqlResp = await _client.GetPQLRecommendationAsync(new PQLRequest
                {
                    session_id         = _sessionId,
                    mastery_vec        = _masteryVec,
                    session_progress   = GetTimeOnTask(),
                    time_since_error   = _timeSinceLastError,
                    current_puzzle_idx = completedPuzzleIdx,
                });
                ApplyPQLResponse(pqlResp);
            }
            catch
            {
                // Heuristic: advance linearly if backend down
                _currentPuzzleIdx = Mathf.Min(_currentPuzzleIdx + 1, 9);
                NextPuzzle = $"puzzle_{_currentPuzzleIdx}";
                OnNextPuzzleRecommended?.Invoke(NextPuzzle);
            }
        }

        // ── Apply responses ───────────────────────────────────────────────

        private void ApplyRLResponse(RLResponse resp)
        {
            int newLevel = Mathf.Clamp(resp.recommended_level, 1, maxLevel);
            ShowHint = resp.show_hint;

            if (newLevel != CurrentLevel)
            {
                CurrentLevel = newLevel;
                Debug.Log($"[AdaptiveLearning] Level → {newLevel} ({resp.action_name})");
                OnLevelChanged?.Invoke(newLevel);
            }
            OnHintToggled?.Invoke(ShowHint);
        }

        private void ApplyPQLResponse(PQLResponse resp)
        {
            NextPuzzle = resp.next_puzzle;
            _currentPuzzleIdx = resp.puzzle_idx;
            Debug.Log($"[AdaptiveLearning] Next puzzle: {NextPuzzle} ({resp.reason})");
            OnNextPuzzleRecommended?.Invoke(NextPuzzle);
        }

        private void ApplyHeuristicRecommendation()
        {
            float score = GetScoreRate();
            if (score > 0.85f && CurrentLevel < maxLevel) CurrentLevel++;
            else if (score < 0.4f && CurrentLevel > 1) CurrentLevel--;
            ShowHint = score < 0.5f;
            OnLevelChanged?.Invoke(CurrentLevel);
            OnHintToggled?.Invoke(ShowHint);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static float CalculateXP(bool success, int streak, int level)
        {
            if (!success) return 0f;
            float base_ = 10f + level * 5f;
            float streakBonus = Mathf.Min(streak * 2f, 20f);
            return base_ + streakBonus;
        }

        // ── Data classes (for serialisation) ────────────────────────────

        [System.Serializable]
        private class PuzzleOutcome
        {
            public int PuzzleIdx;
            public bool Success;
            public float Reward;
        }
    }

    // ── Request/Response DTOs (mirroring FastAPI schemas) ───────────────────

    [System.Serializable]
    public class RLRequest
    {
        public string session_id;
        public float score_rate;
        public float error_rate;
        public float time_on_task;
        public float bci_confidence;
        public int streak;
        public int current_level;
    }

    [System.Serializable]
    public class RLResponse
    {
        public string session_id;
        public string action_name;
        public int recommended_level;
        public bool show_hint;
        public bool skip_puzzle;
        public float confidence;
    }

    [System.Serializable]
    public class PQLRequest
    {
        public string session_id;
        public float[] mastery_vec;
        public float session_progress;
        public float time_since_error;
        public int current_puzzle_idx;
    }

    [System.Serializable]
    public class PQLResponse
    {
        public string session_id;
        public string next_puzzle;
        public int puzzle_idx;
        public float reason_confidence;
        public string reason;
    }
}
