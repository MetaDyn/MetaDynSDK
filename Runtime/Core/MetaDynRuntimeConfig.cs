using UnityEngine;

namespace MetaDyn
{
    /// <summary>
    /// Selects the mutually exclusive NGO networking topology used by a MetaDyn space.
    /// </summary>
    public enum MetaDynNetworkTopology
    {
        DistributedAuthority = 0,
        RelayClientHost = 1
    }

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

        [Header("Networking")]
        [Tooltip("Distributed Authority is the production social-space topology. Relay Client Host remains only as a temporary rollback path during conversion.")]
        public MetaDynNetworkTopology networkTopology = MetaDynNetworkTopology.DistributedAuthority;

        [Tooltip("UGS Relay region used by Distributed Authority. WebGL requires an explicit region because Relay QoS auto-selection is not supported there.")]
        public string relayRegion = "us-central1";

        [HideInInspector]
        [Tooltip("Legacy rejected Relay host-migration setting retained temporarily for serialized compatibility. Runtime code no longer enables it.")]
        public bool enableHostMigration = false;
        
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
