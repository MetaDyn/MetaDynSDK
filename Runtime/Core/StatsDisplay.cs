using UnityEngine;
using TMPro;
using System.Text;
using Unity.Netcode;
using System;

namespace MetaDyn
{
    /// <summary>
    /// Displays FPS, Ping, and Memory stats in the UI.
    /// Updated for NGO/UGS compatibility.
    /// </summary>
    public class StatsDisplay : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject statsPanel; 
        [SerializeField] private TMP_Text statsText;

        [Header("Settings")]
        [SerializeField] private float updateInterval = 0.5f;

        // Internal tracking
        private float _accumulatedTime = 0f;
        private int _frameCount = 0;
        private float _minFrameTime = float.MaxValue;
        private float _maxFrameTime = 0f;
        private StringBuilder _sb = new StringBuilder(500);

        private void Start()
        {
            if (statsPanel == null && transform.childCount > 0) 
                statsPanel = transform.GetChild(0).gameObject;
        }

        private void Update()
        {
            float currentFrameTime = Time.unscaledDeltaTime;
            _accumulatedTime += currentFrameTime;
            _frameCount++;

            if (currentFrameTime < _minFrameTime) _minFrameTime = currentFrameTime;
            if (currentFrameTime > _maxFrameTime) _maxFrameTime = currentFrameTime;

            if (_accumulatedTime >= updateInterval)
            {
                UpdateStatsText();
                ResetAccumulators();
            }
        }

        private void UpdateStatsText()
        {
            if (statsText == null) return;

            float avgFrameTime = _accumulatedTime / _frameCount;
            float avgFPS = 1.0f / avgFrameTime;
            float minFPS = 1.0f / _maxFrameTime; 

            long totalMem = System.GC.GetTotalMemory(false) / 1048576;
            long textureMem = (long)Texture.currentTextureMemory / 1048576;

            // Network Ping (NGO)
            double rtt = 0;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
            {
                rtt = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
            }

            _sb.Clear();
            _sb.Append("FPS:      ").Append(avgFPS.ToString("F0")).Append(" (").Append((avgFrameTime * 1000f).ToString("F1")).Append(" ms)\n");
            _sb.Append("1% Low:   ").Append(minFPS.ToString("F0")).Append("\n");
            _sb.Append("Ping:     ").Append(rtt > 0 ? rtt.ToString("F0") : "--").Append(" ms\n");
            _sb.Append("----------------\n");
            _sb.Append("RAM (GC): ").Append(totalMem).Append(" MB\n");
            _sb.Append("VRAM:     ").Append(textureMem).Append(" MB");

            statsText.text = _sb.ToString();
        }

        private void ResetAccumulators()
        {
            _accumulatedTime = 0f;
            _frameCount = 0;
            _minFrameTime = float.MaxValue;
            _maxFrameTime = 0f;
        }

        public void ToggleStats()
        {
            if (statsPanel != null)
            {
                statsPanel.SetActive(!statsPanel.activeSelf);
            }
        }
    }
}
