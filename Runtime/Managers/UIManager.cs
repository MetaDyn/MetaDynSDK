using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;

namespace MetaDyn
{
    /// <summary>
    /// UI Manager for displaying application info and managing UI interactions
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Application Info Panel")]
        [Tooltip("Panel containing application info text (bottom-left)")]
        public GameObject appInfoPanel;

        [Tooltip("TextMeshPro component for displaying app info")]
        public TextMeshProUGUI appInfoText;

        [Tooltip("Show application info panel")]
        public bool showAppInfo = true;

        [Header("Interactivity")]
        [Tooltip("Sound played on UI interactions (button clicks, slider changes, etc.)")]
        public AudioClip interactionSound;

        [Tooltip("Sound played on confirmations (can be called via Unity events)")]
        public AudioClip confirmationSound;

        [Range(0f, 1f)]
        [Tooltip("Volume for UI sounds (0 = mute, 1 = full volume)")]
        public float uiSoundVolume = 0.5f;

        [Tooltip("Auto-hook interaction sounds to all UI elements in scene")]
        public bool autoHookUIElements = true;

        private StringBuilder _sb;
        private AudioSource _audioSource;

        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            _sb = new StringBuilder(256);
            InitializeAppInfo();
            InitializeAudio();
        }

        private void Start()
        {
            UpdateAppInfo();

            // Auto-hook UI sounds if enabled
            if (autoHookUIElements)
            {
                HookUIElements();
            }

            // Subscribe to settings changes
            if (SettingsManager.Instance != null)
            {
                // Set initial volume
                uiSoundVolume = SettingsManager.Instance.uiVolume;
                UpdateUIVolume();

                // Listen for changes
                SettingsManager.Instance.OnUIVolumeChanged += OnUIVolumeSettingChanged;
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from events to prevent memory leaks
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.OnUIVolumeChanged -= OnUIVolumeSettingChanged;
            }
        }

        private void OnUIVolumeSettingChanged(float newVolume)
        {
            uiSoundVolume = newVolume;
            UpdateUIVolume();
        }

        private void InitializeAppInfo()
        {
            // Auto-find panel if not assigned
            if (appInfoPanel == null)
            {
                appInfoPanel = GameObject.Find("AppInfoPanel");
            }

            // Auto-find text if not assigned
            if (appInfoText == null && appInfoPanel != null)
            {
                appInfoText = appInfoPanel.GetComponentInChildren<TextMeshProUGUI>();
            }

            // Set initial visibility
            if (appInfoPanel != null)
            {
                appInfoPanel.SetActive(showAppInfo);
            }
        }

        /// <summary>
        /// Update application info display
        /// </summary>
        public void UpdateAppInfo()
        {
            if (appInfoText == null) return;

            _sb.Clear();
            _sb.AppendLine($"<b>{Application.companyName}</b>");
            _sb.AppendLine($"{Application.productName} v{Application.version}");
            _sb.AppendLine($"Platform: {Application.platform}");
            _sb.AppendLine($"Unity {Application.unityVersion}");

            appInfoText.text = _sb.ToString();
        }

        /// <summary>
        /// Toggle application info panel visibility
        /// </summary>
        public void ToggleAppInfo()
        {
            showAppInfo = !showAppInfo;
            if (appInfoPanel != null)
            {
                appInfoPanel.SetActive(showAppInfo);
            }
        }

        /// <summary>
        /// Set application info panel visibility
        /// </summary>
        public void SetAppInfoVisible(bool visible)
        {
            showAppInfo = visible;
            if (appInfoPanel != null)
            {
                appInfoPanel.SetActive(showAppInfo);
            }
        }

        // Interactivity Methods

        /// <summary>
        /// Initialize audio source for UI sounds
        /// </summary>
        private void InitializeAudio()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 2D sound
            _audioSource.volume = GetEffectiveVolume();
        }

        /// <summary>
        /// Auto-hook interaction sounds to all UI elements in scene
        /// </summary>
        private void HookUIElements()
        {
            if (interactionSound == null)
            {
                Debug.LogWarning("[UIManager] Interaction sound not assigned. UI sounds will not play.");
                return;
            }

            // Find all buttons and add click sound
            Button[] buttons = FindObjectsOfType<Button>(true);
            foreach (Button button in buttons)
            {
                // Don't add listener if already added (prevent duplicates)
                button.onClick.AddListener(PlayInteractionSound);
            }

            // Find all toggles and add toggle sound
            Toggle[] toggles = FindObjectsOfType<Toggle>(true);
            foreach (Toggle toggle in toggles)
            {
                toggle.onValueChanged.AddListener((value) => PlayInteractionSound());
            }

            // Find all sliders and add slider sound
            Slider[] sliders = FindObjectsOfType<Slider>(true);
            foreach (Slider slider in sliders)
            {
                slider.onValueChanged.AddListener((value) => PlayInteractionSound());
            }

            // Find all dropdowns and add dropdown sound
            TMP_Dropdown[] dropdowns = FindObjectsOfType<TMP_Dropdown>(true);
            foreach (TMP_Dropdown dropdown in dropdowns)
            {
                dropdown.onValueChanged.AddListener((value) => PlayInteractionSound());
            }

            Debug.Log($"[UIManager] Hooked UI sounds to {buttons.Length} buttons, {toggles.Length} toggles, {sliders.Length} sliders, {dropdowns.Length} dropdowns");
        }

        /// <summary>
        /// Play UI interaction sound (automatically called on UI interactions)
        /// </summary>
        public void PlayInteractionSound()
        {
            if (_audioSource == null || interactionSound == null) return;

            _audioSource.volume = GetEffectiveVolume();
            _audioSource.PlayOneShot(interactionSound);
        }

        /// <summary>
        /// Play UI confirmation sound (call from Unity events)
        /// </summary>
        public void PlayConfirmationSound()
        {
            if (_audioSource == null || confirmationSound == null) return;

            _audioSource.volume = GetEffectiveVolume();
            _audioSource.PlayOneShot(confirmationSound);
        }

        /// <summary>
        /// Get effective volume (UI volume * master volume from SettingsManager)
        /// </summary>
        private float GetEffectiveVolume()
        {
            float masterVolume = 1.0f;

            // Get master volume from SettingsManager if available
            if (SettingsManager.Instance != null)
            {
                masterVolume = SettingsManager.Instance.masterVolume;
            }

            return uiSoundVolume * masterVolume;
        }

        /// <summary>
        /// Update UI sound volume (call when settings change)
        /// </summary>
        public void UpdateUIVolume()
        {
            if (_audioSource != null)
            {
                _audioSource.volume = GetEffectiveVolume();
            }
        }
    }
}
