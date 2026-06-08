using System;
using System.Collections.Generic;
using MetaDyn.Networking;
using Unity.Collections;
using UnityEngine;

namespace MetaDyn.UserList
{
    public sealed class MetaDynUGSUserListManager : MonoBehaviour
    {
        private static MetaDynUGSUserListManager _instance;
        public static MetaDynUGSUserListManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<MetaDynUGSUserListManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject(nameof(MetaDynUGSUserListManager));
                        _instance = go.AddComponent<MetaDynUGSUserListManager>();
                        Debug.Log("[MetaDynUGSUserListManager] Created lazy-load instance.");
                    }
                }
                return _instance;
            }
        }

        [SerializeField] private bool firstPlayerIsAdmin = true;
        [SerializeField] private bool startPlayersMuted = true;

        public event Action<ulong, MetaDynUGSUserData> OnUserJoined;
        public event Action<ulong> OnUserLeft;
        public event Action<ulong, MetaDynUGSUserData> OnUserDataChanged;

        private readonly Dictionary<ulong, MetaDynUGSUserData> _users = new();
        private readonly HashSet<ulong> _localBlockedUsers = new();
        private float _nextPlayerScanTime;

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

        private void Update()
        {
            if (Time.time < _nextPlayerScanTime)
                return;

            _nextPlayerScanTime = Time.time + 1f;
            RegisterExistingPlayers();
        }

        public void RegisterExistingPlayers()
        {
            var players = FindObjectsByType<MetaDynUGSPlayerController>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                player.RefreshUserListRegistration();
            }
        }

        public void RegisterPlayer(ulong clientId, string playerName, string userId)
        {
            playerName = SanitizeName(playerName);
            userId ??= string.Empty;

            byte permissionLevel = ResolvePermission(clientId, userId);
            bool isMuted = _users.TryGetValue(clientId, out var existing) ? existing.IsMuted : startPlayersMuted;

            var userData = new MetaDynUGSUserData
            {
                ClientId = clientId,
                PlayerName = new FixedString32Bytes(playerName),
                UserId = new FixedString64Bytes(userId),
                IsMuted = isMuted,
                PermissionLevel = permissionLevel
            };

            if (_users.TryGetValue(clientId, out existing))
            {
                if (existing.Equals(userData))
                    return;

                _users[clientId] = userData;
                Debug.Log($"[UGS UserList] Updated player: {playerName} (ClientId: {clientId}).");
                OnUserDataChanged?.Invoke(clientId, userData);
            }
            else
            {
                _users.Add(clientId, userData);
                Debug.Log($"[UGS UserList] Registered player: {playerName} (ClientId: {clientId}).");
                
                // NEW: Ensure local player starts muted if configured
                if (clientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId && MetaDyn.Networking.MetaDynVivoxService.Instance != null)
                {
                    MetaDyn.Networking.MetaDynVivoxService.Instance.SetMuted(userData.IsMuted);
                }

                OnUserJoined?.Invoke(clientId, userData);
            }
        }

        public void UnregisterPlayer(ulong clientId)
        {
            if (!_users.Remove(clientId))
                return;

            Debug.Log($"[UGS UserList] Unregistered player: {clientId}.");
            OnUserLeft?.Invoke(clientId);
        }

        public bool TryGetUser(ulong clientId, out MetaDynUGSUserData userData)
        {
            return _users.TryGetValue(clientId, out userData);
        }

        public List<MetaDynUGSUserData> GetAllUsers()
        {
            return new List<MetaDynUGSUserData>(_users.Values);
        }

        public bool IsUserBlockedLocally(ulong clientId)
        {
            return _localBlockedUsers.Contains(clientId);
        }

        public void BlockUser(ulong clientId)
        {
            if (_localBlockedUsers.Add(clientId) && TryGetUser(clientId, out var userData))
                OnUserDataChanged?.Invoke(clientId, userData);
        }

        public void UnblockUser(ulong clientId)
        {
            if (_localBlockedUsers.Remove(clientId) && TryGetUser(clientId, out var userData))
                OnUserDataChanged?.Invoke(clientId, userData);
        }

        public void SetPlayerMuted(ulong targetClientId, bool muted, ulong requestingClientId)
{
            if (!_users.TryGetValue(targetClientId, out var userData))
                return;

            bool authorized = requestingClientId == targetClientId
                || (_users.TryGetValue(requestingClientId, out var requestingUser) && requestingUser.IsAdmin);

            if (!authorized)
            {
                Debug.LogWarning($"[UGS UserList] Client {requestingClientId} is not authorized to change mute state for {targetClientId}.");
                return;
            }

            userData.IsMuted = muted;
            _users[targetClientId] = userData;
            Debug.Log($"[UGS UserList] Player {userData.PlayerName} {(muted ? "muted" : "unmuted")} by {requestingClientId}.");

            // Sync with Vivox if it's the local player
            if (targetClientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId && MetaDynVivoxService.Instance != null)
            {
                MetaDynVivoxService.Instance.SetMuted(muted);
            }

            OnUserDataChanged?.Invoke(targetClientId, userData);
}

        private byte ResolvePermission(ulong clientId, string userId)
        {
            string ownerId = MetaDynRuntimeConfig.Instance != null ? MetaDynRuntimeConfig.Instance.ownerId : string.Empty;
            if (!string.IsNullOrEmpty(ownerId) && !string.IsNullOrEmpty(userId) && userId == ownerId)
            {
                return 2;
            }

            return firstPlayerIsAdmin && _users.Count == 0 ? (byte)2 : (byte)0;
        }

        private static string SanitizeName(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return "Guest " + UnityEngine.Random.Range(100, 999);

            return playerName.Length > 31 ? playerName.Substring(0, 31) : playerName;
        }
    }
}
