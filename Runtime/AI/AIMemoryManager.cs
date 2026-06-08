using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MetaDyn.AI
{
    /// <summary>
    /// Manages persistent AI memory via Cloudflare edge API.
    /// Stores user encounters, facts, and conversation summaries.
    /// Retrieves relevant memories for context injection.
    /// </summary>
    public class AIMemoryManager : MonoBehaviour
    {
        [Header("API Configuration")]
        [Tooltip("Base URL for memory API (e.g., https://memory.metadyn.xyz)")]
        public string memoryApiUrl = "https://memory.metadyn.xyz";

        [Header("References")]
        [Tooltip("Perception manager to get active user info")]
        public AIPerceptionManager perceptionManager;

        [Header("Settings")]
        [Tooltip("Number of memories to retrieve for context")]
        public int memoryLimit = 5;

        [Tooltip("Auto-save conversation every X seconds (0 = disabled)")]
        public float autoSaveInterval = 60f;

        [Tooltip("Enable debug logging")]
        public bool debugLogging = true;

        // Cached user data from last seen call
        private UserSeenResponse _lastUserData;
        private MemoryRecallResponse _lastRecall;

        // Public accessors
        public bool IsNewUser => _lastUserData?.is_new ?? true;
        public int InteractionCount => _lastUserData?.user?.interaction_count ?? 0;
        public string LastUserDisplayName => _lastUserData?.user?.display_name;

        /// <summary>
        /// Record that a user was seen. Call when user approaches.
        /// </summary>
        public void RecordUserSeen(string userId, string displayName = null, Action<UserSeenResponse> onComplete = null)
        {
            StartCoroutine(PostUserSeen(userId, displayName, onComplete));
        }

        /// <summary>
        /// Recall relevant memories for context injection.
        /// </summary>
        public void RecallMemories(string query, string userId = null, Action<MemoryRecallResponse> onComplete = null)
        {
            StartCoroutine(PostRecallMemory(query, userId, onComplete));
        }

        /// <summary>
        /// Store a new memory (fact, observation, etc).
        /// </summary>
        public void StoreMemory(string content, string memoryType, string userId = null, string category = null, Action<bool> onComplete = null)
        {
            StartCoroutine(PostStoreMemory(content, memoryType, userId, category, onComplete));
        }

        /// <summary>
        /// Store a conversation summary after interaction ends.
        /// </summary>
        public void StoreConversation(string userId, string summary, string topics = null, string sentiment = null, string location = null, Action<bool> onComplete = null)
        {
            StartCoroutine(PostStoreConversation(userId, summary, topics, sentiment, location, onComplete));
        }

        /// <summary>
        /// Get formatted memory context string for LLM injection.
        /// Call after RecallMemories completes.
        /// </summary>
        public string GetMemoryContext()
        {
            if (_lastRecall == null || _lastRecall.memories == null || _lastRecall.memories.Length == 0)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MEMORY CONTEXT - Previous interactions with this user:]");

            if (_lastRecall.user != null)
            {
                sb.AppendLine($"- User: {_lastRecall.user.display_name ?? "Unknown"}");
                sb.AppendLine($"- First met: {_lastRecall.user.first_seen_at}");
                sb.AppendLine($"- Times met: {_lastRecall.user.interaction_count}");
            }

            sb.AppendLine("- Relevant memories:");
            foreach (var memory in _lastRecall.memories)
            {
                sb.AppendLine($"  * [{memory.memory_type}] {memory.content_preview}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get user greeting based on memory (new vs returning user).
        /// </summary>
        public string GetUserGreetingHint()
        {
            if (_lastUserData == null) return "";

            if (_lastUserData.is_new)
            {
                return "[This is a NEW user you haven't met before. Introduce yourself warmly.]";
            }
            else
            {
                string name = _lastUserData.user?.display_name ?? "this user";
                int count = _lastUserData.user?.interaction_count ?? 1;
                return $"[You've met {name} before ({count} times). Greet them as a returning friend.]";
            }
        }

        #region API Calls

        private IEnumerator PostUserSeen(string userId, string displayName, Action<UserSeenResponse> onComplete)
        {
            string url = $"{memoryApiUrl}/user/seen";
            string json = JsonUtility.ToJson(new UserSeenRequest { user_id = userId, display_name = displayName });

            using (UnityWebRequest www = CreatePostRequest(url, json))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    _lastUserData = JsonUtility.FromJson<UserSeenResponse>(www.downloadHandler.text);
                    if (debugLogging) Debug.Log($"[AIMemory] User seen: {userId}, new={_lastUserData.is_new}, count={_lastUserData.user?.interaction_count}");
                    onComplete?.Invoke(_lastUserData);
                }
                else
                {
                    if (debugLogging) Debug.LogError($"[AIMemory] UserSeen error: {www.error}");
                    onComplete?.Invoke(null);
                }
            }
        }

        private IEnumerator PostRecallMemory(string query, string userId, Action<MemoryRecallResponse> onComplete)
        {
            string url = $"{memoryApiUrl}/memory/recall";
            string json = JsonUtility.ToJson(new MemoryRecallRequest { query = query, user_id = userId, limit = memoryLimit });

            using (UnityWebRequest www = CreatePostRequest(url, json))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    _lastRecall = JsonUtility.FromJson<MemoryRecallResponse>(www.downloadHandler.text);
                    if (debugLogging) Debug.Log($"[AIMemory] Recalled {_lastRecall.memories?.Length ?? 0} memories for query: {query}");
                    onComplete?.Invoke(_lastRecall);
                }
                else
                {
                    if (debugLogging) Debug.LogError($"[AIMemory] Recall error: {www.error}");
                    onComplete?.Invoke(null);
                }
            }
        }

        private IEnumerator PostStoreMemory(string content, string memoryType, string userId, string category, Action<bool> onComplete)
        {
            string url = $"{memoryApiUrl}/memory/store";
            string json = JsonUtility.ToJson(new StoreMemoryRequest
            {
                content = content,
                memory_type = memoryType,
                user_id = userId,
                category = category
            });

            using (UnityWebRequest www = CreatePostRequest(url, json))
            {
                yield return www.SendWebRequest();

                bool success = www.result == UnityWebRequest.Result.Success;
                if (debugLogging)
                {
                    if (success) Debug.Log($"[AIMemory] Stored memory: {memoryType} - {content.Substring(0, Math.Min(50, content.Length))}...");
                    else Debug.LogError($"[AIMemory] Store error: {www.error}");
                }
                onComplete?.Invoke(success);
            }
        }

        private IEnumerator PostStoreConversation(string userId, string summary, string topics, string sentiment, string location, Action<bool> onComplete)
        {
            string url = $"{memoryApiUrl}/conversation/store";
            string json = JsonUtility.ToJson(new StoreConversationRequest
            {
                user_id = userId,
                summary = summary,
                topics = topics,
                sentiment = sentiment,
                location = location
            });

            using (UnityWebRequest www = CreatePostRequest(url, json))
            {
                yield return www.SendWebRequest();

                bool success = www.result == UnityWebRequest.Result.Success;
                if (debugLogging)
                {
                    if (success) Debug.Log($"[AIMemory] Stored conversation for {userId}");
                    else Debug.LogError($"[AIMemory] Conversation store error: {www.error}");
                }
                onComplete?.Invoke(success);
            }
        }

        private UnityWebRequest CreatePostRequest(string url, string json)
        {
            var www = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            return www;
        }

        #endregion

        #region Data Classes

        [Serializable]
        public class UserSeenRequest
        {
            public string user_id;
            public string display_name;
        }

        [Serializable]
        public class UserSeenResponse
        {
            public bool success;
            public UserData user;
            public bool is_new;
        }

        [Serializable]
        public class UserData
        {
            public string id;
            public string display_name;
            public string first_seen_at;
            public string last_seen_at;
            public int interaction_count;
        }

        [Serializable]
        public class MemoryRecallRequest
        {
            public string query;
            public string user_id;
            public int limit;
        }

        [Serializable]
        public class MemoryRecallResponse
        {
            public MemoryItem[] memories;
            public UserData user;
        }

        [Serializable]
        public class MemoryItem
        {
            public string id;
            public float score;
            public string user_id;
            public string memory_type;
            public string content_preview;
            public string created_at;
        }

        [Serializable]
        public class StoreMemoryRequest
        {
            public string user_id;
            public string content;
            public string memory_type;
            public string category;
        }

        [Serializable]
        public class StoreConversationRequest
        {
            public string user_id;
            public string summary;
            public string topics;
            public string sentiment;
            public string location;
        }

        #endregion
    }
}
