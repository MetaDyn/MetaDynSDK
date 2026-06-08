using UnityEngine;

namespace MetaDyn
{
    /// <summary>
    /// Runtime configuration for MetaDyn builds.
    /// Created automatically during build/deployment process.
    /// This ScriptableObject must be placed in a Resources folder to be accessible at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "MetaDynRuntimeConfig", menuName = "MetaDyn/Runtime Config")]
    public class MetaDynRuntimeConfig : ScriptableObject
    {
        private static MetaDynRuntimeConfig _instance;
        
        /// <summary>
        /// Singleton instance - loads from Resources folder
        /// </summary>
        public static MetaDynRuntimeConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<MetaDynRuntimeConfig>("MetaDynRuntimeConfig");
                    
                    if (_instance == null)
                    {
                        Debug.LogError("[MetaDyn] Runtime config not found in Resources folder! Create MetaDynRuntimeConfig asset in Assets/Resources/");
                    }
                }
                return _instance;
            }
        }
        
        [Header("Identity")]
        [Tooltip("The unique Supabase GUID for this space (Source of Truth)")]
        public string spaceId = "";
        
        [Tooltip("The Supabase User ID of the space owner (for Admin permissions)")]
        public string ownerId = "";

        [Header("World Configuration")]
        [Tooltip("The room/world name players will join (set at build time)")]
        public string roomName = "DefaultWorld";
        
        [Tooltip("Display name for this world")]
        public string worldDisplayName = "MetaDyn World";
        
        [Tooltip("Maximum players allowed in this world")]
        public int maxPlayers = 8;
        
        [Header("Build Information")]
        [Tooltip("Automatically set during build/deployment")]
        public string buildVersion = "1.0.0";
        
        [Tooltip("Build timestamp")]
        public string buildTimestamp = "";
        
        [Tooltip("Deployment URL (matches server profile)")]
        public string deploymentURL = "";
        
        /// <summary>
        /// Validate configuration
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(roomName) && maxPlayers > 0;
        }
    }
}