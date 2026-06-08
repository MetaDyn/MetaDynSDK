using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events; // Added for UnityEvent
using TMPro;
using System.Collections;
using System.Runtime.InteropServices;

namespace MetaDyn.Audio
{
    // [System.Serializable] allows this to show in Inspector
    [System.Serializable]
    public class AudioDataEvent : UnityEvent<byte[]> { }

    public class MicrophoneRecorder : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject parentPanel;
        [SerializeField] private Button recordButton;
        
        [Header("Audio Settings")]
        [SerializeField] private int recordingLength = 10;
        [SerializeField] private int sampleRate = 44100;
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Enable local playback of recorded audio (disable when using with voice AI)")]
        [SerializeField] private bool playbackEnabled = true;
        
        [Header("UI Feedback")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Image recordingIndicator;
        [SerializeField] private Color recordingColor = Color.red;
        [SerializeField] private Color idleColor = Color.white;

        [Header("Events")]
        // This event will carry the WAV byte array to your future API script
        public AudioDataEvent OnRecordingCompleted; 
        
        private AudioClip recordedClip;
        private bool isRecording = false;
        private float recordingStartTime;
        private EventTrigger eventTrigger;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void StartMicrophoneRecording(int sampleRate);
        [DllImport("__Internal")]
        private static extern void StopMicrophoneRecording();
        [DllImport("__Internal")]
        private static extern bool IsMicrophoneRecording();
        [DllImport("__Internal")]
        private static extern int GetRecordedAudioLength();
        [DllImport("__Internal")]
        private static extern void GetRecordedAudioData(float[] data, int length);
#endif

        void Start()
        {
            if (parentPanel == null) Debug.LogError("[MetaDyn.Audio] Parent Panel not assigned!");
            if (recordButton == null)
            {
                Debug.LogError("[MetaDyn.Audio] Record Button not assigned!");
                return;
            }

            SetupButtonEvents();

            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            if (recordingIndicator != null) recordingIndicator.color = idleColor;
            
            UpdateStatus("Ready to record");
        }

        // ... [SetupButtonEvents, OnPointerDown, OnPointerUp remain exactly the same] ...
        private void SetupButtonEvents()
        {
            eventTrigger = recordButton.gameObject.GetComponent<EventTrigger>();
            if (eventTrigger == null) eventTrigger = recordButton.gameObject.AddComponent<EventTrigger>();
            eventTrigger.triggers.Clear();

            EventTrigger.Entry pointerDownEntry = new EventTrigger.Entry();
            pointerDownEntry.eventID = EventTriggerType.PointerDown;
            pointerDownEntry.callback.AddListener((data) => { OnPointerDown((PointerEventData)data); });
            eventTrigger.triggers.Add(pointerDownEntry);

            EventTrigger.Entry pointerUpEntry = new EventTrigger.Entry();
            pointerUpEntry.eventID = EventTriggerType.PointerUp;
            pointerUpEntry.callback.AddListener((data) => { OnPointerUp((PointerEventData)data); });
            eventTrigger.triggers.Add(pointerUpEntry);
        }

        public void OnPointerDown(PointerEventData eventData) => StartRecording();
        public void OnPointerUp(PointerEventData eventData) => StopRecording();

        private void StartRecording()
        {
            if (isRecording) return;
            if (audioSource.isPlaying) audioSource.Stop();

#if UNITY_WEBGL && !UNITY_EDITOR
            StartMicrophoneRecording(sampleRate);
#else
            if (Microphone.devices.Length > 0)
                recordedClip = Microphone.Start(null, false, recordingLength, sampleRate);
            else {
                UpdateStatus("No microphone detected");
                return;
            }
#endif

            isRecording = true;
            recordingStartTime = Time.time;
            UpdateStatus("Recording...");
            
            if (recordingIndicator != null) recordingIndicator.color = recordingColor;
            if (recordButton != null)
            {
                ColorBlock colors = recordButton.colors;
                colors.normalColor = recordingColor;
                recordButton.colors = colors;
            }
        }

        private void StopRecording()
        {
            if (!isRecording) return;

            float recordingDuration = Time.time - recordingStartTime;
            isRecording = false;
            UpdateStatus("Processing...");

            // Visual Reset
            if (recordingIndicator != null) recordingIndicator.color = idleColor;
            if (recordButton != null)
            {
                ColorBlock colors = recordButton.colors;
                colors.normalColor = idleColor;
                recordButton.colors = colors;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            StopMicrophoneRecording();
            StartCoroutine(ProcessWebGLRecording());
#else
            int recordedSamples = (int)(recordingDuration * sampleRate);
            Microphone.End(null);
            AudioClip trimmedClip = TrimAudioClip(recordedClip, recordedSamples);
            
            // Divert to the new Finalize method
            FinalizeRecording(trimmedClip);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator ProcessWebGLRecording()
        {
            // Wait for AudioWorklet flush (50ms in JS + 50ms buffer = 100ms total)
            // This ensures all audio buffers are transferred from audio thread
            yield return new WaitForSeconds(0.1f);

            int length = GetRecordedAudioLength();
            if (length > 0)
            {
                float[] audioData = new float[length];
                GetRecordedAudioData(audioData, length);

                recordedClip = AudioClip.Create("WebGLRecording", length, 1, sampleRate, false);
                recordedClip.SetData(audioData, 0);

                Debug.Log($"[MetaDyn.Audio] WebGL recording complete: {length} samples ({(float)length / sampleRate:F2}s)");

                // Divert to the new Finalize method
                FinalizeRecording(recordedClip);
            }
            else
            {
                Debug.LogError("[MetaDyn.Audio] No audio data received from WebGL");
                UpdateStatus("Recording failed");
            }
        }
#endif

        // --- NEW METHOD: Centralizes logic for both Native and WebGL ---
        private void FinalizeRecording(AudioClip clip)
        {
            if (clip == null)
            {
                UpdateStatus("Recording failed");
                return;
            }

            // 1. OPTIONAL: Play the audio locally (can be disabled via inspector)
            if (playbackEnabled)
            {
                PlayRecording(clip);
            }
            else
            {
                // Skip playback, go straight to ready
                UpdateStatus("Ready to record");
            }

            // 2. Convert to WAV and fire event for API
            Debug.Log("[MetaDyn.Audio] Encoding to WAV...");
            byte[] wavData = AudioUtils.EncodeToWAV(clip);
            
            Debug.Log($"[MetaDyn.Audio] WAV Encoded. Size: {wavData.Length} bytes. Invoking Event.");
            OnRecordingCompleted?.Invoke(wavData);
        }

        private AudioClip TrimAudioClip(AudioClip clip, int samples)
        {
            // Safety check to prevent array bounds errors if recording was super short
            if (samples <= 0) return clip; 

            float[] data = new float[samples * clip.channels];
            clip.GetData(data, 0);

            AudioClip trimmedClip = AudioClip.Create("RecordedAudio", samples, clip.channels, clip.frequency, false);
            trimmedClip.SetData(data, 0);
            return trimmedClip;
        }

        private void PlayRecording(AudioClip clip)
        {
            audioSource.clip = clip;
            audioSource.Play();
            UpdateStatus("Playing back...");
            StartCoroutine(WaitForPlaybackEnd());
        }

        private IEnumerator WaitForPlaybackEnd()
        {
            yield return new WaitWhile(() => audioSource.isPlaying);
            UpdateStatus("Ready to record");
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (isRecording) StopMicrophoneRecording();
#else
            if (isRecording) Microphone.End(null);
#endif
        }

        // Getters/Setters
        public AudioClip GetRecordedClip() => recordedClip;
        public void ToggleRecording() { if (isRecording) StopRecording(); else StartRecording(); }
        public bool IsRecording() => isRecording;
        public void SetPanelVisible(bool visible) { if (parentPanel != null) parentPanel.SetActive(visible); }
    }
}