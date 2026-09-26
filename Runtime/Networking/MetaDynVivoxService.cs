using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using UnityEngine;
using System.Text.RegularExpressions;

namespace MetaDyn.Networking
{
    /// <summary>
    /// Manages Vivox text and positional voice integration, linked to UGS Authentication.
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

        private Task _initializationTask;
        private Task _loginTask;
        private Task _channelJoinTask;
        private bool _eventsSubscribed;
        private string _joinedChannelName;

        public bool IsLoggedIn => VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn;
        public bool IsChannelReady =>
            IsLoggedIn &&
            !string.IsNullOrEmpty(_joinedChannelName) &&
            VivoxService.Instance.ActiveChannels.ContainsKey(_joinedChannelName);
        public string JoinedChannelName => _joinedChannelName;

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void OnDestroy()
        {
            if (_instance != this) return;

            UnsubscribeFromVivoxEvents();
            _instance = null;
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
#if UNITY_WEBGL
            // WebGL uses NGO DA for text and the existing WebRTC/HRTF path for spatial
            // voice. Do not enter Vivox 16.10's unstable WebGL channel lifecycle.
            await Task.CompletedTask;
            return;
#else
            if (autoLoginOnStart && AuthenticationService.Instance.IsSignedIn)
            {
                await LoginToVivoxAsync();
            }
#endif
        }

        public Task InitializeAsync()
        {
            if (VivoxService.Instance.InitializationState == VivoxInitializationState.Initialized)
            {
                SubscribeToVivoxEvents();
                return Task.CompletedTask;
            }

            if (_initializationTask == null || _initializationTask.IsCompleted)
                _initializationTask = InitializeCoreAsync();

            return _initializationTask;
        }

        private async Task InitializeCoreAsync()
        {
            Debug.Log("[MetaDyn VOICE] Initializing Vivox.");
            try
            {
                await VivoxService.Instance.InitializeAsync();
                SubscribeToVivoxEvents();
                Debug.Log("[MetaDyn VOICE] Vivox initialized.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Initialization failed: {e}");
                throw;
            }
        }

        public Task LoginToVivoxAsync()
        {
            if (VivoxService.Instance.IsLoggedIn)
                return Task.CompletedTask;

            if (_loginTask == null || _loginTask.IsCompleted)
                _loginTask = LoginCoreAsync();

            return _loginTask;
        }

        private async Task LoginCoreAsync()
        {
            await InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                throw new InvalidOperationException("Vivox login requires an authenticated UGS player.");
            }

            Debug.Log($"[MetaDyn VOICE] Logging in UGS player '{AuthenticationService.Instance.PlayerId}'.");
#if UNITY_WEBGL
            // Vivox WebGL completes its JavaScript lifecycle callbacks on Unity's main
            // thread and its own implementation uses an inline TaskCompletionSource.
            // Forcing asynchronous continuations here can strand the callback on WebGL.
            var loggedInCallback = new TaskCompletionSource<bool>();
#else
            var loggedInCallback = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
#endif
            void HandleLoggedIn()
            {
#if UNITY_WEBGL
                // Leave Vivox's JavaScript login callback stack before channel startup.
                // Starting a channel inline here can race the SDK's own login completion.
                StartCoroutine(CompleteWebGlCallbackNextFrame(loggedInCallback));
#else
                loggedInCallback.TrySetResult(true);
#endif
            }
            VivoxService.Instance.LoggedIn += HandleLoggedIn;

            try
            {
                var loginOptions = new LoginOptions();
#if !UNITY_WEBGL
                loginOptions.DisplayName = PlayerPrefs.GetString("PlayerName", "Player");
                loginOptions.ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.TenPerSecond;
#else
                // Vivox WebGL does not support display names or participant-updated events.
                // Supplying only supported defaults keeps login independent of those native features.
                Debug.Log("[MetaDyn VOICE] Using WebGL-compatible Vivox login options.");
#endif

                Task loginRequest = VivoxService.Instance.LoginAsync(loginOptions);

#if UNITY_WEBGL
                // Vivox 16.10 reaches LoggedIn on WebGL, then its public LoginAsync path calls
                // SetTransmissionModeAsync, which the WebGL backend does not implement. Treat
                // the SDK callback as authoritative while still observing the returned task.
                Task completedLogin = await Task.WhenAny(
                    loginRequest,
                    loggedInCallback.Task,
                    Task.Delay(15000));
                if (completedLogin == loginRequest)
                {
                    try
                    {
                        await loginRequest;
                    }
                    catch (NotImplementedException) when (VivoxService.Instance.IsLoggedIn)
                    {
                        Debug.LogWarning(
                            "[MetaDyn VOICE] Ignoring Vivox WebGL's unsupported automatic " +
                            "channel-transmission-mode step after successful login.");
                    }
                }
                else if (completedLogin != loggedInCallback.Task)
                {
                    throw new TimeoutException(
                        "Vivox WebGL login produced neither task completion nor LoggedIn callback within 15 seconds.");
                }
                else
                {
                    _ = ObserveLateLoginTaskAsync(loginRequest);
                }
#else
                await loginRequest;
#endif

#if UNITY_WEBGL
                // Whether the authoritative callback or the returned task completes first,
                // never start channel creation from inside Vivox's JavaScript login callback.
                await WaitForNextWebGlFrameAsync();
#endif

                if (!VivoxService.Instance.IsLoggedIn)
                    throw new InvalidOperationException("Vivox LoginAsync completed without reaching logged-in state.");

                // Ensure we start muted as requested
                SetMuted(true);

                Debug.Log("[MetaDyn VOICE] Logged in successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Login failed: {e}");
                OnLoginStatusChanged?.Invoke(false);
                throw;
            }
            finally
            {
                VivoxService.Instance.LoggedIn -= HandleLoggedIn;
            }
        }

        private static async Task ObserveLateLoginTaskAsync(Task loginRequest)
        {
            try
            {
                await loginRequest;
            }
            catch (NotImplementedException) when (VivoxService.Instance.IsLoggedIn)
            {
                Debug.LogWarning(
                    "[MetaDyn VOICE] Vivox WebGL login succeeded; its unsupported automatic " +
                    "channel-transmission-mode step was ignored.");
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[MetaDyn VOICE] Vivox LoginAsync faulted after the WebGL LoggedIn callback: {e.Message}");
            }
        }

        public async Task LogoutAsync()
        {
            if (!VivoxService.Instance.IsLoggedIn) return;

            try
            {
                await VivoxService.Instance.LogoutAsync();
                _joinedChannelName = null;
                OnLoginStatusChanged?.Invoke(false);
                Debug.Log("[MetaDyn VOICE] Logged out.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Logout failed: {e}");
                throw;
            }
        }

        public Task JoinChannelAsync(string channelName, ChatCapability chatCapability = ChatCapability.TextAndAudio, bool isPositional = true)
        {
#if UNITY_WEBGL
            return Task.FromException(new PlatformNotSupportedException(
                "MetaDyn WebGL room text uses NGO Distributed Authority and spatial voice uses WebRTC; " +
                "Vivox channel startup is disabled on WebGL."));
#else
            // Vivox channel IDs only allow: letters, numbers, and +=.-_!
            // We strip anything else to ensure the join doesn't fail.
            string sanitizedName = Regex.Replace(channelName, @"[^a-zA-Z0-9+=.\-_!]", "_");

            if (IsChannelReady && string.Equals(_joinedChannelName, sanitizedName, StringComparison.Ordinal))
                return Task.CompletedTask;

            if (_channelJoinTask == null || _channelJoinTask.IsCompleted)
                _channelJoinTask = JoinChannelCoreAsync(sanitizedName, chatCapability, isPositional);

            return _channelJoinTask;
#endif
        }

        private async Task JoinChannelCoreAsync(
            string sanitizedName,
            ChatCapability chatCapability,
            bool isPositional)
        {
            await LoginToVivoxAsync();

#if UNITY_WEBGL
            // Match Vivox WebGL's main-thread callback completion model. The callback is
            // raised only after the SDK has inserted the channel into ActiveChannels.
            var channelJoinedCallback = new TaskCompletionSource<bool>();
#else
            var channelJoinedCallback = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
#endif
            void HandleChannelJoined(string channelName)
            {
                if (string.Equals(channelName, sanitizedName, StringComparison.Ordinal))
                {
#if UNITY_WEBGL
                    StartCoroutine(CompleteWebGlCallbackNextFrame(channelJoinedCallback));
#else
                    channelJoinedCallback.TrySetResult(true);
#endif
                }
            }
            VivoxService.Instance.ChannelJoined += HandleChannelJoined;

            try
            {
                Task channelJoinRequest;
                if (isPositional)
                {
                    var properties = new Channel3DProperties(32, 1, 1.0f, AudioFadeModel.LinearByDistance);
                    channelJoinRequest = VivoxService.Instance.JoinPositionalChannelAsync(
                        sanitizedName,
                        chatCapability,
                        properties);
                }
                else
                {
                    channelJoinRequest = VivoxService.Instance.JoinGroupChannelAsync(
                        sanitizedName,
                        chatCapability);
                }

#if UNITY_WEBGL
                // Vivox WebGL reports authoritative active membership through ChannelJoined.
                // Accept either that event or normal task completion so ChatUI is not stranded
                // when the WebGL channel task fails to resume after the SDK callback.
                Task completedJoin = await Task.WhenAny(
                    channelJoinRequest,
                    channelJoinedCallback.Task,
                    Task.Delay(15000));
                if (completedJoin == channelJoinRequest)
                {
                    try
                    {
                        await channelJoinRequest;
                    }
                    catch (Exception e) when (
                        VivoxService.Instance.ActiveChannels.ContainsKey(sanitizedName))
                    {
                        Debug.LogWarning(
                            $"[MetaDyn VOICE] Vivox WebGL channel '{sanitizedName}' is active; " +
                            $"ignoring its post-join task fault: {e.Message}");
                    }
                }
                else if (completedJoin != channelJoinedCallback.Task)
                {
                    throw new TimeoutException(
                        $"Vivox WebGL channel '{sanitizedName}' produced neither task " +
                        "completion nor ChannelJoined callback within 15 seconds.");
                }
                else
                {
                    _ = ObserveLateChannelJoinTaskAsync(channelJoinRequest, sanitizedName);
                }
#else
                await channelJoinRequest;
#endif

                if (!VivoxService.Instance.ActiveChannels.ContainsKey(sanitizedName))
                    throw new InvalidOperationException(
                        $"Vivox channel join completed without active membership in '{sanitizedName}'.");

                _joinedChannelName = sanitizedName;
                OnLoginStatusChanged?.Invoke(true);
                Debug.Log(
                    $"[MetaDyn VOICE] Joined channel: {sanitizedName} " +
                    $"(Capability: {chatCapability}, Positional: {isPositional})");
            }
            catch (Exception e)
            {
                _joinedChannelName = null;
                OnLoginStatusChanged?.Invoke(false);
                Debug.LogError($"[MetaDyn VOICE] Join channel failed: {e}");
                throw;
            }
            finally
            {
                VivoxService.Instance.ChannelJoined -= HandleChannelJoined;
            }
        }

        private static async Task ObserveLateChannelJoinTaskAsync(
            Task channelJoinRequest,
            string channelName)
        {
            try
            {
                await channelJoinRequest;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[MetaDyn VOICE] Vivox channel '{channelName}' joined through the WebGL " +
                    $"callback, but its returned task later faulted: {e.Message}");
            }
        }

#if UNITY_WEBGL
        private Task WaitForNextWebGlFrameAsync()
        {
            var completion = new TaskCompletionSource<bool>();
            StartCoroutine(CompleteWebGlCallbackNextFrame(completion));
            return completion.Task;
        }

        private static IEnumerator CompleteWebGlCallbackNextFrame(
            TaskCompletionSource<bool> completion)
        {
            yield return null;
            completion.TrySetResult(true);
        }
#endif

        public async Task LeaveChannelAsync(string channelName)
        {
            try
            {
                await VivoxService.Instance.LeaveChannelAsync(channelName);
                if (_joinedChannelName == channelName)
                {
                    _joinedChannelName = null;
                    OnLoginStatusChanged?.Invoke(false);
                }
                Debug.Log($"[MetaDyn VOICE] Left channel: {channelName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Leave channel failed: {e}");
                throw;
            }
        }

        public async Task SendTextMessageAsync(string message)
        {
            if (!IsChannelReady)
                throw new InvalidOperationException("Vivox text send requires a logged-in player in an active channel.");

            try
            {
                await VivoxService.Instance.SendChannelTextMessageAsync(_joinedChannelName, message);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn VOICE] Send message failed: {e}");
                throw;
            }
        }

        public void SetMuted(bool muted)
        {
            if (!VivoxService.Instance.IsLoggedIn) return;
            if (muted) VivoxService.Instance.MuteInputDevice();
            else VivoxService.Instance.UnmuteInputDevice();
            Debug.Log($"[MetaDyn VOICE] Local microphone {(muted ? "muted" : "unmuted")}");
        }

        public void Update3DPosition(GameObject listener, Vector3 position, Vector3 forward, Vector3 up)
{
            if (!IsChannelReady) return;
            VivoxService.Instance.Set3DPosition(listener, _joinedChannelName);
}

        #region Internal Event Handlers

        private void OnVivoxChannelMessageReceived(VivoxMessage message)
        {
            Debug.Log($"[MetaDyn VOICE] [{message.ChannelName}] {message.SenderDisplayName}: {message.MessageText}");

            if (string.Equals(
                    message.SenderPlayerId,
                    AuthenticationService.Instance.PlayerId,
                    StringComparison.Ordinal))
                return;

            string sender = string.IsNullOrWhiteSpace(message.SenderDisplayName)
                ? message.SenderPlayerId
                : message.SenderDisplayName;
            OnTextMessageReceived?.Invoke(sender, message.MessageText);
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

        private void SubscribeToVivoxEvents()
        {
            if (_eventsSubscribed) return;

            VivoxService.Instance.LoggedIn += OnVivoxLoggedIn;
            VivoxService.Instance.LoggedOut += OnVivoxLoggedOut;
            VivoxService.Instance.ChannelJoined += OnVivoxChannelJoined;
            VivoxService.Instance.ChannelLeft += OnVivoxChannelLeft;
            VivoxService.Instance.ChannelMessageReceived += OnVivoxChannelMessageReceived;
            VivoxService.Instance.ParticipantAddedToChannel += OnVivoxParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel += OnVivoxParticipantRemoved;
            _eventsSubscribed = true;
        }

        private void UnsubscribeFromVivoxEvents()
        {
            if (!_eventsSubscribed || VivoxService.Instance == null) return;

            VivoxService.Instance.LoggedIn -= OnVivoxLoggedIn;
            VivoxService.Instance.LoggedOut -= OnVivoxLoggedOut;
            VivoxService.Instance.ChannelJoined -= OnVivoxChannelJoined;
            VivoxService.Instance.ChannelLeft -= OnVivoxChannelLeft;
            VivoxService.Instance.ChannelMessageReceived -= OnVivoxChannelMessageReceived;
            VivoxService.Instance.ParticipantAddedToChannel -= OnVivoxParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel -= OnVivoxParticipantRemoved;
            _eventsSubscribed = false;
        }

        private void OnVivoxLoggedIn()
        {
            Debug.Log("[MetaDyn VOICE] Vivox LoggedIn event received.");
        }

        private void OnVivoxLoggedOut()
        {
            _joinedChannelName = null;
            OnLoginStatusChanged?.Invoke(false);
            Debug.Log("[MetaDyn VOICE] Vivox LoggedOut event received.");
        }

        private void OnVivoxChannelJoined(string channelName)
        {
            Debug.Log($"[MetaDyn VOICE] Vivox ChannelJoined event received for '{channelName}'.");
        }

        private void OnVivoxChannelLeft(string channelName)
        {
            if (string.Equals(_joinedChannelName, channelName, StringComparison.Ordinal))
            {
                _joinedChannelName = null;
                OnLoginStatusChanged?.Invoke(false);
            }

            Debug.Log($"[MetaDyn VOICE] Vivox ChannelLeft event received for '{channelName}'.");
        }

        #endregion
    }
}
