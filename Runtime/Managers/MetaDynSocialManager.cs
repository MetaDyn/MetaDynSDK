using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using MetaDyn.Dashboard;

namespace MetaDyn.Social
{
    /// <summary>
    /// Manages social data orchestration (friends, communities, inventory) for the MetaDyn platform.
    /// Bridges Supabase RLS-based data with the Unity UI.
    /// </summary>
    public class MetaDynSocialManager : MonoBehaviour
    {
        public static MetaDynSocialManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private float refreshInterval = 30f;
        [SerializeField] private bool autoRefresh = true;

        [Header("Data Cache")]
        public List<FriendEntry> Friends = new List<FriendEntry>();
        public List<CommunityEntry> Communities = new List<CommunityEntry>();
        public List<SpaceEntry> AccessibleSpaces = new List<SpaceEntry>();
        
        // Events
        public event Action OnSocialDataUpdated;
        public event Action<string> OnError;

        private Coroutine _refreshCoroutine;
        private bool _isInitialized = false;
        private readonly HashSet<string> _localFavoriteFriendIds = new HashSet<string>();

        private void Awake()
{
            if (Instance == null)
            {
                Instance = this;
                if (transform.parent == null) DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            // Wait for authentication success
            if (SupabaseAuthManager.Instance != null)
            {
                SupabaseAuthManager.Instance.OnLoginSuccess += Initialize;
                
                // If already logged in
                if (SupabaseAuthManager.Instance.IsAuthenticated)
                {
                    Initialize(SupabaseAuthManager.Instance.CurrentSession.user);
                }
            }
        }

        private void OnDestroy()
        {
            if (SupabaseAuthManager.Instance != null)
            {
                SupabaseAuthManager.Instance.OnLoginSuccess -= Initialize;
            }
            
            StopRefresh();
        }

        public void Initialize(SupabaseUser user)
        {
            if (_isInitialized) return;
            
            Debug.Log($"[MetaDyn Social] Initializing for user: {user.email} (ID: {user.id})");
            _isInitialized = true;
            
            RefreshSocialData();
            
            if (autoRefresh)
            {
                StartRefresh();
            }
        }

        public void RefreshSocialData()
        {
            if (SupabaseAuthManager.Instance == null || !SupabaseAuthManager.Instance.IsAuthenticated)
                return;

            StartCoroutine(FetchFriendsCoroutine());
            StartCoroutine(FetchCommunitiesCoroutine());
            StartCoroutine(FetchSpacesCoroutine());
        }

        public bool IsFriendFavorite(string friendId)
        {
            return _localFavoriteFriendIds.Contains(friendId);
        }

        public void ToggleFavorite(string friendId)
        {
            if (_localFavoriteFriendIds.Contains(friendId))
                _localFavoriteFriendIds.Remove(friendId);
            else
                _localFavoriteFriendIds.Add(friendId);

            // Update cache
            var friend = Friends.Find(f => f.Id == friendId);
            if (friend != null)
            {
                friend.IsFavorite = _localFavoriteFriendIds.Contains(friendId);
            }

            OnSocialDataUpdated?.Invoke();
        }

        private void StartRefresh()
{
            StopRefresh();
            _refreshCoroutine = StartCoroutine(RefreshLoop());
        }

        private void StopRefresh()
        {
            if (_refreshCoroutine != null)
            {
                StopCoroutine(_refreshCoroutine);
                _refreshCoroutine = null;
            }
        }

        private IEnumerator RefreshLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(refreshInterval);
                RefreshSocialData();
            }
        }

        #region API Calls

        private IEnumerator FetchFriendsCoroutine()
        {
            var auth = SupabaseAuthManager.Instance;
            if (auth == null || auth.Config == null) 
            {
                Debug.LogError("[MetaDyn Social] Auth Manager or Config is NULL during FetchFriends.");
                yield break;
            }
            var config = auth.Config;

            string userId = auth.CurrentSession.user.id;
            Debug.Log($"[MetaDyn Social] START FetchFriends for: {userId}");
            
            // Fixed column names: user_a_id and user_b_id
            string url1 = $"{config.SupabaseUrl}/rest/v1/friendships?user_a_id=eq.{userId}";
            string url2 = $"{config.SupabaseUrl}/rest/v1/friendships?user_b_id=eq.{userId}";

            List<FriendEntry> tempFriends = new List<FriendEntry>();

            yield return StartCoroutine(RunFriendQuery(url1, true, tempFriends));
            yield return StartCoroutine(RunFriendQuery(url2, false, tempFriends));

            if (tempFriends.Count > 0)
            {
                yield return StartCoroutine(FetchProfilesForFriends(tempFriends));
            }
            else
            {
                Debug.LogWarning("[MetaDyn Social] No friend IDs found in either query.");
            }

            Friends = tempFriends;
            Debug.Log($"[MetaDyn Social] SYNC COMPLETE. Unique friends in list: {Friends.Count}");
            OnSocialDataUpdated?.Invoke();
        }

        private IEnumerator RunFriendQuery(string url, bool isUserA, List<FriendEntry> list)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                SetHeaders(request);
                yield return request.SendWebRequest();

                string json = request.downloadHandler != null ? request.downloadHandler.text : "";
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var rawEntries = JsonHelper.FromJson<FriendshipResponseEntry>(json);
                    foreach (var entry in rawEntries)
                    {
                        bool isAccepted = string.IsNullOrEmpty(entry.status) || string.Equals(entry.status, "accepted", StringComparison.OrdinalIgnoreCase);
                        
                        if (isAccepted)
                        {
                            // If current user is A, friend is B. If current user is B, friend is A.
                            string targetFriendId = isUserA ? entry.user_b_id : entry.user_a_id;
                            if (!string.IsNullOrEmpty(targetFriendId) && !list.Exists(f => f.Id == targetFriendId))
                            {
                                list.Add(new FriendEntry { Id = targetFriendId, Status = "Online" });
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[MetaDyn Social] Friend Query Failed: {request.error} | Body: {json}");
                }
            }
        }

        private IEnumerator FetchProfilesForFriends(List<FriendEntry> list)
        {
            var config = SupabaseAuthManager.Instance.Config;
            
            List<string> ids = new List<string>();
            foreach (var f in list) ids.Add(f.Id);
            string idList = string.Join(",", ids);

            // Fetch profiles for all found friend IDs in one go
            string url = $"{config.SupabaseUrl}/rest/v1/profiles?id=in.({idList})";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                SetHeaders(request);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var profiles = JsonHelper.FromJson<SupabaseProfile>(request.downloadHandler.text);
                    foreach (var p in profiles)
                    {
                        var friend = list.Find(f => f.Id == p.id);
                        if (friend != null)
                        {
                            friend.Name = p.name;
                            friend.AvatarUrl = p.avatar_url;
                            friend.AvatarIndex = p.avatar_index;
                            friend.IsFavorite = IsFriendFavorite(friend.Id);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[MetaDyn Social] Profile batch fetch failed: {request.error}");
                }
            }
        }

        private IEnumerator FetchCommunitiesCoroutine()
        {
            var auth = SupabaseAuthManager.Instance;
            var config = auth.Config;
            if (config == null) yield break;

            string userId = auth.CurrentSession.user.id;
            string url = $"{config.SupabaseUrl}/rest/v1/space_memberships?select=*,spaces(*)&user_id=eq.{userId}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                SetHeaders(request);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    ProcessCommunitiesResponse(request.downloadHandler.text);
                }
                else
                {
                    Debug.LogWarning($"[MetaDyn Social] Failed to fetch communities: {request.error}");
                }
            }
        }

        private IEnumerator FetchSpacesCoroutine()
        {
            var auth = SupabaseAuthManager.Instance;
            var config = auth.Config;
            if (config == null) yield break;

            // Fetch all spaces (or filter as needed)
            string url = $"{config.SupabaseUrl}/rest/v1/spaces?select=*&order=name.asc";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                SetHeaders(request);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    ProcessSpacesResponse(request.downloadHandler.text);
                }
                else
                {
                    Debug.LogWarning($"[MetaDyn Social] Failed to fetch spaces: {request.error}");
                }
            }
        }

        #endregion

        #region Response Processing

        private void ProcessFriendsResponse(string json)
        {
            try
            {
                var rawEntries = JsonHelper.FromJson<FriendshipResponseEntry>(json);
                
                foreach (var entry in rawEntries)
                {
                    var profile = entry.friend ?? entry.profiles;
                    
                    if (profile != null)
                    {
                        if (!Friends.Exists(f => f.Id == profile.id))
                        {
                            Friends.Add(new FriendEntry
                            {
                                Id = profile.id,
                                Name = profile.name,
                                AvatarUrl = profile.avatar_url,
                                AvatarIndex = profile.avatar_index,
                                Status = "Online"
                            });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn Social] Friend Processing Error: {e.Message}");
            }
        }

        private void ProcessCommunitiesResponse(string json)
        {
            try
            {
                var rawEntries = JsonHelper.FromJson<SpaceMembershipResponseEntry>(json);
                
                Communities.Clear();
                foreach (var entry in rawEntries)
                {
                    if (entry.spaces != null)
                    {
                        Communities.Add(new CommunityEntry
                        {
                            Id = entry.spaces.id,
                            Name = entry.spaces.name,
                            Topic = entry.spaces.description,
                            SpaceUrl = entry.spaces.space_url,
                            MemberCount = 0 
                        });
                    }
                }

                Debug.Log($"[MetaDyn Social] Loaded {Communities.Count} communities.");
                OnSocialDataUpdated?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn Social] Community Processing Error: {e.Message}");
            }
        }

        private void ProcessSpacesResponse(string json)
        {
            try
            {
                var rawSpaces = JsonHelper.FromJson<SpaceData>(json);
                AccessibleSpaces.Clear();
                foreach (var s in rawSpaces)
                {
                    AccessibleSpaces.Add(new SpaceEntry
                    {
                        Id = s.id,
                        Name = s.name,
                        Description = s.description,
                        ThumbnailUrl = s.thumbnail_url,
                        SpaceUrl = s.space_url
                    });
                }
                Debug.Log($"[MetaDyn Social] Loaded {AccessibleSpaces.Count} spaces.");
                OnSocialDataUpdated?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn Social] Space Processing Error: {e.Message}");
            }
        }

        #endregion

        #region Debug & Utilities

        [ContextMenu("Debug/Log Friendships Table")]
        public void DebugLogFriendships()
        {
            StartCoroutine(DebugLogFriendshipsCoroutine());
        }

        private IEnumerator DebugLogFriendshipsCoroutine()
        {
            var auth = SupabaseAuthManager.Instance;
            var config = auth.Config;
            string url = $"{config.SupabaseUrl}/rest/v1/friendships?limit=5";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                SetHeaders(request);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[MetaDyn Social DEBUG] Friendships Table Raw Data: {request.downloadHandler.text}");
                }
                else
                {
                    Debug.LogError($"[MetaDyn Social DEBUG] Failed: {request.error} | {request.downloadHandler.text}");
                }
            }
        }

        private void SetHeaders(UnityWebRequest request)
        {
            var auth = SupabaseAuthManager.Instance;
            var config = auth.Config;
            
            request.SetRequestHeader("apikey", config.AnonKey);
            request.SetRequestHeader("Authorization", auth.GetAuthHeader());
            request.SetRequestHeader("X-Client-Info", "metadyn-unity-sdk");
        }

        #endregion

        // JSON Helper Classes for Supabase Response
        [Serializable]
        public class FriendshipResponseEntry
        {
            public string user_a_id;
            public string user_b_id;
            public string status;
            public SupabaseProfile profiles; // Default join key
            public SupabaseProfile friend;   // Aliased join key
        }

        [Serializable]
        public class SpaceMembershipResponseEntry
        {
            public string user_id;
            public string space_id;
            public SpaceData spaces; 
        }

        [Serializable]
        public class SpaceData
        {
            public string id;
            public string name;
            public string description;
            public string thumbnail_url;
            public string space_url;
        }

        /// <summary>
        /// Utility for parsing JSON arrays from Supabase
        /// </summary>
        public static class JsonHelper
        {
            public static T[] FromJson<T>(string json)
            {
                string newJson = "{ \"Items\": " + json + "}";
                Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
                return wrapper.Items;
            }

            [Serializable]
            private class Wrapper<T>
            {
                public T[] Items;
            }
        }
    }

    [Serializable]
    public class FriendEntry
    {
        public string Id;
        public string Name;
        public string AvatarUrl;
        public int AvatarIndex;
        public string Status;
        public DateTime DateAdded = DateTime.Now;
        public bool IsFavorite;
    }

    [Serializable]
    public class CommunityEntry
    {
        public string Id;
        public string Name;
        public string Topic;
        public string SpaceUrl;
        public int MemberCount;
    }

    [Serializable]
    public class SpaceEntry
    {
        public string Id;
        public string Name;
        public string Description;
        public string ThumbnailUrl;
        public string SpaceUrl;
    }
}

