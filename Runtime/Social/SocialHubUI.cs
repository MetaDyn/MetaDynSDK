using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MetaDyn.Dashboard;

namespace MetaDyn.Social
{
    /// <summary>
    /// Controls the Social Hub UI, managing tabs for Friends, Inventory, and Communities.
    /// Integrates with MetaDynSocialManager for data.
    /// </summary>
    public class SocialHubUI : MonoBehaviour
    {
        [Header("UI Settings")]
        [SerializeField] private bool startVisible = false;
        [SerializeField] private KeyCode toggleKey = KeyCode.H;

        [Header("UI References")]
        [SerializeField] private GameObject hubPanel;
        [SerializeField] private TMP_Text headerText;
        
        [Header("Tabs")]
        [SerializeField] private Button friendsTabButton;
        [SerializeField] private Button spacesTabButton;
        [SerializeField] private Button inventoryTabButton;
        [SerializeField] private Button communitiesTabButton;
        [SerializeField] private Button marketplaceTabButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject friendsPanel;
        [SerializeField] private GameObject spacesPanel;
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private GameObject communitiesPanel;
        [SerializeField] private GameObject marketplacePanel;

        [Header("List Containers")]
        [SerializeField] private Transform friendsContainer;
        [SerializeField] private Transform spacesContainer;
        [SerializeField] private Transform inventoryContainer;
        [SerializeField] private Transform communitiesContainer;
        [SerializeField] private Transform marketplaceContainer;
        [SerializeField] private GameObject socialEntryPrefab;

        [Header("Status & Sorting")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private GameObject sortBar;

        public enum SocialSortMode { Default, Alphabetical, Newest, Oldest, Favorite }
        private SocialSortMode _currentSortMode = SocialSortMode.Alphabetical;

        private void Start()
        {
            if (hubPanel == null)
            {
                Debug.LogError("[SocialHubUI] Hub Panel reference is missing.");
                enabled = false;
                return;
            }

            // Setup listeners
            friendsTabButton?.onClick.AddListener(() => SwitchTab(SocialHubTab.Friends));
            spacesTabButton?.onClick.AddListener(() => SwitchTab(SocialHubTab.Spaces));
            inventoryTabButton?.onClick.AddListener(() => SwitchTab(SocialHubTab.Inventory));
            communitiesTabButton?.onClick.AddListener(() => SwitchTab(SocialHubTab.Communities));
            marketplaceTabButton?.onClick.AddListener(() => SwitchTab(SocialHubTab.Marketplace));
            closeButton?.onClick.AddListener(ToggleHub);

            hubPanel.SetActive(startVisible);
            
            if (startVisible)
            {
                SwitchTab(SocialHubTab.Friends);
            }

            // Subscribe to data updates
            if (MetaDynSocialManager.Instance != null)
            {
                MetaDynSocialManager.Instance.OnSocialDataUpdated += RefreshUI;
            }

            // Dynamically wire up sorting buttons
            if (sortBar != null)
            {
                string[] labels = { "Default", "A-Z", "Newest", "Oldest", "Favorite" };
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    Transform btn = sortBar.transform.Find($"{labels[i]}Btn");
                    if (btn != null)
                    {
                        Button b = btn.GetComponent<Button>();
                        if (b != null) b.onClick.AddListener(() => SetSortMode(index));
                    }
                }
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                ToggleHub();
            }
        }

        private void OnDestroy()
        {
            if (MetaDynSocialManager.Instance != null)
            {
                MetaDynSocialManager.Instance.OnSocialDataUpdated -= RefreshUI;
            }
        }

        public void ToggleHub()
        {
            bool isVisible = !hubPanel.activeSelf;
            hubPanel.SetActive(isVisible);

            if (isVisible)
            {
                InputManager.LockInput("SocialHub");
                SwitchTab(SocialHubTab.Friends);
                RefreshUI();
            }
            else
            {
                InputManager.UnlockInput("SocialHub");
            }
        }

        public void SwitchTab(SocialHubTab tab)
        {
            if (friendsPanel != null) friendsPanel.SetActive(tab == SocialHubTab.Friends);
            if (spacesPanel != null) spacesPanel.SetActive(tab == SocialHubTab.Spaces);
            if (inventoryPanel != null) inventoryPanel.SetActive(tab == SocialHubTab.Inventory);
            if (communitiesPanel != null) communitiesPanel.SetActive(tab == SocialHubTab.Communities);
            if (marketplacePanel != null) marketplacePanel.SetActive(tab == SocialHubTab.Marketplace);

            if (sortBar != null)
            {
                sortBar.SetActive(tab == SocialHubTab.Friends);
            }

            // Update header or styling
            if (headerText != null)
            {
                headerText.text = tab.ToString().ToUpper();
            }

            RefreshUI();
        }

        public void SetSortMode(int modeIndex)
        {
            _currentSortMode = (SocialSortMode)modeIndex;
            RefreshUI();
        }

        public void RefreshUI()
        {
            if (hubPanel == null || !hubPanel.activeSelf || MetaDynSocialManager.Instance == null) return;

            // Ensure containers are correctly configured for layout
            if (friendsContainer != null) ConfigureContainer(friendsContainer);
            if (spacesContainer != null) ConfigureContainer(spacesContainer);
            if (communitiesContainer != null) ConfigureContainer(communitiesContainer);
            if (inventoryContainer != null) ConfigureContainer(inventoryContainer);
            if (marketplaceContainer != null) ConfigureContainer(marketplaceContainer);

            // Clear and Populate
            RefreshTab(SocialHubTab.Friends);
            RefreshTab(SocialHubTab.Spaces);
            RefreshTab(SocialHubTab.Communities);

            if (statusText != null)
            {
                statusText.text = $"LAST UPDATED: {System.DateTime.Now:HH:mm:ss}";
            }

            // Force layout rebuild to ensure visibility
            Canvas.ForceUpdateCanvases();
        }

        private void RefreshTab(SocialHubTab tab)
        {
            if (MetaDynSocialManager.Instance == null) return;

            switch (tab)
            {
                case SocialHubTab.Friends:
                    if (friendsContainer == null) break;
                    ClearContainer(friendsContainer);
                    var sortedFriends = new List<FriendEntry>(MetaDynSocialManager.Instance.Friends);
                    ApplySorting(sortedFriends);
                    foreach (var f in sortedFriends) CreateFriendEntry(f);
                    break;
                case SocialHubTab.Spaces:
                    if (spacesContainer == null) break;
                    ClearContainer(spacesContainer);
                    foreach (var s in MetaDynSocialManager.Instance.AccessibleSpaces) CreateSpaceEntry(s);
                    break;
                case SocialHubTab.Communities:
                    if (communitiesContainer == null) break;
                    ClearContainer(communitiesContainer);
                    foreach (var c in MetaDynSocialManager.Instance.Communities) CreateCommunityEntry(c);
                    break;
            }
        }

        private void ApplySorting(List<FriendEntry> list)
        {
            switch (_currentSortMode)
            {
                case SocialSortMode.Alphabetical:
                    list.Sort((a, b) => string.Compare(a.Name, b.Name));
                    break;
                case SocialSortMode.Newest:
                    list.Sort((a, b) => b.DateAdded.CompareTo(a.DateAdded));
                    break;
                case SocialSortMode.Oldest:
                    list.Sort((a, b) => a.DateAdded.CompareTo(b.DateAdded));
                    break;
                case SocialSortMode.Favorite:
                    list.Sort((a, b) => {
                        int favComp = b.IsFavorite.CompareTo(a.IsFavorite);
                        return favComp != 0 ? favComp : string.Compare(a.Name, b.Name);
                    });
                    break;
            }
        }

        private void ConfigureContainer(Transform container)
        {
            if (container == null) return;

            var vlg = container.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = container.gameObject.AddComponent<VerticalLayoutGroup>();
            
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 10;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childAlignment = TextAnchor.UpperCenter;

            var csf = container.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = container.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Ensure the Viewport parent has a Mask and correct sizing
            Transform viewport = container.parent;
            if (viewport != null)
            {
                var mask = viewport.GetComponent<Mask>();
                if (mask == null) mask = viewport.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = false;

                var img = viewport.GetComponent<UnityEngine.UI.Image>();
                if (img == null) img = viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
                img.color = Color.white; // Match ChatUI standard
                img.enabled = true;
                
                RectTransform vRT = viewport.GetComponent<RectTransform>();
vRT.anchorMin = Vector2.zero;
                vRT.anchorMax = Vector2.one;
                vRT.offsetMin = Vector2.zero;
                vRT.offsetMax = Vector2.zero;
            }
        }

        private void CreateFriendEntry(FriendEntry friend)
        {
            if (socialEntryPrefab == null || friendsContainer == null) return;

            GameObject go = Instantiate(socialEntryPrefab, friendsContainer);
            
            // Force reset transform to ensure visibility
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.localScale = Vector3.one;
                rt.anchoredPosition3D = Vector3.zero;
            }

            var entry = go.GetComponent<SocialHubEntry>();
            if (entry != null)
            {
                entry.Setup(friend.Name, friend.Status, friend.AvatarUrl, friend.Id, friend.IsFavorite);
                entry.OnChatClicked += () => StartChat(friend);
                entry.OnJoinClicked += () => JoinFriend(friend);
            }
        }

        private void CreateSpaceEntry(SpaceEntry space)
        {
            if (socialEntryPrefab == null || spacesContainer == null) return;

            GameObject go = Instantiate(socialEntryPrefab, spacesContainer);
            
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.localScale = Vector3.one;
                rt.anchoredPosition3D = Vector3.zero;
            }

            var entry = go.GetComponent<SocialHubEntry>();
            if (entry != null)
            {
                entry.Setup(space.Name, space.Description, space.ThumbnailUrl);
                entry.OnJoinClicked += () => JoinSpace(space);
            }
        }

        private void JoinSpace(SpaceEntry space)
        {
            if (string.IsNullOrEmpty(space.SpaceUrl))
            {
                Debug.LogWarning($"[SocialHub] Cannot join space '{space.Name}': URL is missing.");
                return;
            }

            Debug.Log($"[SocialHub] Redirecting to space: {space.Name} ({space.SpaceUrl})");
            
            if (WebAuthBridge.Instance != null)
            {
                WebAuthBridge.Instance.OpenURL(space.SpaceUrl);
            }
            else
            {
                Application.OpenURL(space.SpaceUrl);
            }
        }

        private void CreateCommunityEntry(CommunityEntry community)
        {
            if (socialEntryPrefab == null || communitiesContainer == null) return;

            GameObject go = Instantiate(socialEntryPrefab, communitiesContainer);
            
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.localScale = Vector3.one;
                rt.anchoredPosition3D = Vector3.zero;
            }

            var entry = go.GetComponent<SocialHubEntry>();
            if (entry != null)
            {
                entry.Setup(community.Name, community.Topic, "");
                entry.OnJoinClicked += () => JoinCommunity(community);
            }
        }

        private void ClearContainer(Transform container)
        {
            if (container == null) return;
            foreach (Transform child in container)
            {
                Destroy(child.gameObject);
            }
        }

        private void StartChat(FriendEntry friend)
        {
            Debug.Log($"[SocialHub] Starting chat with {friend.Name}");
            // Integration with ChatUI or DM system
        }

        private void JoinFriend(FriendEntry friend)
        {
            Debug.Log($"[SocialHub] Attempting to join {friend.Name}");
            // Integration with SessionService to warp to their space
        }

        private void JoinCommunity(CommunityEntry community)
        {
            if (string.IsNullOrEmpty(community.SpaceUrl))
            {
                Debug.LogWarning($"[SocialHub] Cannot join community '{community.Name}': URL is missing.");
                return;
            }

            Debug.Log($"[SocialHub] Redirecting to community space: {community.Name} ({community.SpaceUrl})");
            
            if (WebAuthBridge.Instance != null)
            {
                WebAuthBridge.Instance.OpenURL(community.SpaceUrl);
            }
            else
            {
                Application.OpenURL(community.SpaceUrl);
            }
        }
    }

    public enum SocialHubTab
    {
        Friends,
        Spaces,
        Inventory,
        Communities,
        Marketplace
    }
}
