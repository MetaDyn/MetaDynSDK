using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MetaDyn.UserList
{
    public class UserListUI : MonoBehaviour
    {
        [Header("UI Settings")]
        [SerializeField] private bool startVisible = true;
        [SerializeField] private bool showLocalPlayerInList = true;
        [SerializeField] private KeyCode toggleListKey = KeyCode.Tab;

        [Header("UI References")]
        [SerializeField] private GameObject userListPanel;
        [SerializeField] private Transform userListContainer;
        [SerializeField] private GameObject userListEntryPrefab;
        [SerializeField] private TMP_Text headerText;

        [Header("Layout Settings")]
        [SerializeField] private float entryHeight = 60f;
        [SerializeField] private float entrySpacing = 5f;

        private readonly Dictionary<ulong, UserListEntry> _activeEntries = new();
private readonly Queue<GameObject> _entryPool = new();
        private const int InitialPoolSize = 10;
        private bool _isSubscribed;

        private void Start()
        {
            if (!ValidateReferences())
                return;

            InitializePool();
            EnsureLayoutGroup();
            userListPanel.SetActive(startVisible);
            TrySubscribe();
        }

        private void Update()
        {
            TrySubscribe();

            if (Input.GetKeyDown(toggleListKey))
                ToggleVisibility();
        }

        private void OnDestroy()
        {
            if (MetaDynUGSUserListManager.Instance != null)
            {
                MetaDynUGSUserListManager.Instance.OnUserJoined -= OnUserJoined;
                MetaDynUGSUserListManager.Instance.OnUserLeft -= OnUserLeft;
                MetaDynUGSUserListManager.Instance.OnUserDataChanged -= OnUserDataChanged;
            }
        }

        public void ToggleVisibility()
        {
            if (userListPanel == null)
                return;

            bool visible = !userListPanel.activeSelf;
            userListPanel.SetActive(visible);

            if (visible)
                RefreshAllEntries();
        }

        public void Show()
        {
            if (userListPanel == null)
                return;

            userListPanel.SetActive(true);
            RefreshAllEntries();
        }

        public void Hide()
        {
            if (userListPanel != null)
                userListPanel.SetActive(false);
        }

        private bool ValidateReferences()
{
            if (userListPanel == null || userListContainer == null || userListEntryPrefab == null)
            {
                Debug.LogError("[UserListUI] Required UI references are missing.");
                enabled = false;
                return false;
            }

            return true;
        }

        private void TrySubscribe()
        {
            if (_isSubscribed)
                return;

            var manager = MetaDynUGSUserListManager.Instance;
            if (manager == null)
                return;

            manager.OnUserJoined += OnUserJoined;
            manager.OnUserLeft += OnUserLeft;
            manager.OnUserDataChanged += OnUserDataChanged;

            _isSubscribed = true;
            Debug.Log("[UserListUI] Subscribed to UGS user list manager.");
            RefreshAllEntries();
        }

        private void InitializePool()
        {
            for (int i = 0; i < InitialPoolSize; i++)
            {
                var entry = Instantiate(userListEntryPrefab);
                entry.SetActive(false);
                _entryPool.Enqueue(entry);
            }
        }

        private void EnsureLayoutGroup()
        {
            if (userListContainer.GetComponent<VerticalLayoutGroup>() != null)
                return;

            var layoutGroup = userListContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            layoutGroup.spacing = entrySpacing;
            layoutGroup.childAlignment = TextAnchor.UpperCenter;
            layoutGroup.childControlHeight = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = true;
        }

        private GameObject GetPooledEntry()
        {
            if (_entryPool.Count > 0)
            {
                var entry = _entryPool.Dequeue();
                entry.SetActive(true);
                return entry;
            }

            return Instantiate(userListEntryPrefab);
        }

        private void ReturnToPool(GameObject entry)
        {
            entry.SetActive(false);
            entry.transform.SetParent(null);
            _entryPool.Enqueue(entry);
        }

        private void RefreshAllEntries()
        {
            ClearAllEntries();

            var manager = MetaDynUGSUserListManager.Instance;
            if (manager == null)
            {
                UpdateHeader(0);
                return;
            }

            ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            var users = manager.GetAllUsers();

            foreach (var user in users)
{
                if (!showLocalPlayerInList && user.ClientId == localClientId)
                    continue;

                CreateEntry(user);
            }

            UpdateHeader(users.Count);
        }

        private void CreateEntry(MetaDynUGSUserData userData)
        {
            if (_activeEntries.ContainsKey(userData.ClientId))
                return;

            var entryObj = GetPooledEntry();
            entryObj.transform.SetParent(userListContainer, false);

            var rectTransform = entryObj.GetComponent<RectTransform>();
            if (rectTransform != null)
                rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, entryHeight);

            var entry = entryObj.GetComponent<UserListEntry>();
            if (entry == null)
                return;

            ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            entry.Initialize(userData, userData.ClientId == localClientId);
            _activeEntries[userData.ClientId] = entry;
        }

        private void RemoveEntry(ulong clientId)
        {
            if (!_activeEntries.TryGetValue(clientId, out var entry))
                return;

            ReturnToPool(entry.gameObject);
            _activeEntries.Remove(clientId);
        }

        private void UpdateEntry(ulong clientId, MetaDynUGSUserData userData)
        {
            if (!_activeEntries.TryGetValue(clientId, out var entry))
            {
                CreateEntry(userData);
                return;
            }

            ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            entry.Initialize(userData, clientId == localClientId);
        }

        private void ClearAllEntries()
        {
            foreach (var entry in _activeEntries.Values)
                ReturnToPool(entry.gameObject);

            _activeEntries.Clear();
        }

        private void OnUserJoined(ulong clientId, MetaDynUGSUserData userData)
        {
            Debug.Log($"[UserListUI] UGS user joined: {userData.PlayerName}");

            ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            if (!showLocalPlayerInList && clientId == localClientId)
                return;

            CreateEntry(userData);
            UpdateHeader(MetaDynUGSUserListManager.Instance != null ? MetaDynUGSUserListManager.Instance.GetAllUsers().Count : 0);
        }

        private void OnUserLeft(ulong clientId)
        {
            Debug.Log($"[UserListUI] UGS user left: {clientId}");
            RemoveEntry(clientId);
            UpdateHeader(MetaDynUGSUserListManager.Instance != null ? MetaDynUGSUserListManager.Instance.GetAllUsers().Count : 0);
        }

        private void OnUserDataChanged(ulong clientId, MetaDynUGSUserData userData)
        {
            Debug.Log($"[UserListUI] UGS user data changed: {userData.PlayerName}");
            UpdateEntry(clientId, userData);
        }

        private void UpdateHeader(int userCount)
        {
            if (headerText != null)
                headerText.text = $"Online ({userCount})";
        }
    }
}
