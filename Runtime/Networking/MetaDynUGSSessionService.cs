using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MetaDyn.UserList;
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
    /// Creates or joins Unity Gaming Services sessions from MetaDynRuntimeConfig.
    /// Stage 1 only proves configured UGS room connectivity; player/avatar spawning is handled in a later migration layer.
    /// </summary>
    public class MetaDynUGSSessionService : MonoBehaviour
    {
        public const string SpaceIdPropertyKey = "space_id";
        private const string BuildVersionPropertyKey = "build_version";
        private const string ScenePropertyKey = "scene";
        private const string OwnerIdPropertyKey = "owner_id";
        private const string FutureBackendPropertyKey = "future_backend";
        private const string FutureBackendPlaceholder = "custom-websocket-cloudflare-durable-objects";

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

        public event Action<ISession> OnSessionJoined;
        public event Action OnSessionLeft;

        private bool _isJoining;
        private NetworkManager _spawnCallbackNetworkManager;
        private readonly Dictionary<ulong, int> _clientAvatarChoices = new Dictionary<ulong, int>();

        private void Awake()
{
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
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
            Debug.Log($"[MetaDyn UGS] Starting session join flow for user: {playerName}");

            try
            {
                await EnsureAuthenticatedAsync(playerName);

                // 1. Get and Validate NetworkManager
                var networkManager = NetworkManager.Singleton;
                if (networkManager == null) return MetaDynUGSJoinResult.Failed("NetworkManager missing in scene.");

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

                // 4. Consistent Handshake State (ConnectionApproval = TRUE)
                // We MUST set this BEFORE calling Find/Join because UGS starts NGO automatically.
                networkManager.NetworkConfig.ConnectionApproval = true;

                string deterministicName = Regex.Replace(config.spaceId, @"[^a-zA-Z0-9\-]", "");
                
                RegisterNetworkSpawnCallbacks();
                PrepareConnectionPayload();
                EnsureUserListManagerExists();

                // 5. Try to Find and Join (As Client)
                Debug.Log($"[MetaDyn UGS] Searching for space: {config.spaceId}");
                CurrentSession = await FindExistingSessionWithTimeoutAsync(config.spaceId, deterministicName);

                if (CurrentSession != null)
                {
                    Debug.Log($"[MetaDyn UGS] Joining as Client: {CurrentSession.Id}");
                }
                else
                {
                    // 6. Become Host
                    // Switch to Host config BEFORE calling Create
                    networkManager.NetworkConfig.ConnectionApproval = true;
                    ConfigureManualPlayerSpawning();
                    
                    Debug.Log($"[MetaDyn UGS] Creating as Host: {deterministicName}");
                    try 
                    {
                        CurrentSession = await CreateConfiguredSessionWithTimeoutAsync(config, deterministicName);
                    }
                    catch (Exception createEx)
                    {
                        Debug.LogWarning($"[MetaDyn UGS] Join race condition or timeout: {createEx.Message}. Retrying as client...");
                        networkManager.NetworkConfig.ConnectionApproval = true;
                        await Task.Delay(1000); 
                        CurrentSession = await FindExistingSessionWithTimeoutAsync(config.spaceId, deterministicName);
                        if (CurrentSession == null) throw new Exception("Failed to create or join session after timeout/retry.");
                    }
                }

                // 7. Wait for Listening state
                float startupTimeout = 10.0f; // Increased for WebGL networking variance
                while (networkManager != null && !networkManager.IsListening && startupTimeout > 0)
                {
                    await Task.Delay(100);
                    startupTimeout -= 0.1f;
                }

                if (networkManager != null && networkManager.IsListening)
                {
                    // Automatic spawning removed to allow UI-controlled timing.
                }

                _ = InitializeAndJoinVivoxAsync(deterministicName);
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
                _isJoining = false;
            }
        }

        private async Task<ISession> FindExistingSessionWithTimeoutAsync(string spaceId, string sessionName)
        {
            var task = FindExistingSessionAsync(spaceId, sessionName);
            if (await Task.WhenAny(task, Task.Delay(15000)) == task) return await task;
            throw new Exception("UGS Session Search timed out.");
        }

        private async Task<ISession> CreateConfiguredSessionWithTimeoutAsync(MetaDynRuntimeConfig config, string roomNameOverride)
        {
            var task = CreateConfiguredSessionAsync(config, roomNameOverride);
            if (await Task.WhenAny(task, Task.Delay(15000)) == task) return await task;
            throw new Exception("UGS Session Creation timed out.");
        }

        public async Task LeaveSessionAsync()
        {
            if (CurrentSession == null)
                return;

            try
            {
                // Leave Vivox channels
                if (MetaDynVivoxService.Instance != null)
                {
                    _ = MetaDynVivoxService.Instance.LogoutAsync();
                }

                CurrentSession.SessionPropertiesChanged -= OnSessionPropertiesChanged;
                await CurrentSession.LeaveAsync();
                
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
                CurrentSession = null;
                UnregisterNetworkSpawnCallbacks();
                OnSessionLeft?.Invoke();
            }
        }

        private async Task InitializeAndJoinVivoxAsync(string roomName)
        {
            try
            {
                var vivox = MetaDynVivoxService.Instance;
                if (vivox == null) return;

                await vivox.InitializeAsync();
                await vivox.LoginToVivoxAsync();
                
                // Join Positional voice channel
                await vivox.JoinChannelAsync(roomName + "_voice", ChatCapability.TextAndAudio, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MetaDyn UGS] Vivox auto-join failed: {ex.Message}");
            }
        }

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

        private async Task<ISession> FindExistingSessionAsync(string spaceId, string sessionName)
        {
            var queryOptions = new QuerySessionsOptions();
            var results = await MultiplayerService.Instance.QuerySessionsAsync(queryOptions);

            if (results == null || results.Sessions == null)
                return null;

            foreach (var session in results.Sessions)
            {
                if (session == null || session.Properties == null)
                    continue;

                if (!string.Equals(session.Name, sessionName, StringComparison.Ordinal))
                    continue;

                if (!session.Properties.TryGetValue(SpaceIdPropertyKey, out var property))
                    continue;

                if (!string.Equals(property.Value?.ToString(), spaceId, StringComparison.Ordinal))
                    continue;

                if (session.AvailableSlots <= 0)
                {
                    Debug.LogWarning($"[MetaDyn UGS] Matching session '{session.Name}' is full.");
                    continue;
                }

                try
                {
                    Debug.Log($"[MetaDyn UGS] Joining existing session '{session.Name}' ({session.Id}) for space '{spaceId}'.");
                    var joinedSession = await MultiplayerService.Instance.JoinSessionByIdAsync(session.Id, CreateJoinOptions());
                    BindSession(joinedSession);
                    return joinedSession;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MetaDyn UGS] Failed to join existing session '{session.Name}': {ex.Message}. This may be a zombie session. Searching for others...");
                    continue;
                }
            }

            return null;
        }

        private async Task<ISession> CreateConfiguredSessionAsync(MetaDynRuntimeConfig config, string roomNameOverride)
        {
            string sessionName = string.IsNullOrWhiteSpace(roomNameOverride) ? config.roomName : roomNameOverride;

            var options = new SessionOptions
            {
                Name = sessionName,
                MaxPlayers = Mathf.Max(1, config.maxPlayers),
                IsPrivate = false,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { SpaceIdPropertyKey, new SessionProperty(config.spaceId) },
                    { OwnerIdPropertyKey, new SessionProperty(config.ownerId ?? string.Empty) },
                    { BuildVersionPropertyKey, new SessionProperty(config.buildVersion ?? Application.version) },
                    { ScenePropertyKey, new SessionProperty(SceneManager.GetActiveScene().name) },
                    { FutureBackendPropertyKey, new SessionProperty(FutureBackendPlaceholder) }
                }
            }.WithRelayNetwork()
             .WithNetworkOptions(CreateNetworkOptions());

            Debug.Log($"[MetaDyn UGS] Creating session '{sessionName}' for space '{config.spaceId}'.");
            var session = await MultiplayerService.Instance.CreateSessionAsync(options);
            BindSession(session);
            return session;
        }

        private static JoinSessionOptions CreateJoinOptions()
        {
            return new JoinSessionOptions()
                .WithNetworkOptions(CreateNetworkOptions());
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

        private void BindSession(ISession session)
        {
            if (session != null)
            {
                session.SessionPropertiesChanged += OnSessionPropertiesChanged;
            }
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
            if (CurrentSession != null)
            {
                Debug.Log($"[MetaDyn UGS] Session properties changed for '{CurrentSession.Name}'.");
            }
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
        }

        private void UnregisterNetworkSpawnCallbacks()
        {
            if (_spawnCallbackNetworkManager != null)
            {
                _spawnCallbackNetworkManager.OnClientConnectedCallback -= OnClientConnected;
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

            GetSpawnPose(out var spawnPosition, out var spawnRotation);

            Debug.Log($"[MetaDyn SPAWN] Spawning player for client {clientId}: prefab='{playerPrefab.name}', choiceIndex={choice}.");

            var playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);
            var networkObject = playerInstance.GetComponent<NetworkObject>();
            networkObject.SpawnAsPlayerObject(clientId, destroyWithScene: false);

            Debug.Log($"[MetaDyn SPAWN] Spawned player '{playerInstance.name}' for client {clientId}.");
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
