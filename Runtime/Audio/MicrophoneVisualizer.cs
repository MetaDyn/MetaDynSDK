using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;

namespace MetaDyn.Audio
{
    /// <summary>
    /// Manages microphone selection and input level visualization, following the project's
    /// platform-specific architecture. Uses JSLIB for WebGL and standard Unity API for the Editor.
    /// </summary>
    public class MicrophoneVisualizer : MonoBehaviour
    {
        [Header("UI Elements")]
        [Tooltip("Dropdown for selecting microphone device")]
        public TMP_Dropdown microphoneDropdown;

        [Tooltip("List of UI Images to use as level meter bars")]
        public List<Image> levelMeterBars;

        [Header("Meter Colors")]
        public Color greenColor = Color.green;
        public Color orangeColor = Color.yellow;
        public Color redColor = Color.red;

#if UNITY_WEBGL && !UNITY_EDITOR
        // --- WebGL JSLIB Contract ---
        [DllImport("__Internal")]
        private static extern void EnumerateMicrophoneDevices(string gameObjectName, string callbackName);

        [DllImport("__Internal")]
        private static extern void StartLiveMicrophone(string deviceId, string gameObjectName, string callbackName);

        [DllImport("__Internal")]
        private static extern void StopLiveMicrophone();

        // Data structures for parsing JSON from JSLIB
        [System.Serializable]
        private class Device
        {
            public string deviceId;
            public string label;
        }
        [System.Serializable]
        private class DeviceList
        {
            public List<Device> devices;
        }

        private List<Device> _webGLDevices = new List<Device>();

#else
        // --- Editor & Native Platform Implementation ---
        private AudioSource _audioSource;
        private string _currentDeviceName;
        private float[] _samples = new float[1024];
#endif

        private void Awake()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
#endif
        }

        private void Start()
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.OnMicrophoneDeviceChanged += OnMicrophoneDeviceChanged;
            }
            PopulateDropdown();
        }

        private void OnEnable()
        {
            // Re-start listening when the panel becomes active
            string savedDevice = SettingsManager.Instance?.microphoneDevice;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!string.IsNullOrEmpty(savedDevice))
            {
                StartLiveMicrophone(savedDevice, gameObject.name, "OnLiveMicrophoneLevel");
            }
            // If empty or first run, PopulateDropdown will handle it via enumeration callback
#else
            StartListening(savedDevice);
#endif
        }

        private void OnDisable()
        {
            // Stop listening when panel is hidden/disabled
#if UNITY_WEBGL && !UNITY_EDITOR
            StopLiveMicrophone();
#else
            StopListening();
#endif
        }

        private void OnDestroy()
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.OnMicrophoneDeviceChanged -= OnMicrophoneDeviceChanged;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            StopLiveMicrophone();
#else
            StopListening();
#endif
        }

        public void PopulateDropdown()
        {
            if (microphoneDropdown == null) return;
            microphoneDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged); // Clear old listeners
#if UNITY_WEBGL && !UNITY_EDITOR
            EnumerateMicrophoneDevices(gameObject.name, "OnDevicesEnumerated");
#else
            microphoneDropdown.ClearOptions();
            var deviceOptions = new List<string>();
            if (Microphone.devices.Length == 0)
            {
                deviceOptions.Add("No microphones found");
                microphoneDropdown.interactable = false;
            }
            else
            {
                deviceOptions.Add("Default");
                foreach (var device in Microphone.devices) deviceOptions.Add(device);
                microphoneDropdown.interactable = true;
            }
            microphoneDropdown.AddOptions(deviceOptions);
            microphoneDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            SetDropdownToSavedValue(deviceOptions, SettingsManager.Instance?.microphoneDevice);
#endif
        }
        
        private void OnDropdownValueChanged(int index)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (index < 0 || index >= _webGLDevices.Count) return;
            string selectedDeviceId = _webGLDevices[index].deviceId;
#else
            if (index < 0 || index >= microphoneDropdown.options.Count) return;
            string selectedDeviceId = microphoneDropdown.options[index].text;
            if (selectedDeviceId == "Default" || selectedDeviceId == "No microphones found") selectedDeviceId = "";
#endif
            SettingsManager.Instance?.SetMicrophoneDevice(selectedDeviceId);
        }

        private void OnMicrophoneDeviceChanged(string newDeviceId)
        {
            if (!isActiveAndEnabled) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            StartLiveMicrophone(newDeviceId, gameObject.name, "OnLiveMicrophoneLevel");
#else
            StartListening(newDeviceId);
#endif
        }
        
        private void SetDropdownToSavedValue(List<string> options, string savedValue)
        {
            int savedIndex = options.FindIndex(d => d == savedValue);
            microphoneDropdown.value = (savedIndex == -1) ? 0 : savedIndex;
            microphoneDropdown.RefreshShownValue();
            OnMicrophoneDeviceChanged(savedValue);
        }

        private void UpdateMeter(float level)
        {
            if (levelMeterBars == null || levelMeterBars.Count == 0) return;
            int barsToShow = Mathf.Clamp(Mathf.RoundToInt(level * levelMeterBars.Count), 0, levelMeterBars.Count);
            for (int i = 0; i < levelMeterBars.Count; i++)
            {
                if (i < barsToShow)
                {
                    levelMeterBars[i].enabled = true;
                    if (i == levelMeterBars.Count - 1) levelMeterBars[i].color = redColor;
                    else if (i == levelMeterBars.Count - 2) levelMeterBars[i].color = orangeColor;
                    else levelMeterBars[i].color = greenColor;
                }
                else { levelMeterBars[i].enabled = false; }
            }
        }
        
#if UNITY_WEBGL && !UNITY_EDITOR
        // --- WebGL Callbacks ---
        public void OnDevicesEnumerated(string devicesJson)
        {
            var deviceList = JsonUtility.FromJson<DeviceList>(devicesJson);
            _webGLDevices = deviceList.devices;

            microphoneDropdown.ClearOptions();
            var deviceOptions = new List<string>();
            foreach (var device in _webGLDevices)
            {
                deviceOptions.Add(device.label);
            }
            microphoneDropdown.AddOptions(deviceOptions);
            microphoneDropdown.onValueChanged.AddListener(OnDropdownValueChanged);

            string savedDeviceId = SettingsManager.Instance?.microphoneDevice;
            int savedIndex = _webGLDevices.FindIndex(d => d.deviceId == savedDeviceId);
            microphoneDropdown.value = (savedIndex == -1) ? 0 : savedIndex;
            microphoneDropdown.RefreshShownValue();
            
            OnMicrophoneDeviceChanged(microphoneDropdown.value >= 0 ? _webGLDevices[microphoneDropdown.value].deviceId : "");
        }

        public void OnLiveMicrophoneLevel(float level)
        {
            UpdateMeter(level);
        }
#else
        // --- Editor & Native Methods ---
        private void Update()
        {
            if (Microphone.IsRecording(_currentDeviceName))
            {
                UpdateMeter(GetPeakLevel());
            }
        }

        private void StartListening(string deviceName)
        {
            StopListening();
            _currentDeviceName = deviceName;
            string deviceToUse = string.IsNullOrEmpty(deviceName) ? null : deviceName;
            if (Microphone.devices.Length == 0) return;
            _audioSource.clip = Microphone.Start(deviceToUse, true, 1, 44100);
            _audioSource.loop = true;
            StartCoroutine(WaitForMicToStart(deviceToUse));
        }

        private IEnumerator WaitForMicToStart(string deviceInUse)
        {
            while (!(Microphone.GetPosition(deviceInUse) > 0)) yield return null;
            if (_audioSource != null && _audioSource.clip != null) _audioSource.Play();
        }

        private void StopListening()
        {
            if (_audioSource != null && _audioSource.isPlaying) _audioSource.Stop();
            if (!string.IsNullOrEmpty(_currentDeviceName) && Microphone.IsRecording(_currentDeviceName)) Microphone.End(_currentDeviceName);
        }
        
        private float GetPeakLevel()
        {
            if (!_audioSource.isPlaying || _audioSource.clip == null) return 0;
            _audioSource.GetOutputData(_samples, 0);
            float peakLevel = 0;
            foreach (float sample in _samples) peakLevel = Mathf.Max(peakLevel, Mathf.Abs(sample));
            return peakLevel;
        }
#endif
    }
}