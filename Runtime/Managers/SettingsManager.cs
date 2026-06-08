using UnityEngine;
using System;
using System.Reflection;

namespace MetaDyn
{
    /// <summary>
    /// Singleton Settings Manager for managing game settings (audio, graphics, controls, etc.)
    /// Settings are persisted to PlayerPrefs and loaded on startup.
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        [Header("Audio Settings")]
        [Range(0f, 1f)]
        [Tooltip("Master volume (0 = mute, 1 = full volume)")]
        public float masterVolume = 1.0f;

        [Range(0f, 1f)]
        [Tooltip("Music volume (0 = mute, 1 = full volume)")]
        public float musicVolume = 0.8f;

        [Range(0f, 1f)]
        [Tooltip("SFX volume (0 = mute, 1 = full volume)")]
        public float sfxVolume = 1.0f;

        [Range(0f, 1f)]
        [Tooltip("Voice chat volume (0 = mute, 1 = full volume)")]
        public float voiceVolume = 1.0f;

        [Range(0f, 1f)]
        [Tooltip("UI sound volume (0 = mute, 1 = full volume)")]
        public float uiVolume = 0.5f;

        [Header("Graphics Settings")]
        [Tooltip("VSync enabled")]
        public bool vsyncEnabled = true;

        [Tooltip("Target frame rate (0 = unlimited)")]
        public int targetFrameRate = 60;

        [Header("Controls Settings")]
        [Range(0.1f, 2.0f)]
        [Tooltip("Mouse sensitivity")]
        public float mouseSensitivity = 1.0f;

        [Tooltip("Invert Y axis for camera")]
        public bool invertY = false;

        [Header("Voice Settings")]
        [Tooltip("Selected microphone device")]
        public string microphoneDevice = "";

        // Events for settings changes
        public event Action<float> OnMasterVolumeChanged;
        public event Action<float> OnMusicVolumeChanged;
        public event Action<float> OnSFXVolumeChanged;
        public event Action<float> OnVoiceVolumeChanged;
        public event Action<float> OnUIVolumeChanged;
        public event Action<bool> OnVSyncChanged;
        public event Action<int> OnTargetFrameRateChanged;
        public event Action<float> OnMouseSensitivityChanged;
        public event Action<bool> OnInvertYChanged;
        public event Action<string> OnMicrophoneDeviceChanged;

        // PlayerPrefs keys
        private const string KEY_MASTER_VOLUME = "Settings_MasterVolume";
        private const string KEY_MUSIC_VOLUME = "Settings_MusicVolume";
        private const string KEY_SFX_VOLUME = "Settings_SFXVolume";
        private const string KEY_VOICE_VOLUME = "Settings_VoiceVolume";
        private const string KEY_UI_VOLUME = "Settings_UIVolume";
        private const string KEY_VSYNC = "Settings_VSync";
        private const string KEY_TARGET_FPS = "Settings_TargetFPS";
        private const string KEY_MOUSE_SENSITIVITY = "Settings_MouseSensitivity";
        private const string KEY_INVERT_Y = "Settings_InvertY";
        private const string KEY_MICROPHONE_DEVICE = "Settings_MicrophoneDevice";

        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadSettings();
            ApplySettings();
        }

        /// <summary>
        /// Load settings from PlayerPrefs
        /// </summary>
        public void LoadSettings()
        {
            masterVolume = PlayerPrefs.GetFloat(KEY_MASTER_VOLUME, 1.0f);
            musicVolume = PlayerPrefs.GetFloat(KEY_MUSIC_VOLUME, 0.8f);
            sfxVolume = PlayerPrefs.GetFloat(KEY_SFX_VOLUME, 1.0f);
            voiceVolume = PlayerPrefs.GetFloat(KEY_VOICE_VOLUME, 1.0f);
            uiVolume = PlayerPrefs.GetFloat(KEY_UI_VOLUME, 0.5f);
            vsyncEnabled = PlayerPrefs.GetInt(KEY_VSYNC, 1) == 1;
            targetFrameRate = PlayerPrefs.GetInt(KEY_TARGET_FPS, 60);
            mouseSensitivity = PlayerPrefs.GetFloat(KEY_MOUSE_SENSITIVITY, 1.0f);
            invertY = PlayerPrefs.GetInt(KEY_INVERT_Y, 0) == 1;
            microphoneDevice = PlayerPrefs.GetString(KEY_MICROPHONE_DEVICE, "");

            Debug.Log($"[SettingsManager] Settings loaded from PlayerPrefs");
        }

        /// <summary>
        /// Save settings to PlayerPrefs
        /// </summary>
        public void SaveSettings()
        {
            PlayerPrefs.SetFloat(KEY_MASTER_VOLUME, masterVolume);
            PlayerPrefs.SetFloat(KEY_MUSIC_VOLUME, musicVolume);
            PlayerPrefs.SetFloat(KEY_SFX_VOLUME, sfxVolume);
            PlayerPrefs.SetFloat(KEY_VOICE_VOLUME, voiceVolume);
            PlayerPrefs.SetFloat(KEY_UI_VOLUME, uiVolume);
            PlayerPrefs.SetInt(KEY_VSYNC, vsyncEnabled ? 1 : 0);
            PlayerPrefs.SetInt(KEY_TARGET_FPS, targetFrameRate);
            PlayerPrefs.SetFloat(KEY_MOUSE_SENSITIVITY, mouseSensitivity);
            PlayerPrefs.SetInt(KEY_INVERT_Y, invertY ? 1 : 0);
            PlayerPrefs.SetString(KEY_MICROPHONE_DEVICE, microphoneDevice);
            PlayerPrefs.Save();

            Debug.Log($"[SettingsManager] Settings saved to PlayerPrefs");
        }

        /// <summary>
        /// Apply settings to Unity systems
        /// </summary>
        public void ApplySettings()
        {
            // Apply master volume to AudioListener
            AudioListener.volume = masterVolume;

            ApplyFramePacing();

            Debug.Log($"[SettingsManager] Settings applied - Master Volume: {masterVolume}, VSync: {vsyncEnabled}, Target FPS: {targetFrameRate}");
        }

        // Public setters with events and auto-save

        public void SetMasterVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            AudioListener.volume = masterVolume;
            OnMasterVolumeChanged?.Invoke(masterVolume);

            // Update UI sound volume if UIManager exists
            if (UIManager.Instance != null)
            {
                UIManager.Instance.UpdateUIVolume();
            }

            SaveSettings();
        }

        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            OnMusicVolumeChanged?.Invoke(musicVolume);
            SaveSettings();
        }

        public void SetSFXVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            OnSFXVolumeChanged?.Invoke(sfxVolume);
            SaveSettings();
        }

        public void SetVoiceVolume(float volume)
        {
            voiceVolume = Mathf.Clamp01(volume);
            OnVoiceVolumeChanged?.Invoke(voiceVolume);
            SaveSettings();
        }

        public void SetUIVolume(float volume)
        {
            uiVolume = Mathf.Clamp01(volume);
            OnUIVolumeChanged?.Invoke(uiVolume);
            SaveSettings();
        }

        public void SetVSync(bool enabled)
        {
            vsyncEnabled = enabled;
            ApplyFramePacing();
            OnVSyncChanged?.Invoke(vsyncEnabled);
            SaveSettings();
        }

        public void SetTargetFrameRate(int fps)
        {
            targetFrameRate = fps;
            ApplyFramePacing();
            OnTargetFrameRateChanged?.Invoke(targetFrameRate);
            SaveSettings();
        }

        public void SetMouseSensitivity(float sensitivity)
        {
            mouseSensitivity = Mathf.Clamp(sensitivity, 0.1f, 2.0f);
            OnMouseSensitivityChanged?.Invoke(mouseSensitivity);
            SaveSettings();
        }

        public void SetInvertY(bool invert)
        {
            invertY = invert;
            OnInvertYChanged?.Invoke(invertY);
            SaveSettings();
        }

        public void SetMicrophoneDevice(string deviceName)
        {
            microphoneDevice = deviceName;
            OnMicrophoneDeviceChanged?.Invoke(microphoneDevice);
            SaveSettings();
        }

        /// <summary>
        /// Reset all settings to default values
        /// </summary>
        public void ResetToDefaults()
        {
            SetMasterVolume(1.0f);
            SetMusicVolume(0.8f);
            SetSFXVolume(1.0f);
            SetVoiceVolume(1.0f);
            SetUIVolume(0.5f);
            SetVSync(true);
            SetTargetFrameRate(60);
            SetMouseSensitivity(1.0f);
            SetInvertY(false);
            SetMicrophoneDevice("");

            Debug.Log("[SettingsManager] Settings reset to defaults");
        }

        private void ApplyFramePacing()
        {
            if (targetFrameRate <= 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 0;
                return;
            }

            int vSyncDivisor = GetVSyncDivisorForTarget(targetFrameRate);
            if (vSyncDivisor > 0)
            {
                QualitySettings.vSyncCount = vSyncDivisor;
                Application.targetFrameRate = -1;
                return;
            }

            // Fall back to a direct frame cap when the display refresh rate doesn't divide cleanly.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
        }

        private int GetVSyncDivisorForTarget(int targetFps)
        {
            int refreshRate = GetDisplayRefreshRate();
            if (refreshRate <= 0 || targetFps <= 0 || targetFps > refreshRate)
                return 0;

            int divisor = Mathf.RoundToInt((float)refreshRate / targetFps);
            if (divisor < 1 || divisor > 4)
                return 0;

            int derivedTarget = refreshRate / divisor;
            if (Mathf.Abs(derivedTarget - targetFps) > 1)
                return 0;

            return divisor;
        }

        private int GetDisplayRefreshRate()
        {
            Resolution resolution = Screen.currentResolution;

            PropertyInfo refreshRateRatioProperty = typeof(Resolution).GetProperty("refreshRateRatio");
            if (refreshRateRatioProperty != null)
            {
                object refreshRateRatio = refreshRateRatioProperty.GetValue(resolution);
                if (refreshRateRatio != null)
                {
                    PropertyInfo valueProperty = refreshRateRatio.GetType().GetProperty("value");
                    if (valueProperty != null)
                    {
                        object value = valueProperty.GetValue(refreshRateRatio);
                        if (value is double refreshRateDouble)
                            return Mathf.RoundToInt((float)refreshRateDouble);
                        if (value is float refreshRateFloat)
                            return Mathf.RoundToInt(refreshRateFloat);
                    }
                }
            }

#pragma warning disable CS0618
            if (resolution.refreshRate > 0)
                return resolution.refreshRate;
#pragma warning restore CS0618

            return 60;
        }
    }
}
