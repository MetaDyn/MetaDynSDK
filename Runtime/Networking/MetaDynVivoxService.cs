using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MetaDyn.Networking
{
    /// <summary>
    /// Manages Vivox voice and text chat integration, linked to UGS Authentication.
    /// Handles channel management (Positional for voice, Non-Positional for text).
    /// </summary>
    public class MetaDynVivoxService : MonoBehaviour
    {
        private static MetaDynVivoxService _instance;
        private static bool _isQuitting = false;

        public static MetaDynVivoxService Instance
        {
            get
            {
                if (_isQuitting) return null;

                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<MetaDynVivoxService>();
                    if (_instance == null)
                    {
                        var go = new GameObject(nameof(MetaDynVivoxService));
                        _instance = go.AddComponent<MetaDynVivoxService>();
                    }
                }
                return _instance;
            }
        }

        [Header("Settings")]
        [SerializeField] private bool autoLoginOnStart = false;

        [Header("Events")]
        public Action<bool> OnLoginStatusChanged;
        public Action<string, string> OnTextMessageReceived; // sender, message
        public Action<string> OnParticipantAdded;
        public Action<string> OnParticipantRemoved;

        private bool _isLoggedIn = false;
        private string _joinedChannelName;

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private async void Start()
        {
            if (autoLoginOnStart && AuthenticationService.Instance.IsSignedIn)
            {
                await LoginToVivoxAsync();
            }
        }

        public async Task InitializeAsync()
        {
            try
            {
                await VivoxService.Instance.InitializeAsync();
                Debug.Log("[MetaDyn VOICE] Vivox initialized.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Initialization failed: {e.Message}");
            }
        }

        public async Task LoginToVivoxAsync()
        {
            if (_isLoggedIn) return;

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.LogError("[MetaDyn VOICE] Cannot login: UGS Authentication not signed in.");
                return;
            }

            try
            {
                var loginOptions = new LoginOptions
                {
                    DisplayName = PlayerPrefs.GetString("PlayerName", "Player"),
                    ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.TenPerSecond
                };

                await VivoxService.Instance.LoginAsync(loginOptions);
                _isLoggedIn = true;
                
                // Ensure we start muted as requested
                SetMuted(true);

                OnLoginStatusChanged?.Invoke(true);
                Debug.Log("[MetaDyn VOICE] Logged in successfully.");

                // Subscribe to events
                VivoxService.Instance.ChannelMessageReceived += OnVivoxChannelMessageReceived;
                VivoxService.Instance.ParticipantAddedToChannel += OnVivoxParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel += OnVivoxParticipantRemoved;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Login failed: {e.Message}");
                OnLoginStatusChanged?.Invoke(false);
            }
        }

        public async Task LogoutAsync()
        {
            if (!_isLoggedIn) return;

            try
            {
                await VivoxService.Instance.LogoutAsync();
                _isLoggedIn = false;
                _joinedChannelName = null;
                OnLoginStatusChanged?.Invoke(false);
                Debug.Log("[MetaDyn VOICE] Logged out.");
                
                VivoxService.Instance.ChannelMessageReceived -= OnVivoxChannelMessageReceived;
                VivoxService.Instance.ParticipantAddedToChannel -= OnVivoxParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel -= OnVivoxParticipantRemoved;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Logout failed: {e.Message}");
            }
        }

        public async Task JoinChannelAsync(string channelName, ChatCapability chatCapability = ChatCapability.TextAndAudio, bool isPositional = true)
        {
            if (!_isLoggedIn)
            {
                await LoginToVivoxAsync();
            }

            // Vivox channel IDs only allow: letters, numbers, and +=.-_!
            // We strip anything else to ensure the join doesn't fail.
            string sanitizedName = Regex.Replace(channelName, @"[^a-zA-Z0-9+=.\-_!]", "_");

            try
            {
                if (isPositional)
                {
                    var properties = new Channel3DProperties(32, 1, 1.0f, AudioFadeModel.LinearByDistance);
                    await VivoxService.Instance.JoinPositionalChannelAsync(sanitizedName, chatCapability, properties);
                }
                else
                {
                    await VivoxService.Instance.JoinGroupChannelAsync(sanitizedName, chatCapability);
                }

                _joinedChannelName = sanitizedName;
                Debug.Log($"[MetaDyn VOICE] Joined channel: {sanitizedName} (Positional: {isPositional})");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Join channel failed: {e.Message}");
            }
        }

        public async Task LeaveChannelAsync(string channelName)
        {
            try
            {
                await VivoxService.Instance.LeaveChannelAsync(channelName);
                if (_joinedChannelName == channelName) _joinedChannelName = null;
                Debug.Log($"[MetaDyn VOICE] Left channel: {channelName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Leave channel failed: {e.Message}");
            }
        }

        public async Task SendTextMessageAsync(string message)
        {
            if (!_isLoggedIn || string.IsNullOrEmpty(_joinedChannelName)) return;

            try
            {
                await VivoxService.Instance.SendChannelTextMessageAsync(_joinedChannelName, message);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Send message failed: {e.Message}");
            }
        }

        public void SetMuted(bool muted)
        {
            if (!_isLoggedIn) return;
            if (muted) VivoxService.Instance.MuteInputDevice();
            else VivoxService.Instance.UnmuteInputDevice();
            Debug.Log($"[MetaDyn VOICE] Local microphone {(muted ? "muted" : "unmuted")}");
        }

        public void Update3DPosition(GameObject listener, Vector3 position, Vector3 forward, Vector3 up)
{
            if (!_isLoggedIn || string.IsNullOrEmpty(_joinedChannelName)) return;
            VivoxService.Instance.Set3DPosition(listener, _joinedChannelName);
        }

        #region Internal Event Handlers

        private void OnVivoxChannelMessageReceived(VivoxMessage message)
        {
            Debug.Log($"[MetaDyn VOICE] [{message.ChannelName}] {message.SenderDisplayName}: {message.MessageText}");
            OnTextMessageReceived?.Invoke(message.SenderDisplayName, message.MessageText);
        }

        private void OnVivoxParticipantAdded(VivoxParticipant participant)
        {
            Debug.Log($"[MetaDyn VOICE] Participant added: {participant.DisplayName}");
            OnParticipantAdded?.Invoke(participant.DisplayName);
        }

        private void OnVivoxParticipantRemoved(VivoxParticipant participant)
        {
            Debug.Log($"[MetaDyn VOICE] Participant removed: {participant.DisplayName}");
            OnParticipantRemoved?.Invoke(participant.DisplayName);
        }

        #endregion
    }
}
