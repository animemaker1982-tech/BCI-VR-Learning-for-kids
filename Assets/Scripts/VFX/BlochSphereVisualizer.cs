using System.Collections;
using UnityEngine;

namespace QuantumMagicGarden.VFX
{
    /// <summary>
    /// Bloch Sphere Visualizer — renders the quantum state of a single qubit
    /// as a 3D arrow on a sphere.
    ///
    /// The Bloch vector (x, y, z) maps to a point on the unit sphere:
    ///   |0⟩ = north pole (0,0,1)
    ///   |1⟩ = south pole (0,0,-1)
    ///   |+⟩ = equator   (1,0,0)
    ///
    /// Probability bars are shown as cylinder overlays for each basis state.
    /// </summary>
    public class BlochSphereVisualizer : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────
        [Header("Bloch Sphere Components")]
        [SerializeField] private Transform stateArrow;
        [SerializeField] private Transform sphereMesh;
        [SerializeField] private LineRenderer blochLine;

        [Header("Probability Bars")]
        [SerializeField] private Transform prob0Bar;
        [SerializeField] private Transform prob1Bar;
        [SerializeField] private Renderer prob0Renderer;
        [SerializeField] private Renderer prob1Renderer;

        [Header("Labels")]
        [SerializeField] private TMPro.TextMeshProUGUI state0Label;
        [SerializeField] private TMPro.TextMeshProUGUI state1Label;
        [SerializeField] private TMPro.TextMeshProUGUI blochCoordLabel;

        [Header("Colours")]
        [SerializeField] private Color prob0Color = new(0.2f, 0.6f, 1.0f);
        [SerializeField] private Color prob1Color = new(1.0f, 0.4f, 0.4f);
        [SerializeField] private Color superpositionColor = new(0.8f, 0.3f, 1.0f);

        [Header("Settings")]
        [SerializeField] private float sphereRadius   = 1f;
        [SerializeField] private float animationSpeed = 3f;

        // ── Private ───────────────────────────────────────────────────────
        private Vector3 _targetBloch;
        private Vector3 _currentBloch = Vector3.forward;
        private float[] _probs = { 1f, 0f };

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Start()
        {
            UpdateState(0f, 0f, 1f, new[] { 1f, 0f });
        }

        private void Update()
        {
            // Smoothly animate Bloch vector
            _currentBloch = Vector3.Lerp(_currentBloch, _targetBloch, Time.deltaTime * animationSpeed);
            ApplyBlochVector(_currentBloch);
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Update the visualiser with a new Bloch vector and probability array.
        /// Called by GardenController after each gate application.
        /// </summary>
        public void UpdateState(float bx, float by, float bz, float[] probs)
        {
            _targetBloch = new Vector3(bx, bz, by) * sphereRadius;  // Unity Y-up convention
            _probs = probs;
            UpdateProbBars();
            UpdateLabels(bx, by, bz);
        }

        // ── Rendering ────────────────────────────────────────────────────

        private void ApplyBlochVector(Vector3 b)
        {
            if (stateArrow != null)
            {
                stateArrow.localPosition = b * 0.5f;
                if (b.sqrMagnitude > 0.001f)
                    stateArrow.localRotation = Quaternion.LookRotation(b.normalized, Vector3.up);
            }

            if (blochLine != null)
            {
                blochLine.SetPosition(0, Vector3.zero);
                blochLine.SetPosition(1, b);
                // Colour by Z component (north = blue, south = red, equator = purple)
                float t = (b.y / sphereRadius + 1f) * 0.5f;
                Color c = Color.Lerp(prob1Color, prob0Color, t);
                blochLine.startColor = c;
                blochLine.endColor = c;
            }

            if (sphereMesh != null)
            {
                float superpositionness = 1f - Mathf.Abs(_currentBloch.y / sphereRadius);
                sphereMesh.GetComponent<Renderer>()?.material.SetColor(
                    "_EmissionColor",
                    Color.Lerp(Color.clear, superpositionColor * 0.3f, superpositionness)
                );
            }
        }

        private void UpdateProbBars()
        {
            float p0 = _probs.Length > 0 ? _probs[0] : 1f;
            float p1 = _probs.Length > 1 ? _probs[1] : 0f;

            // Scale bars along Y axis
            if (prob0Bar != null)
            {
                var s = prob0Bar.localScale;
                prob0Bar.localScale = new Vector3(s.x, Mathf.Max(0.01f, p0), s.z);
            }
            if (prob1Bar != null)
            {
                var s = prob1Bar.localScale;
                prob1Bar.localScale = new Vector3(s.x, Mathf.Max(0.01f, p1), s.z);
            }

            // Colour
            if (prob0Renderer != null)
            {
                float emission = p0 > 0.9f ? 0.5f : 0f;
                prob0Renderer.material.color = Color.Lerp(Color.gray, prob0Color, p0);
                prob0Renderer.material.SetColor("_EmissionColor", prob0Color * emission);
            }
            if (prob1Renderer != null)
            {
                float emission = p1 > 0.9f ? 0.5f : 0f;
                prob1Renderer.material.color = Color.Lerp(Color.gray, prob1Color, p1);
                prob1Renderer.material.SetColor("_EmissionColor", prob1Color * emission);
            }
        }

        private void UpdateLabels(float bx, float by, float bz)
        {
            float p0 = _probs.Length > 0 ? _probs[0] : 1f;
            float p1 = _probs.Length > 1 ? _probs[1] : 0f;

            if (state0Label != null) state0Label.text = $"|0⟩: {p0 * 100f:F0}%";
            if (state1Label != null) state1Label.text = $"|1⟩: {p1 * 100f:F0}%";
            if (blochCoordLabel != null)
                blochCoordLabel.text = $"({bx:F2}, {by:F2}, {bz:F2})";
        }
    }
}
