using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MetaDyn
{
    /// <summary>
    /// UI Controller for Settings Menu
    /// Binds UI sliders/toggles to SettingsManager
    /// </summary>
    public class SettingsUI : MonoBehaviour
    {
        private static readonly int[] TargetFPSOptions = { 30, 60, 120, 0 };

        [Header("Audio Sliders")]
        [Tooltip("Master volume slider (0-1)")]
        public Slider masterVolumeSlider;

        [Tooltip("Music volume slider (0-1)")]
        public Slider musicVolumeSlider;

        [Tooltip("SFX volume slider (0-1)")]
        public Slider sfxVolumeSlider;

        [Tooltip("Voice chat volume slider (0-1)")]
        public Slider voiceVolumeSlider;

        [Tooltip("UI sound volume slider (0-1)")]
        public Slider uiVolumeSlider;

        [Header("Audio Labels (Optional)")]
        public TextMeshProUGUI masterVolumeLabel;
        public TextMeshProUGUI musicVolumeLabel;
        public TextMeshProUGUI sfxVolumeLabel;
        public TextMeshProUGUI voiceVolumeLabel;
        public TextMeshProUGUI uiVolumeLabel;

        [Header("Graphics")]
        public Toggle vsyncToggle;
        public Slider targetFPSSlider;
        public TextMeshProUGUI targetFPSLabel;

        [Header("Controls")]
        public Slider mouseSensitivitySlider;
        public TextMeshProUGUI mouseSensitivityLabel;
        public Toggle invertYToggle;

        [Header("Buttons")]
        public Button resetButton;
        public Button closeButton;

        private void Start()
        {
            if (SettingsManager.Instance == null)
            {
                Debug.LogError("[SettingsUI] SettingsManager not found! Make sure SettingsManager GameObject exists in scene.");
                return;
            }

            InitializeUI();
            AddListeners();
        }

        private void OnEnable()
        {
            if (SettingsManager.Instance != null)
            {
                RefreshUI();
            }
        }

        /// <summary>
        /// Initialize UI sliders/toggles with current settings
        /// </summary>
        private void InitializeUI()
        {
            var settings = SettingsManager.Instance;

            // Audio sliders
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.value = settings.masterVolume;
                UpdateLabel(masterVolumeLabel, settings.masterVolume);
            }

            if (musicVolumeSlider != null)
            {
                musicVolumeSlider.value = settings.musicVolume;
                UpdateLabel(musicVolumeLabel, settings.musicVolume);
            }

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.value = settings.sfxVolume;
                UpdateLabel(sfxVolumeLabel, settings.sfxVolume);
            }

            if (voiceVolumeSlider != null)
            {
                voiceVolumeSlider.value = settings.voiceVolume;
                UpdateLabel(voiceVolumeLabel, settings.voiceVolume);
            }

            if (uiVolumeSlider != null)
            {
                uiVolumeSlider.value = settings.uiVolume;
                UpdateLabel(uiVolumeLabel, settings.uiVolume);
            }

            // Graphics
            if (vsyncToggle != null)
                vsyncToggle.isOn = settings.vsyncEnabled;

            if (targetFPSSlider != null)
            {
                targetFPSSlider.value = GetTargetFPSOptionIndex(settings.targetFrameRate);
                UpdateFPSLabel(settings.targetFrameRate);
            }

            // Controls
            if (mouseSensitivitySlider != null)
            {
                mouseSensitivitySlider.value = settings.mouseSensitivity;
                UpdateSensitivityLabel(settings.mouseSensitivity);
            }

            if (invertYToggle != null)
                invertYToggle.isOn = settings.invertY;
        }

        /// <summary>
        /// Add listeners to UI elements
        /// </summary>
        private void AddListeners()
        {
            // Audio sliders
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            }

            if (musicVolumeSlider != null)
            {
                musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
            }

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
            }

            if (voiceVolumeSlider != null)
            {
                voiceVolumeSlider.onValueChanged.AddListener(OnVoiceVolumeChanged);
            }

            if (uiVolumeSlider != null)
            {
                uiVolumeSlider.onValueChanged.AddListener(OnUIVolumeChanged);
            }

            // Graphics
            if (vsyncToggle != null)
            {
                vsyncToggle.onValueChanged.AddListener(OnVSyncChanged);
            }

            if (targetFPSSlider != null)
            {
                targetFPSSlider.onValueChanged.AddListener(OnTargetFPSChanged);
            }

            // Controls
            if (mouseSensitivitySlider != null)
            {
                mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);
            }

            if (invertYToggle != null)
            {
                invertYToggle.onValueChanged.AddListener(OnInvertYChanged);
            }

            // Buttons
            if (resetButton != null)
            {
                resetButton.onClick.AddListener(OnResetClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(OnCloseClicked);
            }
        }

        /// <summary>
        /// Refresh UI to match current settings
        /// </summary>
        public void RefreshUI()
        {
            InitializeUI();
        }

        // UI Event Handlers

        private void OnMasterVolumeChanged(float value)
        {
            SettingsManager.Instance.SetMasterVolume(value);
            UpdateLabel(masterVolumeLabel, value);
        }

        private void OnMusicVolumeChanged(float value)
        {
            SettingsManager.Instance.SetMusicVolume(value);
            UpdateLabel(musicVolumeLabel, value);
        }

        private void OnSFXVolumeChanged(float value)
        {
            SettingsManager.Instance.SetSFXVolume(value);
            UpdateLabel(sfxVolumeLabel, value);
        }

        private void OnVoiceVolumeChanged(float value)
        {
            SettingsManager.Instance.SetVoiceVolume(value);
            UpdateLabel(voiceVolumeLabel, value);
        }

        private void OnUIVolumeChanged(float value)
        {
            SettingsManager.Instance.SetUIVolume(value);
            UpdateLabel(uiVolumeLabel, value);
        }

        private void OnVSyncChanged(bool enabled)
        {
            SettingsManager.Instance.SetVSync(enabled);
        }

        private void OnTargetFPSChanged(float value)
        {
            int optionIndex = Mathf.Clamp(Mathf.RoundToInt(value), 0, TargetFPSOptions.Length - 1);
            int fps = TargetFPSOptions[optionIndex];
            SettingsManager.Instance.SetTargetFrameRate(fps);
            UpdateFPSLabel(fps);
        }

        private void OnMouseSensitivityChanged(float value)
        {
            SettingsManager.Instance.SetMouseSensitivity(value);
            UpdateSensitivityLabel(value);
        }

        private void OnInvertYChanged(bool invert)
        {
            SettingsManager.Instance.SetInvertY(invert);
        }

        private void OnResetClicked()
        {
            SettingsManager.Instance.ResetToDefaults();
            RefreshUI();
        }

        private void OnCloseClicked()
        {
            gameObject.SetActive(false);
        }

        // Label update helpers

        private void UpdateLabel(TextMeshProUGUI label, float value)
        {
            if (label != null)
            {
                label.text = Mathf.RoundToInt(value * 100f).ToString() + "%";
            }
        }

        private void UpdateFPSLabel(int fps)
        {
            if (targetFPSLabel != null)
            {
                targetFPSLabel.text = fps == 0 ? "Unlimited" : fps.ToString();
            }
        }

        private int GetTargetFPSOptionIndex(int fps)
        {
            for (int i = 0; i < TargetFPSOptions.Length; i++)
            {
                if (TargetFPSOptions[i] == fps)
                {
                    return i;
                }
            }

            if (fps <= 30)
                return 0;
            if (fps <= 60)
                return 1;
            if (fps <= 120)
                return 2;

            return 3;
        }

        private void UpdateSensitivityLabel(float value)
        {
            if (mouseSensitivityLabel != null)
            {
                mouseSensitivityLabel.text = value.ToString("F1") + "x";
            }
        }

        private void OnDestroy()
        {
            // Remove listeners to prevent memory leaks
            if (masterVolumeSlider != null)
                masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);

            if (musicVolumeSlider != null)
                musicVolumeSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);

            if (sfxVolumeSlider != null)
                sfxVolumeSlider.onValueChanged.RemoveListener(OnSFXVolumeChanged);

            if (voiceVolumeSlider != null)
                voiceVolumeSlider.onValueChanged.RemoveListener(OnVoiceVolumeChanged);

            if (uiVolumeSlider != null)
                uiVolumeSlider.onValueChanged.RemoveListener(OnUIVolumeChanged);

            if (vsyncToggle != null)
                vsyncToggle.onValueChanged.RemoveListener(OnVSyncChanged);

            if (targetFPSSlider != null)
                targetFPSSlider.onValueChanged.RemoveListener(OnTargetFPSChanged);

            if (mouseSensitivitySlider != null)
                mouseSensitivitySlider.onValueChanged.RemoveListener(OnMouseSensitivityChanged);

            if (invertYToggle != null)
                invertYToggle.onValueChanged.RemoveListener(OnInvertYChanged);

            if (resetButton != null)
                resetButton.onClick.RemoveListener(OnResetClicked);

            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnCloseClicked);
        }
    }
}
