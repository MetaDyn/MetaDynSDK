using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MetaDyn.UserList;
using MetaDyn.Networking.HostMigration;
using MetaDyn.Dashboard;
using MetaDyn.Avatar;
using Unity.Services.Relay.Models;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Services.Vivox;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MetaDyn.Networking
{
    /// <summary>
    /// High-level host-migration state exposed for UI and diagnostics.
    /// </summary>
    public enum MetaDynUGSMigrationState
    {
        Idle,
        Connected,
        MigrationDetected,
        RestartingNetwork,
        MigrationFailed,
        Leaving
    }

    /// <summary>
    /// Creates or joins Unity Gaming Services sessions from MetaDynRuntimeConfig.
    /// Distributed Authority is the production topology; Relay client-host remains a temporary rollback path.
    /// </summary>
    public class MetaDynUGSSessionService : MonoBehaviour
    {
        public const string SpaceIdPropertyKey = "space_id";
        private const string BuildVersionPropertyKey = "build_version";
        private const string ScenePropertyKey = "scene";
        private const string NetworkTopologyPropertyKey = "network_topology";
        private const string SessionScopePropertyKey = "session_scope";
        private const string DistributedAuthorityTopologyValue = "distributed_authority";
        private const string RelayClientHostTopologyValue = "relay_client_host";
        private const string FutureBackendPropertyKey = "future_backend";
        private const string FutureBackendPlaceholder = "custom-websocket-cloudflare-durable-objects";
        private const string DefaultWebGlRelayRegion = "us-central1";
        private const string ReconnectSessionKeyPrefix = "MetaDyn.UGS.ReconnectSession.";
        private const int QuickJoinTimeoutSeconds = 5;
        private const int DaJoinCodeRecoveryDelayMilliseconds = 2000;
        private const int DaJoinCodeMaximumAttempts = 2;
        private const int MigrationTimeoutMilliseconds = 20000;
        private const float LocalStateCaptureIntervalSeconds = 0.25f;

        private static MetaDynUGSSessionService _instance;
        private static bool _isInitializingServices = false;

        public static MetaDynUGSSessionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<MetaDynUGSSessionService>();
                    if (_instance == null)
                    {
                        var serviceObject = new GameObject(nameof(MetaDynUGSSessionService));
                        _instance = serviceObject.AddComponent<MetaDynUGSSessionService>();
                    }
                }

                return _instance;
            }
        }

        public ISession CurrentSession { get; private set; }
        public bool IsConnected => CurrentSession != null;
        public bool IsJoining => _isJoining;
        public bool IsHostMigrationEnabled => _hostMigrationEnabled;
        public bool IsDistributedAuthority => _networkTopology == MetaDynNetworkTopology.DistributedAuthority;
        public MetaDynNetworkTopology NetworkTopology => _networkTopology;
        public MetaDynUGSMigrationState MigrationState { get; private set; } = MetaDynUGSMigrationState.Idle;

        public event Action<ISession> OnSessionJoined;
        public event Action OnSessionLeft;
        public event Action<MetaDynUGSMigrationState> OnMigrationStateChanged;
        public event Action<string> OnHostMigrationFailed;

        private static bool _isJoining;
        private bool _isExplicitlyLeaving;
        private bool _hostMigrationEnabled;
        private MetaDynNetworkTopology _networkTopology = MetaDynNetworkTopology.DistributedAuthority;
        private string _activeReconnectSessionKey;
        private int _migrationGeneration;
        private float _nextLocalStateCaptureTime;
        private double _migrationStartedRealtime = -1d;
        private NetworkManager _spawnCallbackNetworkManager;
        private ISession _boundSession;
        private IClientSessionNetwork _boundSessionNetwork;
        private MetaDynUGSMigrationCoordinator _migrationCoordinator;
        private MetaDynUGSHostMigrationDataHandler _migrationDataHandler;
        private MetaDynUGSMigrationPresentation _migrationPresentation;
        private readonly Dictionary<ulong, int> _clientAvatarChoices = new Dictionary<ulong, int>();

        private void Awake()
{
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            EnsureMigrationHandler();
            _migrationPresentation = new MetaDynUGSMigrationPresentation();
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;

            UnbindSession();
            UnregisterNetworkSpawnCallbacks();
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnFetchLocalPlayerPrefabToSpawn = null;
            _migrationPresentation?.Release();
            _instance = null;
        }

        private void LateUpdate()
        {
            _migrationPresentation?.UpdateFollowTarget();

            if (!_hostMigrationEnabled || MigrationState != MetaDynUGSMigrationState.Connected)
                return;

            if (Time.unscaledTime < _nextLocalStateCaptureTime)
                return;

            _nextLocalStateCaptureTime = Time.unscaledTime + LocalStateCaptureIntervalSeconds;
            CaptureLocalPlayerState();
        }

        public async Task<MetaDynUGSJoinResult> JoinConfiguredWorldAsync(string playerName)
        {
            return await JoinConfiguredWorldAsync(MetaDynRuntimeConfig.Instance, playerName, null);
        }

        public async Task<MetaDynUGSJoinResult> JoinConfiguredWorldAsync(MetaDynRuntimeConfig config, string playerName, string roomNameOverride = null)
        {
            if (_isJoining) return MetaDynUGSJoinResult.Failed("A UGS session join is already in progress.");
            if (config == null) return MetaDynUGSJoinResult.Failed("MetaDynRuntimeConfig is missing.");
            if (!config.IsValid()) return MetaDynUGSJoinResult.Failed("MetaDynRuntimeConfig is invalid.");

            _isJoining = true;
            _isExplicitlyLeaving = false;
            _networkTopology = config.networkTopology;
            // The custom Relay/NGO reconstruction path is rejected. Keep its source isolated
            // during the DA rollout, but never enable it from serialized legacy configuration.
            _hostMigrationEnabled = false;
            EnsureMigrationHandler();
            _migrationCoordinator.Reset();
            _migrationPresentation.Release();
            _migrationStartedRealtime = -1d;
            SetMigrationState(MetaDynUGSMigrationState.Idle);
            Debug.Log($"[MetaDyn UGS] Starting session join flow for user: {playerName}");
            Debug.Log($"[MetaDyn UGS] Requested network topology: {_networkTopology}.");

            NetworkManager networkManager = null;
            Action<ulong> localClientConnectedHandler = null;
            try
            {
                await EnsureAuthenticatedAsync(playerName);

                // 1. Get and Validate NetworkManager
                networkManager = NetworkManager.Singleton;
                if (networkManager == null) return MetaDynUGSJoinResult.Failed("NetworkManager missing in scene.");

                var localClientConnected = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                localClientConnectedHandler = clientId =>
                {
                    if (networkManager != null && clientId == networkManager.LocalClientId)
                    {
                        Debug.Log($"[MetaDyn UGS] NGO local client {clientId} connected.");
                        localClientConnected.TrySetResult(true);
                    }
                };
                networkManager.OnClientConnectedCallback += localClientConnectedHandler;

                // Ensure Persistence
                if (networkManager.gameObject.scene.name != "DontDestroyOnLoad")
                {
                    if (networkManager.transform.parent != null) networkManager.transform.SetParent(null);
                    DontDestroyOnLoad(networkManager.gameObject);
                }

                // 2. Forced Cleanup (Must leave existing sessions via API first)
                if (CurrentSession != null) await LeaveSessionAsync();

                // 3. Programmatic Transport Enforcement (Critical for WebGL/Mobile)
                var transport = networkManager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                if (transport != null)
                {
                    // Force Relay + WebSockets for WebGL compliance
#if UNITY_WEBGL
                    transport.UseWebSockets = true;
#endif
                    Debug.Log($"[MetaDyn UGS] Transport forced to WebSockets: {transport.UseWebSockets}");
                }

                string deterministicName = Regex.Replace(config.spaceId, @"[^a-zA-Z0-9\-]", "");
                string sessionScope = BuildSessionScope(
                    deterministicName,
                    roomNameOverride,
                    GetTopologyPropertyValue());

                // UGS starts NGO automatically during create/join, so topology and player
                // spawning must be configured before either operation begins.
                ConfigureNetworkTopology(networkManager);
                EnsureUserListManagerExists();

                // Quick Join treats the MetaDyn space as indexed matchmaking metadata.
                // UGS joins an active matching session or creates a fresh ephemeral session.
                Debug.Log(
                    $"[MetaDyn UGS] Quick joining or creating a fresh {_networkTopology} session " +
                    $"for scope '{sessionScope}' and space '{config.spaceId}'.");
                CurrentSession = await MatchmakeConfiguredSessionAsync(
                    config,
                    deterministicName,
                    sessionScope);

                // Unity's DA reference requires the local NGO connection callback before
                // gameplay or player-spawn readiness is accepted. IsListening alone only
                // proves that the transport started.
                if (networkManager.IsConnectedClient)
                    localClientConnected.TrySetResult(true);

                Task completedConnectionTask = await Task.WhenAny(
                    localClientConnected.Task,
                    Task.Delay(10000));
                if (completedConnectionTask != localClientConnected.Task)
                    throw new Exception("UGS session completed but the NGO local-client connection callback did not arrive.");

                await localClientConnected.Task;

                if (networkManager.IsConnectedClient)
                {
                    LogNetworkManagerState();
                    if (IsDistributedAuthority)
                    {
                        Debug.Log(
                            $"[UGS DA] Connected. topology={networkManager.NetworkConfig.NetworkTopology}, " +
                            $"distributedAuthority={networkManager.DistributedAuthorityMode}, " +
                            $"sessionOwner={networkManager.LocalClient.IsSessionOwner}, " +
                            $"localClientId={networkManager.LocalClientId}.");
                    }
                }

#if UNITY_WEBGL
                Debug.Log(
                    "[UGS CHAT] WebGL text chat uses the connected NGO Distributed " +
                    "Authority player; Vivox channel startup is disabled on WebGL.");
#else
                _ = InitializeAndJoinVivoxAsync(deterministicName);
#endif
                SetMigrationState(MetaDynUGSMigrationState.Connected);
                OnSessionJoined?.Invoke(CurrentSession);
                return MetaDynUGSJoinResult.Succeeded(CurrentSession);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MetaDyn UGS] Fatal Join Error: {ex}");
                return MetaDynUGSJoinResult.Failed(ex.Message);
            }
            finally
            {
                if (networkManager != null && localClientConnectedHandler != null)
                    networkManager.OnClientConnectedCallback -= localClientConnectedHandler;
                _isJoining = false;
            }
        }

        public async Task LeaveSessionAsync()
        {
            if (CurrentSession == null)
                return;

            var sessionToLeave = CurrentSession;
            _isExplicitlyLeaving = true;
            _migrationGeneration++;
            SetMigrationState(MetaDynUGSMigrationState.Leaving);
            UnbindSession();

            try
            {
                // Native clients leave their Vivox channel. WebGL never starts Vivox;
                // its text and spatial-voice transports have independent lifecycles.
#if !UNITY_WEBGL
                if (MetaDynVivoxService.Instance != null)
                {
                    _ = MetaDynVivoxService.Instance.LogoutAsync();
                }
#endif

                await sessionToLeave.LeaveAsync();
                
                // Also shutdown NGO if still active
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MetaDyn UGS] Error while leaving session: {ex.Message}");
            }
            finally
            {
                ClearPersistedReconnectSession();
                CurrentSession = null;
                _clientAvatarChoices.Clear();
                UnregisterNetworkSpawnCallbacks();
                if (NetworkManager.Singleton != null)
                    NetworkManager.Singleton.OnFetchLocalPlayerPrefabToSpawn = null;
                _migrationPresentation.Release();
                _isExplicitlyLeaving = false;
                SetMigrationState(MetaDynUGSMigrationState.Idle);
                OnSessionLeft?.Invoke();
            }
        }

#if !UNITY_WEBGL
        private async Task InitializeAndJoinVivoxAsync(string roomName)
        {
            try
            {
                var vivox = MetaDynVivoxService.Instance;
                if (vivox == null) return;

                await vivox.InitializeAsync();
                await vivox.LoginToVivoxAsync();
                
                // Native clients retain Vivox positional voice and text in one channel.
                await vivox.JoinChannelAsync(roomName + "_voice");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MetaDyn UGS] Vivox auto-join failed: {ex.Message}");
            }
        }
#endif

        private static async Task EnsureAuthenticatedAsync(string playerName)
        {
            // Prevention of concurrent initialization
            while (_isInitializingServices) await Task.Delay(100);

            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                _isInitializingServices = true;
                try
                {
                    var options = new InitializationOptions();
                    string profile = SanitizeProfileName(playerName);
                    if (!string.IsNullOrEmpty(profile))
                    {
                        options.SetProfile(profile);
                    }

                    await UnityServices.InitializeAsync(options);
                }
                finally
                {
                    _isInitializingServices = false;
                }
            }
            else
            {
                // Wait for initialization to complete if it's currently in progress
                float initTimeout = 10.0f;
                while (UnityServices.State == ServicesInitializationState.Initializing && initTimeout > 0)
                {
                    await Task.Delay(100);
                    initTimeout -= 0.1f;
                }
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        private async Task<ISession> MatchmakeConfiguredSessionAsync(
            MetaDynRuntimeConfig config,
            string sessionName,
            string sessionScope)
        {
            string relayRegion = ResolveRelayRegion(config.relayRegion);
            _activeReconnectSessionKey = BuildReconnectSessionKey(sessionScope);

            SessionOptions CreateOptions()
            {
                var options = new SessionOptions
                {
                    Name = sessionName,
                    MaxPlayers = Mathf.Max(1, config.maxPlayers),
                    IsPrivate = false,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        { SpaceIdPropertyKey, new SessionProperty(config.spaceId, VisibilityPropertyOptions.Public, PropertyIndex.String1) },
                        { BuildVersionPropertyKey, new SessionProperty(config.buildVersion ?? Application.version) },
                        { ScenePropertyKey, new SessionProperty(SceneManager.GetActiveScene().name) },
                        { NetworkTopologyPropertyKey, new SessionProperty(GetTopologyPropertyValue(), VisibilityPropertyOptions.Public, PropertyIndex.String2) },
                        { SessionScopePropertyKey, new SessionProperty(sessionScope, VisibilityPropertyOptions.Public, PropertyIndex.String3) },
                        { FutureBackendPropertyKey, new SessionProperty(FutureBackendPlaceholder) }
                    }
                };

                options = IsDistributedAuthority
                    ? options.WithDistributedAuthorityNetwork(relayRegion)
                    : options.WithRelayNetwork(relayRegion);
                return options.WithNetworkOptions(CreateNetworkOptions());
            }

            string reconnectSessionId = PlayerPrefs.GetString(_activeReconnectSessionKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(reconnectSessionId))
            {
                Debug.Log(
                    $"[MetaDyn UGS] Reconnecting UGS player to prior session " +
                    $"'{reconnectSessionId}' for scope '{sessionScope}'.");

                try
                {
                    var reconnectedSession = await MultiplayerService.Instance
                        .ReconnectToSessionAsync(reconnectSessionId);
                    BindSession(reconnectedSession);

                    // A returning browser can still be recorded as Lobby host even though its
                    // old NGO process no longer exists. MPS reconnects Lobby successfully but,
                    // for a host without new network options, intentionally does not join the
                    // surviving DA network. Hand Lobby ownership to an existing peer so MPS
                    // consumes the existing DA network metadata as a client.
                    if (reconnectedSession.IsHost &&
                        (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient))
                    {
                        string survivingPlayerId = FindOtherSessionPlayerId(reconnectedSession);
                        if (!string.IsNullOrWhiteSpace(survivingPlayerId))
                        {
                            var hostSession = reconnectedSession.AsHost();
                            hostSession.Host = survivingPlayerId;
                            await hostSession.SavePropertiesAsync();
                            Debug.Log(
                                $"[MetaDyn UGS] Transferred stale returning Lobby-host role to " +
                                $"surviving UGS player '{survivingPlayerId}' before DA network rejoin.");
                        }
                        else
                        {
                            // No peer owns a surviving DA network. Leave this obsolete membership
                            // and let the standard Quick Join create-or-find path run below.
                            UnbindSession();
                            await reconnectedSession.LeaveAsync();
                            ClearPersistedReconnectSession();
                            _activeReconnectSessionKey = BuildReconnectSessionKey(sessionScope);
                            Debug.Log(
                                $"[MetaDyn UGS] Prior session '{reconnectSessionId}' has no " +
                                "surviving peer; continuing with Quick Join.");
                            reconnectedSession = null;
                        }
                    }

                    if (reconnectedSession != null)
                    {
                        PersistReconnectSessionId(reconnectedSession.Id);
                        Debug.Log(
                            $"[MetaDyn UGS] Reconnected to prior session " +
                            $"'{reconnectedSession.Id}' for scope '{sessionScope}'.");
                        return reconnectedSession;
                    }
                }
                catch (Exception reconnectException)
                {
                    // Do not create a second room while Lobby still records this player in
                    // the surviving session. Query membership only after reconnect fails.
                    List<string> joinedSessionIds = await MultiplayerService.Instance
                        .GetJoinedSessionIdsAsync();
                    bool remainsJoined = joinedSessionIds.Exists(
                        id => string.Equals(id, reconnectSessionId, StringComparison.Ordinal));
                    if (remainsJoined)
                    {
                        throw new InvalidOperationException(
                            $"Reconnect to surviving session '{reconnectSessionId}' failed, " +
                            "but UGS still records this player as a member. Refusing to create " +
                            "a duplicate room.",
                            reconnectException);
                    }

                    Debug.LogWarning(
                        $"[MetaDyn UGS] Prior session '{reconnectSessionId}' is no longer " +
                        $"joined by this player; continuing with Quick Join for '{sessionScope}'.");
                    ClearPersistedReconnectSession();
                    _activeReconnectSessionKey = BuildReconnectSessionKey(sessionScope);
                }
            }

            for (int attempt = 1; attempt <= DaJoinCodeMaximumAttempts; attempt++)
            {
                var quickJoinOptions = new QuickJoinOptions
                {
                    Filters = new List<FilterOption>
                    {
                        new FilterOption(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual),
                        new FilterOption(FilterField.StringIndex1, config.spaceId, FilterOperation.Equal),
                        new FilterOption(FilterField.StringIndex2, GetTopologyPropertyValue(), FilterOperation.Equal),
                        new FilterOption(FilterField.StringIndex3, sessionScope, FilterOperation.Equal)
                    },
                    Timeout = TimeSpan.FromSeconds(QuickJoinTimeoutSeconds),
                    CreateSession = true
                };

                Debug.Log(
                    $"[MetaDyn UGS] Starting Unity Quick Join for scope '{sessionScope}' " +
                    $"with a {QuickJoinTimeoutSeconds}-second create-if-none timeout, " +
                    $"explicit Relay region '{relayRegion}', and attempt " +
                    $"{attempt}/{DaJoinCodeMaximumAttempts}.");

                try
                {
                    var session = await MultiplayerService.Instance.MatchmakeSessionAsync(
                        quickJoinOptions,
                        CreateOptions());
                    BindSession(session);
                    PersistReconnectSessionId(session.Id);
                    return session;
                }
                catch (Exception exception) when (
                    attempt < DaJoinCodeMaximumAttempts &&
                    IsDistributedAuthority &&
                    !IsNetworkListening() &&
                    IsDaJoinCodePropagationFailure(exception))
                {
                    // Installed MPS 2.3.0 awaits CleanupSessionAsync before rethrowing this
                    // exact DA initialization failure. Wait beyond Lobby's one-request-per-
                    // second player limit, then perform one fresh Quick Join operation with
                    // fresh options. Never retry auth, 429-only, spawn, reconnect, or arbitrary
                    // session failures, and never retry after NGO begins listening.
                    Debug.LogWarning(
                        "[MetaDyn UGS] Unity DA did not publish a Relay join code before " +
                        $"session initialization ended (attempt {attempt}/{DaJoinCodeMaximumAttempts}). " +
                        $"MPS cleanup completed; retrying once in " +
                        $"{DaJoinCodeRecoveryDelayMilliseconds} ms.");
                    await Task.Delay(DaJoinCodeRecoveryDelayMilliseconds);
                }
            }

            throw new InvalidOperationException(
                "Unity Quick Join exhausted its bounded DA join-code recovery without returning a session.");
        }

        private static bool IsNetworkListening()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }

        private static bool IsDaJoinCodePropagationFailure(Exception exception)
        {
            if (!(exception is SessionException sessionException) ||
                sessionException.Error != SessionError.Unknown ||
                !string.Equals(
                    sessionException.Message,
                    "Failed to create and join session",
                    StringComparison.Ordinal))
            {
                return false;
            }

            const string distributedAuthorityExceptionType =
                "Unity.Services.DistributedAuthority.Exceptions.DistributedAuthorityServiceException";
            const string missingJoinCodeMessage = "No join code returned from lobby.";

            Exception cause = sessionException.InnerException;
            while (cause != null)
            {
                if (string.Equals(
                        cause.GetType().FullName,
                        distributedAuthorityExceptionType,
                        StringComparison.Ordinal) &&
                    string.Equals(cause.Message, missingJoinCodeMessage, StringComparison.Ordinal))
                {
                    return true;
                }

                cause = cause.InnerException;
            }

            return false;
        }

        private static string BuildReconnectSessionKey(string sessionScope)
        {
            return ReconnectSessionKeyPrefix + sessionScope;
        }

        private static string FindOtherSessionPlayerId(ISession session)
        {
            if (session?.Players == null)
                return null;

            string localPlayerId = session.CurrentPlayer?.Id;
            foreach (var player in session.Players)
            {
                if (player != null &&
                    !string.IsNullOrWhiteSpace(player.Id) &&
                    !string.Equals(player.Id, localPlayerId, StringComparison.Ordinal))
                    return player.Id;
            }

            return null;
        }

        private void PersistReconnectSessionId(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(_activeReconnectSessionKey) ||
                string.IsNullOrWhiteSpace(sessionId))
                return;

            PlayerPrefs.SetString(_activeReconnectSessionKey, sessionId);
            PlayerPrefs.Save();
        }

        private void ClearPersistedReconnectSession()
        {
            if (string.IsNullOrWhiteSpace(_activeReconnectSessionKey))
                return;

            PlayerPrefs.DeleteKey(_activeReconnectSessionKey);
            PlayerPrefs.Save();
            _activeReconnectSessionKey = null;
        }

        private static string BuildSessionScope(
            string sanitizedSpaceId,
            string roomNameOverride,
            string topologyValue)
        {
            string topologySuffix = string.Equals(
                topologyValue,
                DistributedAuthorityTopologyValue,
                StringComparison.Ordinal)
                ? "da"
                : "relay";
            string sessionId = $"{sanitizedSpaceId}-{topologySuffix}";

            if (string.IsNullOrWhiteSpace(roomNameOverride))
                return sessionId;

            string overrideToken = Regex.Replace(
                roomNameOverride,
                @"[^a-zA-Z0-9\-_]",
                "-").Trim('-');
            int availableLength = 64 - sessionId.Length - 1;
            if (availableLength <= 0 || string.IsNullOrEmpty(overrideToken))
                return sessionId;

            if (overrideToken.Length > availableLength)
                overrideToken = overrideToken.Substring(0, availableLength);

            return $"{sessionId}-{overrideToken}";
        }

        private static NetworkOptions CreateNetworkOptions()
        {
            // WebGL requires WebSocket Secure (WSS) to communicate with Relay.
            // Native platforms (Editor, Android, iOS) should use UDP for performance.
            var protocol = RelayProtocol.UDP;
            
#if UNITY_WEBGL
            // Force WSS for all WebGL environments (including Editor WebGL target)
            protocol = RelayProtocol.WSS;
#endif

            return new NetworkOptions
            {
                RelayProtocol = protocol
            };
        }

        private static string ResolveRelayRegion(string configuredRegion)
        {
            string region = configuredRegion?.Trim();
#if UNITY_WEBGL
            if (string.IsNullOrEmpty(region))
                region = DefaultWebGlRelayRegion;
#endif
            return region;
        }

        private void BindSession(ISession session)
        {
            if (session == null) return;
            if (ReferenceEquals(_boundSession, session)) return;

            UnbindSession();

            _boundSession = session;
            _boundSession.SessionPropertiesChanged += OnSessionPropertiesChanged;
            _boundSession.StateChanged += OnSessionStateChanged;
            _boundSession.SessionHostChanged += OnSessionHostChanged;
            _boundSession.SessionMigrated += OnSessionMigrated;

            _boundSessionNetwork = session.Network;
            if (_boundSessionNetwork != null)
            {
                _boundSessionNetwork.StateChanged += OnNetworkStateChanged;
                _boundSessionNetwork.MigrationFailed += OnNetworkMigrationFailed;
            }
        }

        private void UnbindSession()
        {
            if (_boundSessionNetwork != null)
            {
                _boundSessionNetwork.StateChanged -= OnNetworkStateChanged;
                _boundSessionNetwork.MigrationFailed -= OnNetworkMigrationFailed;
                _boundSessionNetwork = null;
            }

            if (_boundSession != null)
            {
                _boundSession.SessionPropertiesChanged -= OnSessionPropertiesChanged;
                _boundSession.StateChanged -= OnSessionStateChanged;
                _boundSession.SessionHostChanged -= OnSessionHostChanged;
                _boundSession.SessionMigrated -= OnSessionMigrated;
                _boundSession = null;
            }
        }

        private void ConfigureNetworkTopology(NetworkManager networkManager)
        {
            if (networkManager == null)
                return;

            if (IsDistributedAuthority)
            {
                // NGO Distributed Authority players are selected and spawned by their owning
                // client. A gameplay server connection-approval/spawn loop does not exist.
                UnregisterNetworkSpawnCallbacks();
                _clientAvatarChoices.Clear();
                networkManager.ConnectionApprovalCallback = null;
                networkManager.NetworkConfig.ConnectionApproval = false;
                networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();
                networkManager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = true;
                networkManager.OnFetchLocalPlayerPrefabToSpawn = FetchLocalPlayerPrefabToSpawn;
                Debug.Log("[UGS DA] Configured client-side player prefab selection and disabled manual server spawning.");
                return;
            }

            // Temporary pre-DA rollback path. The rejected WithHostMigration reconstruction
            // remains disabled even when this client-host topology is selected.
            networkManager.OnFetchLocalPlayerPrefabToSpawn = null;
            ConfigureManualPlayerSpawning();
            RegisterNetworkSpawnCallbacks();
            PrepareConnectionPayload();
        }

        private GameObject FetchLocalPlayerPrefabToSpawn()
        {
            int choice = PlayerPrefs.GetInt("AvatarChoice", 0);
            GameObject playerPrefab = null;

            if (MetaDynUGSAvatarRegistry.Instance != null)
            {
                NetworkObject registeredPrefab = MetaDynUGSAvatarRegistry.Instance.GetPrefabByIndex(choice);
                if (registeredPrefab != null)
                    playerPrefab = registeredPrefab.gameObject;
            }

            if (playerPrefab == null && NetworkManager.Singleton?.NetworkConfig != null)
                playerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;

            if (playerPrefab == null)
            {
                Debug.LogError($"[UGS DA] Cannot select local player prefab for avatar choice {choice}: registry and NetworkManager fallback are empty.");
                return null;
            }

            LogPlayerPrefabSetup(playerPrefab);
            Debug.Log($"[UGS DA] Selected locally owned player prefab '{playerPrefab.name}' for avatar choice {choice}.");
            return playerPrefab;
        }

        /// <summary>
        /// Places a newly auto-spawned DA-owned local player at an existing MetaDyn entrance.
        /// Remaining players never call this when another participant leaves because their
        /// NetworkObjects are not reconstructed in Distributed Authority.
        /// </summary>
        public void ApplyInitialDistributedAuthoritySpawnPose(MetaDynUGSPlayerController player)
        {
            if (!IsDistributedAuthority || player == null || !player.IsOwner)
                return;

            GetSpawnPose(out Vector3 position, out Quaternion rotation);

            CharacterController characterController = player.GetComponent<CharacterController>();
            bool restoreCharacterController = characterController != null && characterController.enabled;
            if (restoreCharacterController)
                characterController.enabled = false;

            player.transform.SetPositionAndRotation(position, rotation);

            if (restoreCharacterController)
                characterController.enabled = true;

            Debug.Log($"[UGS DA] Positioned locally owned player '{player.name}' at initial entrance pose {position}.");
        }

        private string GetTopologyPropertyValue()
        {
            return IsDistributedAuthority
                ? DistributedAuthorityTopologyValue
                : RelayClientHostTopologyValue;
        }

        private void ConfigureManualPlayerSpawning()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogWarning("[MetaDyn UGS] Manual player spawning not configured: NetworkManager.Singleton is null.");
                return;
            }

            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.ConnectionApprovalCallback = (NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response) =>
            {
                response.Approved = true;
                response.CreatePlayerObject = false;
                response.Pending = false;

                // Store connection data for spawning
                if (request.Payload != null && request.Payload.Length >= 4)
                {
                    int avatarIndex = BitConverter.ToInt32(request.Payload, 0);
                    _clientAvatarChoices[request.ClientNetworkId] = avatarIndex;
                    Debug.Log($"[MetaDyn UGS] Received avatar choice {avatarIndex} for client {request.ClientNetworkId}.");
                }
                else
                {
                    _clientAvatarChoices[request.ClientNetworkId] = 0;
                }
            };
        }

        private static void PrepareConnectionPayload()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            int choice = PlayerPrefs.GetInt("AvatarChoice", 0);
            networkManager.NetworkConfig.ConnectionData = BitConverter.GetBytes(choice);
            Debug.Log($"[MetaDyn UGS] Prepared connection payload with avatar choice {choice}.");
        }

        private void OnSessionPropertiesChanged()
        {
            if (_boundSession != null)
            {
                Debug.Log($"[MetaDyn UGS] Session properties changed for '{_boundSession.Name}'.");
            }
        }

        private void OnSessionStateChanged(SessionState state)
        {
            if (IsDistributedAuthority)
                Debug.Log($"[UGS DA] Session state changed to {state}.");
            else
                LogMigration($"Session state changed to {state}.");
        }

        private void OnSessionHostChanged(string newHostPlayerId)
        {
            if (IsDistributedAuthority)
            {
                if (!_isExplicitlyLeaving)
                    Debug.Log($"[UGS DA] UGS session owner changed to '{newHostPlayerId}' without starting MetaDyn migration recovery.");
                return;
            }

            if (!_hostMigrationEnabled || _isExplicitlyLeaving) return;

            BeginMigration($"UGS elected replacement host '{newHostPlayerId}'.");
        }

        private void OnNetworkStateChanged(NetworkState state)
        {
            if (IsDistributedAuthority)
            {
                if (!_isExplicitlyLeaving)
                    Debug.Log($"[UGS DA] Network state changed to {state}.");
                return;
            }

            if (!_hostMigrationEnabled || _isExplicitlyLeaving) return;

            LogMigration($"Network state changed to {state}.");

            if (state == NetworkState.Migrating)
            {
                BeginMigration("UGS is restarting Relay and NGO.");
                SetMigrationState(MetaDynUGSMigrationState.RestartingNetwork);
            }
        }

        private void OnSessionMigrated()
        {
            if (!_hostMigrationEnabled || _isExplicitlyLeaving) return;

            _migrationGeneration++;
            ConfigureManualPlayerSpawning();
            RegisterNetworkSpawnCallbacks();
            EnsurePlayerObjectsSpawned();
            SetMigrationState(MetaDynUGSMigrationState.Connected);

            LogMigration(
                $"Session migration completed. " +
                $"host={CurrentSession?.IsHost}, lifecycleSnapshot={_migrationCoordinator.HasPendingSnapshot}.");
        }

        private void OnNetworkMigrationFailed(SessionError error)
        {
            if (!_hostMigrationEnabled || _isExplicitlyLeaving) return;

            HandleMigrationFailure($"UGS network migration failed: {error}.");
        }

        private void BeginMigration(string reason)
        {
            bool alreadyMigrating = MigrationState == MetaDynUGSMigrationState.MigrationDetected ||
                                    MigrationState == MetaDynUGSMigrationState.RestartingNetwork;

            if (!alreadyMigrating)
                _migrationStartedRealtime = Time.realtimeSinceStartupAsDouble;

            LogMigration(reason);
            if (alreadyMigrating) return;

            CaptureLocalPlayerState();
            _clientAvatarChoices.Clear();
            int generation = ++_migrationGeneration;
            SetMigrationState(MetaDynUGSMigrationState.MigrationDetected);
            _ = MonitorMigrationTimeoutAsync(generation);
        }

        private async Task MonitorMigrationTimeoutAsync(int generation)
        {
            await Task.Delay(MigrationTimeoutMilliseconds);

            if (generation != _migrationGeneration || _isExplicitlyLeaving) return;
            if (MigrationState != MetaDynUGSMigrationState.MigrationDetected &&
                MigrationState != MetaDynUGSMigrationState.RestartingNetwork) return;

            HandleMigrationFailure($"Host migration did not complete within {MigrationTimeoutMilliseconds / 1000} seconds.");
        }

        private void HandleMigrationFailure(string message)
        {
            _migrationGeneration++;
            SetMigrationState(MetaDynUGSMigrationState.MigrationFailed);
            Debug.LogError($"[UGS MIGRATION] {message}");
            OnHostMigrationFailed?.Invoke(message);
        }

        private void SetMigrationState(MetaDynUGSMigrationState state)
        {
            if (MigrationState == state) return;

            MigrationState = state;
            LogMigration($"MetaDyn state changed to {state}.");
            OnMigrationStateChanged?.Invoke(state);
        }

        private void EnsureMigrationHandler()
        {
            if (_migrationCoordinator == null)
            {
                _migrationCoordinator = new MetaDynUGSMigrationCoordinator();
            }

            if (_migrationDataHandler == null)
            {
                _migrationDataHandler = new MetaDynUGSHostMigrationDataHandler(_migrationCoordinator);
            }
        }

        private void OnNetworkPreShutdown()
        {
            if (!_hostMigrationEnabled || _isExplicitlyLeaving || CurrentSession == null)
                return;

            BeginMigration("NGO pre-shutdown detected for host migration.");
            SetMigrationState(MetaDynUGSMigrationState.RestartingNetwork);

            MetaDynUGSPlayerController localPlayer = FindLocalPlayer();
            if (localPlayer == null)
            {
                Debug.LogWarning("[UGS MIGRATION] Could not preserve local presentation before NGO shutdown: local player missing.");
                return;
            }

            CaptureLocalPlayerState(localPlayer);
            _migrationPresentation.Capture(localPlayer);
        }

        private void CaptureLocalPlayerState()
        {
            CaptureLocalPlayerState(FindLocalPlayer());
        }

        private void CaptureLocalPlayerState(MetaDynUGSPlayerController localPlayer)
        {
            if (localPlayer == null || !localPlayer.IsOwner) return;

            string ugsPlayerId = AuthenticationService.Instance.IsSignedIn
                ? AuthenticationService.Instance.PlayerId
                : string.Empty;
            string supabaseUserId = string.Empty;
            string activeAvatarModelId = localPlayer.ActiveAvatarModelId;

            SupabaseAuthManager authManager = SupabaseAuthManager.Instance;
            if (authManager != null && authManager.IsAuthenticated && authManager.CurrentSession?.user != null)
            {
                supabaseUserId = authManager.CurrentSession.user.id ?? string.Empty;
                if (string.IsNullOrWhiteSpace(activeAvatarModelId))
                    activeAvatarModelId = authManager.CurrentSession.user.profile?.active_avatar_model_id ?? string.Empty;
            }

            byte permissionLevel = 0;
            bool isMuted = true;
            MetaDynUGSUserListManager userList = MetaDynUGSUserListManager.Instance;
            if (userList != null && userList.TryGetUser(localPlayer.OwnerClientId, out MetaDynUGSUserData userData))
            {
                permissionLevel = userData.PermissionLevel;
                isMuted = userData.IsMuted;
                if (string.IsNullOrWhiteSpace(supabaseUserId))
                    supabaseUserId = userData.UserId.ToString();
            }

            _migrationCoordinator.CaptureLocalPlayerState(new MetaDynUGSMigrationCoordinator.LocalPlayerState
            {
                UgsPlayerId = ugsPlayerId,
                SupabaseUserId = supabaseUserId,
                DisplayName = string.IsNullOrWhiteSpace(localPlayer.DisplayName)
                    ? PlayerPrefs.GetString("PlayerName", "Guest")
                    : localPlayer.DisplayName,
                ActiveAvatarModelId = activeAvatarModelId ?? string.Empty,
                AvatarIndex = PlayerPrefs.GetInt("AvatarChoice", 0),
                Position = localPlayer.transform.position,
                Rotation = localPlayer.transform.rotation,
                PermissionLevel = permissionLevel,
                IsMuted = isMuted,
                CapturedUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        private static MetaDynUGSPlayerController FindLocalPlayer()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager?.SpawnManager == null) return null;

            NetworkObject localPlayerObject = networkManager.SpawnManager.GetLocalPlayerObject();
            return localPlayerObject != null
                ? localPlayerObject.GetComponent<MetaDynUGSPlayerController>()
                : null;
        }

        public void NotifyLocalAvatarPresentationReady(
            MetaDynUGSPlayerController player,
            string avatarModelId,
            bool succeeded)
        {
            if (player == null || !player.IsOwner || !_migrationPresentation.IsActive)
                return;

            player.SetMigrationPresentationHidden(false);
            _migrationPresentation.Release();

            if (succeeded)
                LogMigration($"Replacement custom avatar '{avatarModelId}' is ready; released preserved presentation.");
            else
                Debug.LogWarning($"[UGS MIGRATION] [{MigrationElapsedLabel}] Replacement custom avatar '{avatarModelId}' failed to load; released preserved presentation.");
        }

        private void RegisterNetworkSpawnCallbacks()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || _spawnCallbackNetworkManager == networkManager)
            {
                if (networkManager == null)
                    Debug.LogWarning("[MetaDyn UGS] Cannot register spawn callbacks: NetworkManager.Singleton is null.");
                return;
            }

            UnregisterNetworkSpawnCallbacks();
            _spawnCallbackNetworkManager = networkManager;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnPreShutdown += OnNetworkPreShutdown;
        }

        private void UnregisterNetworkSpawnCallbacks()
        {
            if (_spawnCallbackNetworkManager != null)
            {
                _spawnCallbackNetworkManager.OnClientConnectedCallback -= OnClientConnected;
                _spawnCallbackNetworkManager.OnPreShutdown -= OnNetworkPreShutdown;
                _spawnCallbackNetworkManager = null;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[MetaDyn SPAWN] Client connected: {clientId}. Checking player object.");
            EnsurePlayerObjectSpawned(clientId);
        }

        public void EnsurePlayerObjectsSpawned()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer) return;

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                EnsurePlayerObjectSpawned(clientId);
            }
        }

        private void EnsureUserListManagerExists()
        {
            if (MetaDynUGSUserListManager.Instance != null) return;

            var userListObject = new GameObject(nameof(MetaDynUGSUserListManager));
            userListObject.AddComponent<MetaDynUGSUserListManager>();
            Debug.Log("[MetaDyn UGS] UserList initialized.");
        }

        private void EnsurePlayerObjectSpawned(ulong clientId)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer) return;

            if (!networkManager.ConnectedClients.TryGetValue(clientId, out var client)) return;

            if (client.PlayerObject != null) return;

            // Get the prefab from the registry based on client choice
            GameObject playerPrefab = null;
            int choice = 0;

            if (clientId == networkManager.LocalClientId)
            {
                choice = PlayerPrefs.GetInt("AvatarChoice", 0);
            }
            else if (_clientAvatarChoices.TryGetValue(clientId, out int c))
            {
                choice = c;
            }
            
            if (MetaDynUGSAvatarRegistry.Instance != null)
            {
                var no = MetaDynUGSAvatarRegistry.Instance.GetPrefabByIndex(choice);
                if (no != null) playerPrefab = no.gameObject;
            }

            // Fallback to NetworkManager default if registry fails
            if (playerPrefab == null)
            {
                playerPrefab = networkManager.NetworkConfig != null ? networkManager.NetworkConfig.PlayerPrefab : null;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("[MetaDyn SPAWN] Cannot spawn player: No Player Prefab found in registry or NetworkManager.");
                return;
            }

            LogPlayerPrefabSetup(playerPrefab);

            MetaDynUGSMigrationCoordinator.LocalPlayerState localState = null;
            bool restoreLocalPlayer = clientId == networkManager.LocalClientId &&
                                      _hostMigrationEnabled &&
                                      _migrationCoordinator.TryGetLocalPlayerState(out localState);

            Vector3 spawnPosition;
            Quaternion spawnRotation;
            if (restoreLocalPlayer)
            {
                spawnPosition = localState.Position;
                spawnRotation = localState.Rotation;
                choice = localState.AvatarIndex;
                LogMigration($"Restoring local player spawn pose at {spawnPosition}.");
            }
            else
            {
                GetSpawnPose(out spawnPosition, out spawnRotation);
            }

            Debug.Log($"[MetaDyn SPAWN] Spawning player for client {clientId}: prefab='{playerPrefab.name}', choiceIndex={choice}.");

            var playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);
            var networkObject = playerInstance.GetComponent<NetworkObject>();
            networkObject.SpawnAsPlayerObject(clientId, destroyWithScene: false);

            if (restoreLocalPlayer)
            {
                RestoreLocalPlayerAfterMigration(playerInstance, clientId, localState);
            }

            Debug.Log($"[MetaDyn SPAWN] Spawned player '{playerInstance.name}' for client {clientId}.");
        }

        private void RestoreLocalPlayerAfterMigration(
            GameObject playerInstance,
            ulong clientId,
            MetaDynUGSMigrationCoordinator.LocalPlayerState state)
        {
            if (playerInstance == null || state == null) return;

            MetaDynUGSPlayerController player = playerInstance.GetComponent<MetaDynUGSPlayerController>();
            if (player == null) return;

            MetaDynUGSUserListManager.Instance?.RestorePlayer(
                clientId,
                state.DisplayName,
                state.SupabaseUserId,
                state.PermissionLevel,
                state.IsMuted);

            if (_migrationPresentation.IsActive)
            {
                _migrationPresentation.AttachTo(player.transform);

                bool waitForCustomAvatar = _migrationPresentation.HasVisuals &&
                                           !string.IsNullOrWhiteSpace(state.ActiveAvatarModelId);
                if (waitForCustomAvatar)
                {
                    player.SetMigrationPresentationHidden(true);
                    LogMigration(
                        $"Replacement player controls are active; holding only its renderers behind the preserved avatar " +
                        $"until custom model '{state.ActiveAvatarModelId}' is restored.");
                }
                else
                {
                    _migrationPresentation.Release();
                    LogMigration("Replacement preset avatar is ready; released preserved presentation.");
                }
            }

            if (!string.IsNullOrWhiteSpace(state.ActiveAvatarModelId))
            {
                RestoreCustomAvatarAfterMigration(player, state.ActiveAvatarModelId);
            }

            LogMigration(
                $"Restored local player continuity for UGS player '{state.UgsPlayerId}' " +
                $"at {state.Position}.");
        }

        private void RestoreCustomAvatarAfterMigration(
            MetaDynUGSPlayerController player,
            string avatarModelId)
        {
            GLBAvatarLoader loader = GLBAvatarLoader.Instance != null
                ? GLBAvatarLoader.Instance
                : FindFirstObjectByType<GLBAvatarLoader>();

            if (loader == null)
            {
                Debug.LogWarning($"[UGS MIGRATION] Cannot restore custom avatar '{avatarModelId}': GLBAvatarLoader missing.");
                NotifyLocalAvatarPresentationReady(player, avatarModelId, false);
                return;
            }

            LogMigration($"Restoring custom avatar '{avatarModelId}' on the replacement player.");
            loader.LoadAndApplyStoredAvatar(avatarModelId, player);
        }

        private string MigrationElapsedLabel
        {
            get
            {
                if (_migrationStartedRealtime < 0d)
                    return "+?.???s";

                double elapsed = Time.realtimeSinceStartupAsDouble - _migrationStartedRealtime;
                return $"+{elapsed:0.000}s";
            }
        }

        private void LogMigration(string message)
        {
            Debug.Log($"[UGS MIGRATION] [{MigrationElapsedLabel}] {message}");
        }

        private static void LogPlayerPrefabSetup(GameObject playerPrefab)
        {
            bool hasController = playerPrefab.TryGetComponent<MetaDynUGSPlayerController>(out _);
            bool hasCharacterController = playerPrefab.TryGetComponent<CharacterController>(out _);
            bool hasNetworkTransform = playerPrefab.TryGetComponent<NetworkTransform>(out var networkTransform);

            Debug.Log(
                $"[MetaDyn SPAWN] Player prefab setup: prefab='{playerPrefab.name}', " +
                $"Controller={hasController}, " +
                $"CharacterController={hasCharacterController}, " +
                $"NetworkTransform={hasNetworkTransform}" +
                (hasNetworkTransform ? $", Authority={networkTransform.AuthorityMode}" : string.Empty) +
                ".");

            if (!hasController)
                Debug.LogWarning($"[MetaDyn SPAWN] Player prefab '{playerPrefab.name}' has no MetaDynUGSPlayerController.");

            if (!hasCharacterController)
                Debug.LogWarning($"[MetaDyn SPAWN] Player prefab '{playerPrefab.name}' has no CharacterController.");

            if (!hasNetworkTransform)
                Debug.LogWarning($"[MetaDyn SPAWN] Player prefab '{playerPrefab.name}' has no NetworkTransform.");
        }

        private static void GetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            var spawnPoints = FindObjectsByType<EntrancePoint>(FindObjectsSortMode.None);
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                var spawnPoint = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];
                position = spawnPoint.transform.position;
                rotation = spawnPoint.transform.rotation;
                Debug.Log($"[MetaDyn SPAWN] Selected entrance '{spawnPoint.name}' at {position}.");
                return;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            Debug.LogWarning("[MetaDyn SPAWN] No active entrance found; spawning at world origin.");
        }

        private static void LogNetworkManagerState()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogWarning("[MetaDyn UGS] NetworkManager.Singleton is null.");
                return;
            }

            var playerPrefab = networkManager.NetworkConfig != null ? networkManager.NetworkConfig.PlayerPrefab : null;
            var localPlayerObject = networkManager.SpawnManager != null ? networkManager.SpawnManager.GetLocalPlayerObject() : null;

            Debug.Log(
                "[MetaDyn UGS] NGO state: " +
                $"listening={networkManager.IsListening}, " +
                $"host={networkManager.IsHost}, " +
                $"client={networkManager.IsClient}, " +
                $"localClientId={networkManager.LocalClientId}, " +
                $"prefab={(playerPrefab != null ? playerPrefab.name : "none")}.");
        }

        private static string SanitizeSessionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "MetaDynRoom";
            // Match Vivox sanitation: letters, numbers, and +=.-_!
            return Regex.Replace(name, @"[^a-zA-Z0-9+=.\-_!]", "_");
        }

        private static string SanitizeProfileName(string playerName)
{
            if (string.IsNullOrWhiteSpace(playerName))
                return "MetaDynPlayer";

            string sanitized = Regex.Replace(playerName, @"[^\w-]", string.Empty);
            return string.IsNullOrEmpty(sanitized) ? "MetaDynPlayer" : sanitized;
        }
    }

    public readonly struct MetaDynUGSJoinResult
    {
        public bool Ok { get; }
        public string Error { get; }
        public ISession Session { get; }

        private MetaDynUGSJoinResult(bool ok, ISession session, string error)
        {
            Ok = ok;
            Session = session;
            Error = error;
        }

        public static MetaDynUGSJoinResult Succeeded(ISession session)
        {
            return new MetaDynUGSJoinResult(true, session, null);
        }

        public static MetaDynUGSJoinResult Failed(string error)
        {
            return new MetaDynUGSJoinResult(false, null, error);
        }
    }
}
