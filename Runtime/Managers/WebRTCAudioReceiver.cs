using UnityEngine;
using System.Runtime.InteropServices;

namespace MetaDyn
{
    /// <summary>
    /// Receives WebRTC audio samples from JavaScript for lip sync integration.
    /// NOTE: Audio playback is handled by browser's Web Audio API (PannerNode),
    /// this component only pulls samples for lip sync animation.
    /// </summary>
    public class WebRTCAudioReceiver : MonoBehaviour
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int WebRTC_GetAudioSamples(string peerId, float[] buffer, int sampleCount);

        [DllImport("__Internal")]
        private static extern int WebRTC_GetAudioSampleRate(string peerId);
        #endif

        [Header("Configuration")]
        [Tooltip("Player ID to receive audio from")]
        public string remotePeerId;

        [Header("Lip Sync Integration")]
        [Tooltip("Optional lip sync component for mouth animation (AvatarSDKPlayerLipSync or Wolf3DPlayerLipSync)")]
        public MonoBehaviour lipSyncController;

        [Header("Audio Level Settings")]
        [Tooltip("Audio level threshold to trigger speaking state (lip sync)")]
        [Range(0.001f, 0.1f)]
        public float speakingThreshold = 0.01f;

        [Tooltip("Smoothing factor for audio level (lower = more smooth)")]
        [Range(0.1f, 1.0f)]
        public float audioLevelSmoothing = 0.3f;

        private bool _isInitialized = false;
        private int _sampleRate = 48000;

        // Audio level tracking for lip sync
        private float _currentAudioLevel = 0f;
        private bool _isSpeaking = false;

        // Audio sample buffer for lip sync analysis
        private float[] _audioBuffer;
        private const int BUFFER_SIZE = 4096;
        private Coroutine _audioStreamCoroutine;

        /// <summary>
        /// Initialize with remote player ID and optional lip sync component
        /// </summary>
        public void Initialize(string peerId, MonoBehaviour lipSync = null)
        {
            Debug.Log($"[WebRTCAudioReceiver] ENTER Initialize - peerId: {peerId}, lipSync: {(lipSync != null ? "found" : "null")}");
            remotePeerId = peerId;
            lipSyncController = lipSync;

            #if UNITY_WEBGL && !UNITY_EDITOR
            // Create audio buffer for sample analysis
            _audioBuffer = new float[BUFFER_SIZE];

            // Get sample rate from JavaScript
            _sampleRate = WebRTC_GetAudioSampleRate(remotePeerId);
            if (_sampleRate == 0)
            {
                _sampleRate = 48000; // Fallback to 48kHz (standard WebRTC)
                Debug.LogWarning($"[WebRTCAudioReceiver] Could not get sample rate for peer {remotePeerId}, using fallback: {_sampleRate}Hz");
            }

            // Start coroutine to pull samples for lip sync
            _audioStreamCoroutine = StartCoroutine(StreamAudioData());

            _isInitialized = true;

            Debug.Log($"[WebRTCAudioReceiver] Initialized for peer {remotePeerId} at {_sampleRate}Hz, lip sync: {(lipSync != null ? lipSync.GetType().Name : "disabled")}");
            #else
            Debug.LogWarning("[WebRTCAudioReceiver] Only works in WebGL builds");
            #endif
        }

        /// <summary>
        /// Coroutine to continuously pull audio samples from JavaScript for lip sync analysis
        /// </summary>
        private System.Collections.IEnumerator StreamAudioData()
        {
            int totalSamplesReceived = 0;
            float debugTimer = 0f;

            Debug.Log($"[WebRTCAudioReceiver] StreamAudioData coroutine started for peer {remotePeerId}");

            while (true)
            {
                #if UNITY_WEBGL && !UNITY_EDITOR
                if (!string.IsNullOrEmpty(remotePeerId))
                {
                    // Pull audio samples from JavaScript ring buffer
                    int samplesReceived = WebRTC_GetAudioSamples(remotePeerId, _audioBuffer, BUFFER_SIZE);

                    if (samplesReceived > 0)
                    {
                        totalSamplesReceived += samplesReceived;

                        // Calculate audio level for lip sync triggering
                        UpdateAudioLevel(_audioBuffer, samplesReceived);
                    }

                    // Debug log every 2 seconds
                    debugTimer += Time.deltaTime;
                    if (debugTimer >= 2f)
                    {
                        Debug.Log($"[WebRTCAudioReceiver] Peer {remotePeerId}: Received {totalSamplesReceived} samples in last 2s, audio level: {_currentAudioLevel:F4}, speaking: {_isSpeaking}");
                        totalSamplesReceived = 0;
                        debugTimer = 0f;
                    }
                }
                #endif

                // Wait for next frame
                yield return null;
            }
        }

        /// <summary>
        /// Calculate RMS audio level and update lip sync state
        /// </summary>
        private void UpdateAudioLevel(float[] data, int sampleCount)
        {
            if (sampleCount == 0)
            {
                // No audio data - smooth level toward zero
                _currentAudioLevel = Mathf.Lerp(_currentAudioLevel, 0f, audioLevelSmoothing);
                UpdateLipSyncState();
                return;
            }

            // Calculate RMS (root mean square) for audio level
            float sum = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                sum += data[i] * data[i];
            }
            float rms = Mathf.Sqrt(sum / sampleCount);

            // Smooth the audio level
            _currentAudioLevel = Mathf.Lerp(_currentAudioLevel, rms, audioLevelSmoothing);

            // Update lip sync based on threshold
            UpdateLipSyncState();
        }

        /// <summary>
        /// Update lip sync speaking state based on audio level
        /// </summary>
        private void UpdateLipSyncState()
        {
            bool shouldBeSpeaking = _currentAudioLevel > speakingThreshold;

            // State changed - update lip sync
            if (shouldBeSpeaking != _isSpeaking)
            {
                _isSpeaking = shouldBeSpeaking;

                if (lipSyncController != null)
                {
                    if (_isSpeaking)
                    {
                        // Start lip sync animation
                        lipSyncController.SendMessage("StartSpeaking", SendMessageOptions.DontRequireReceiver);
                    }
                    else
                    {
                        // Stop lip sync animation
                        lipSyncController.SendMessage("StopSpeaking", SendMessageOptions.DontRequireReceiver);
                    }
                }
            }
        }

        /// <summary>
        /// Get current audio level (for debugging)
        /// </summary>
        public float GetAudioLevel()
        {
            return _currentAudioLevel;
        }

        /// <summary>
        /// Check if currently speaking (for debugging)
        /// </summary>
        public bool IsSpeaking()
        {
            return _isSpeaking;
        }

        private void OnDestroy()
        {
            // Stop audio streaming coroutine
            if (_audioStreamCoroutine != null)
            {
                StopCoroutine(_audioStreamCoroutine);
                _audioStreamCoroutine = null;
            }

            // Ensure lip sync is stopped
            if (lipSyncController != null && _isSpeaking)
            {
                lipSyncController.SendMessage("StopSpeaking", SendMessageOptions.DontRequireReceiver);
            }

            Debug.Log($"[WebRTCAudioReceiver] Destroyed for peer {remotePeerId}");
        }

    }
}
