using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace QuantumMagicGarden
{
    /// <summary>
    /// HTTP client for communicating with the Python FastAPI backend.
    /// Shared by BCIManager and AdaptiveLearningManager.
    /// All requests are fire-and-forget from Unity's main thread
    /// (awaited in async methods).
    /// </summary>
    public class BackendClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public BackendClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        // ── BCI classification ────────────────────────────────────────────

        public async Task<BCIClassifyResponse> ClassifyBCIAsync(
            string sessionId,
            float[,] eeg,
            float sfreq)
        {
            // Convert float[channels, samples] → List<List<float>>
            int channels = eeg.GetLength(0);
            int samples  = eeg.GetLength(1);
            var eegList  = new List<List<float>>();
            for (int ch = 0; ch < channels; ch++)
            {
                var row = new List<float>();
                for (int s = 0; s < samples; s++) row.Add(eeg[ch, s]);
                eegList.Add(row);
            }

            var body = new
            {
                session_id = sessionId,
                eeg_data   = eegList,
                sfreq,
            };
            return await PostAsync<BCIClassifyResponse>("/api/bci/classify", body);
        }

        // ── RL recommendation ─────────────────────────────────────────────

        public async Task<RLResponse> GetRLRecommendationAsync(Learning.RLRequest req)
        {
            return await PostAsync<RLResponse>("/api/rl/recommend", req);
        }

        // ── PQL curriculum ────────────────────────────────────────────────

        public async Task<PQLApiResponse> GetPQLRecommendationAsync(Learning.PQLRequest req)
        {
            return await PostAsync<PQLApiResponse>("/api/pql/next-puzzle", req);
        }

        // ── QAOA optimisation ─────────────────────────────────────────────

        public async Task<QAOAApiResponse> OptimiseQAOAAsync(
            string puzzleType, int nQubits = 3, int pLayers = 3)
        {
            var body = new { puzzle_type = puzzleType, n_qubits = nQubits, p_layers = pLayers };
            return await PostAsync<QAOAApiResponse>("/api/qaoa/optimise", body);
        }

        // ── Generic HTTP POST ─────────────────────────────────────────────

        private async Task<T> PostAsync<T>(string path, object body)
        {
            string json = JsonUtility.ToJson(body);  // Unity's built-in JsonUtility
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(_baseUrl + path, content);
            resp.EnsureSuccessStatusCode();
            string responseJson = await resp.Content.ReadAsStringAsync();
            return JsonUtility.FromJson<T>(responseJson);
        }

        public void Dispose() => _http?.Dispose();
    }

    // ── Response DTOs ─────────────────────────────────────────────────────

    [System.Serializable]
    public class BCIClassifyResponse
    {
        public string session_id;
        public string state;
        public float  confidence;
        public string gate_action;
        // probabilities dict not directly serialisable via JsonUtility; use gate_action
    }

    [System.Serializable]
    public class RLResponse
    {
        public string session_id;
        public string action_name;
        public int    recommended_level;
        public bool   show_hint;
        public bool   skip_puzzle;
        public float  confidence;
    }

    [System.Serializable]
    public class PQLApiResponse
    {
        public string session_id;
        public string next_puzzle;
        public int    puzzle_idx;
        public string reason;
    }

    [System.Serializable]
    public class QAOAApiResponse
    {
        public string   puzzle_type;
        public float    optimal_cost;
        public float    target_state_probability;
        public int      circuit_depth;
        public string[] gate_sequence;
        public bool     converged;
        public int      iterations;
    }
}
