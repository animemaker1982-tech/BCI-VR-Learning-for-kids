using System.Collections;
using UnityEngine;
using QuantumMagicGarden.BCI;
using QuantumMagicGarden.Learning;

namespace QuantumMagicGarden.UI
{
    /// <summary>
    /// In-headset HUD for Quantum Magic Garden.
    ///
    /// Displays:
    ///  - XP / Level progression bar
    ///  - BCI connection status + current mental state indicator
    ///  - Active puzzle name + adaptive hint panel
    ///  - Session stats (accuracy, streak)
    ///  - Mini Bloch sphere overlay (from BlochSphereVisualizer data)
    /// </summary>
    public class ProgressUI : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────
        [Header("XP / Level")]
        [SerializeField] private UnityEngine.UI.Slider xpBar;
        [SerializeField] private TMPro.TextMeshProUGUI levelText;
        [SerializeField] private TMPro.TextMeshProUGUI xpText;
        [SerializeField] private Animator levelUpAnimator;

        [Header("BCI Status")]
        [SerializeField] private TMPro.TextMeshProUGUI bciStatusText;
        [SerializeField] private Renderer bciIndicatorLight;
        [SerializeField] private UnityEngine.UI.Slider confidenceBar;

        [Header("Puzzle Info")]
        [SerializeField] private TMPro.TextMeshProUGUI puzzleNameText;
        [SerializeField] private TMPro.TextMeshProUGUI hintPanel;
        [SerializeField] private GameObject hintContainer;

        [Header("Stats")]
        [SerializeField] private TMPro.TextMeshProUGUI accuracyText;
        [SerializeField] private TMPro.TextMeshProUGUI streakText;

        [Header("Notifications")]
        [SerializeField] private TMPro.TextMeshProUGUI notificationText;
        [SerializeField] private CanvasGroup notificationGroup;

        [Header("Settings")]
        [SerializeField] private float xpPerLevel = 100f;

        // ── State colours ─────────────────────────────────────────────────
        private static readonly Color FocusColor   = new(0.2f, 0.8f, 1.0f);
        private static readonly Color RelaxColor   = new(0.4f, 1.0f, 0.5f);
        private static readonly Color NeutralColor = new(0.8f, 0.8f, 0.8f);
        private static readonly Color DisconColor  = new(0.8f, 0.2f, 0.2f);

        // ── Private ───────────────────────────────────────────────────────
        private float _displayedXP;
        private int   _displayedLevel = 1;
        private Coroutine _notificationCoroutine;

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void OnEnable()
        {
            if (BCIManager.Instance != null)
            {
                BCIManager.Instance.OnStateChanged       += OnBCIStateChanged;
                BCIManager.Instance.OnBCIConnected       += OnBCIConnected;
                BCIManager.Instance.OnBCIDisconnected    += OnBCIDisconnected;
            }
            if (AdaptiveLearningManager.Instance != null)
            {
                AdaptiveLearningManager.Instance.OnXPGained              += OnXPGained;
                AdaptiveLearningManager.Instance.OnLevelChanged           += OnLevelChanged;
                AdaptiveLearningManager.Instance.OnNextPuzzleRecommended  += OnNextPuzzleRecommended;
                AdaptiveLearningManager.Instance.OnHintToggled            += OnHintToggled;
            }
        }

        private void OnDisable()
        {
            if (BCIManager.Instance != null)
            {
                BCIManager.Instance.OnStateChanged    -= OnBCIStateChanged;
                BCIManager.Instance.OnBCIConnected    -= OnBCIConnected;
                BCIManager.Instance.OnBCIDisconnected -= OnBCIDisconnected;
            }
            if (AdaptiveLearningManager.Instance != null)
            {
                AdaptiveLearningManager.Instance.OnXPGained             -= OnXPGained;
                AdaptiveLearningManager.Instance.OnLevelChanged          -= OnLevelChanged;
                AdaptiveLearningManager.Instance.OnNextPuzzleRecommended -= OnNextPuzzleRecommended;
                AdaptiveLearningManager.Instance.OnHintToggled           -= OnHintToggled;
            }
        }

        private void Start()
        {
            SetBCIStatus(false, false);
            UpdateXPBar(0f, 1);
            if (notificationGroup != null) notificationGroup.alpha = 0f;
            if (hintContainer != null) hintContainer.SetActive(false);
        }

        private void Update()
        {
            UpdateStats();
        }

        // ── BCI event handlers ────────────────────────────────────────────

        private void OnBCIStateChanged(MentalState state, float confidence)
        {
            if (bciStatusText != null)
                bciStatusText.text = state switch
                {
                    MentalState.Focus   => "🔵 Focused",
                    MentalState.Relax   => "🟢 Relaxed",
                    MentalState.Neutral => "⚪ Neutral",
                    _                   => "…",
                };

            if (bciIndicatorLight != null)
            {
                Color c = state switch
                {
                    MentalState.Focus   => FocusColor,
                    MentalState.Relax   => RelaxColor,
                    _                   => NeutralColor,
                };
                bciIndicatorLight.material.color = c;
                bciIndicatorLight.material.SetColor("_EmissionColor", c * 0.6f);
            }

            if (confidenceBar != null) confidenceBar.value = confidence;
        }

        private void OnBCIConnected()    => SetBCIStatus(true,  false);
        private void OnBCIDisconnected() => SetBCIStatus(false, false);

        private void SetBCIStatus(bool connected, bool simulating)
        {
            if (bciIndicatorLight != null)
                bciIndicatorLight.material.color = connected ? NeutralColor : DisconColor;

            if (bciStatusText != null)
                bciStatusText.text = connected ? "BCI Connected"
                    : simulating ? "BCI Simulating"
                    : "BCI Searching…";
        }

        // ── Learning event handlers ───────────────────────────────────────

        private void OnXPGained(float totalXP)
        {
            StartCoroutine(AnimateXP(_displayedXP, totalXP));
        }

        private void OnLevelChanged(int newLevel)
        {
            if (newLevel > _displayedLevel)
            {
                ShowNotification($"🎉 Level Up! You reached Level {newLevel}!");
                if (levelUpAnimator != null)
                    levelUpAnimator.SetTrigger("LevelUp");
            }
            _displayedLevel = newLevel;
            UpdateXPBar(_displayedXP, newLevel);
        }

        private void OnNextPuzzleRecommended(string puzzleKey)
        {
            if (puzzleNameText != null)
                puzzleNameText.text = FormatPuzzleName(puzzleKey);
        }

        private void OnHintToggled(bool showHint)
        {
            if (hintContainer != null) hintContainer.SetActive(showHint);
            if (showHint && AdaptiveLearningManager.Instance != null && hintPanel != null)
            {
                hintPanel.text = GetHintForPuzzle(AdaptiveLearningManager.Instance.NextPuzzle);
            }
        }

        // ── Stats update ──────────────────────────────────────────────────

        private void UpdateStats()
        {
            if (AdaptiveLearningManager.Instance == null) return;
            float acc = AdaptiveLearningManager.Instance.GetScoreRate();
            int streak = AdaptiveLearningManager.Instance.CurrentStreak;

            if (accuracyText != null) accuracyText.text = $"Accuracy: {acc * 100f:F0}%";
            if (streakText != null)   streakText.text   = streak > 1 ? $"🔥 Streak ×{streak}" : "";
        }

        private void UpdateXPBar(float xp, int level)
        {
            _displayedXP = xp;
            float xpInLevel = xp % xpPerLevel;
            if (xpBar != null)    xpBar.value = xpInLevel / xpPerLevel;
            if (levelText != null) levelText.text = $"Level {level}";
            if (xpText != null)   xpText.text = $"{xpInLevel:F0} / {xpPerLevel:F0} XP";
        }

        // ── Animations ────────────────────────────────────────────────────

        private IEnumerator AnimateXP(float from, float to)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 2f;
                float xp = Mathf.Lerp(from, to, t);
                UpdateXPBar(xp, _displayedLevel);
                yield return null;
            }
            UpdateXPBar(to, _displayedLevel);
        }

        public void ShowNotification(string message, float duration = 3f)
        {
            if (_notificationCoroutine != null) StopCoroutine(_notificationCoroutine);
            _notificationCoroutine = StartCoroutine(ShowNotificationCoroutine(message, duration));
        }

        private IEnumerator ShowNotificationCoroutine(string message, float duration)
        {
            if (notificationText != null) notificationText.text = message;
            if (notificationGroup != null)
            {
                notificationGroup.alpha = 0f;
                float t = 0f;
                while (t < 0.3f) { t += Time.deltaTime; notificationGroup.alpha = t / 0.3f; yield return null; }
                notificationGroup.alpha = 1f;
                yield return new WaitForSeconds(duration);
                t = 0.3f;
                while (t > 0f) { t -= Time.deltaTime; notificationGroup.alpha = t / 0.3f; yield return null; }
                notificationGroup.alpha = 0f;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static string FormatPuzzleName(string key) => key switch
        {
            "superposition_intro" => "🌸 Superposition Flower",
            "hadamard_basic"      => "✨ Hadamard Sparkle",
            "cnot_entanglement"   => "🦋 Entangle Fireflies",
            "bell_state"          => "🔗 Bell State",
            "grover_2qubit"       => "🔮 2-Qubit Treasure Hunt",
            "grover_3qubit"       => "🔮 3-Qubit Treasure Hunt",
            "shor_factor6"        => "🔒 Crack Lock: N=6",
            "shor_factor15"       => "🔒 Crack Lock: N=15",
            _                     => key,
        };

        private static string GetHintForPuzzle(string key) => key switch
        {
            "superposition_intro" => "💡 Focus your mind to apply the Hadamard gate — it creates superposition!",
            "grover_2qubit"       => "💡 Apply Grover iterations until the bright firefly shines the most!",
            "shor_factor6"        => "💡 Apply QFT step by step — each focus builds the Fourier pattern.",
            "cnot_entanglement"   => "💡 H first, then CNOT — that creates a Bell state!",
            _                     => "💡 Focus to apply gates, relax to measure.",
        };
    }
}
