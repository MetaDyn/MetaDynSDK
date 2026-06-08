using UnityEngine;

namespace MetaDyn
{
    /// <summary>
    /// Metadata component for MetaDyn platform items.
    /// Used for identifying, licensing, and tracking objects across the metaverse.
    /// </summary>
    [AddComponentMenu("MetaDyn/Inventory/Item Metadata")]
    public class MetaDynItemMetadata : MonoBehaviour
    {
        public enum ItemRarity
        {
            Common,
            Uncommon,
            Rare,
            Epic,
            Legendary,
            Exotic,
            Artifact
        }

        public enum LicenseType
        {
            Proprietary,
            CC_BY,
            CC_BY_SA,
            CC_BY_ND,
            CC_BY_NC,
            CC0,
            PublicDomain
        }

        [Header("Identity")]
        [Tooltip("Unique Identifier for this item on the platform.")]
        public string itemId;
        
        [Tooltip("Display name of the item.")]
        public string displayName;
        
        [Tooltip("The creator or owner of this asset definition.")]
        public string creatorName;

        [Header("Classification")]
        public string category = "Ship";
        public ItemRarity rarity = ItemRarity.Common;
        public string version = "1.0.0";

        [Header("Licensing")]
        public LicenseType license = LicenseType.Proprietary;
        public string licenseUrl;

        [Header("Platform Integration")]
        [Tooltip("The linked JSON manifest for this item.")]
        public TextAsset manifestJson;

        [Tooltip("URL to the canonical platform manifest for this item.")]
        public string platformManifestUrl;
        
        [Tooltip("JSON metadata for extra platform-specific fields.")]
        [TextArea(3, 10)]
        public string customMetadata;

        /// <summary>
        /// Syncs component fields from the linked JSON manifest.
        /// </summary>
        [ContextMenu("Sync from Manifest")]
        public void SyncFromManifest()
        {
            if (manifestJson == null)
            {
                Debug.LogWarning("[MetaDyn] No manifest JSON linked to sync from.");
                return;
            }

            try
            {
                var data = JsonUtility.FromJson<ManifestSyncData>(manifestJson.text);
                itemId = data.itemId;
                displayName = data.displayName;
                category = data.category;
                creatorName = data.creator;
                version = data.version;
                
                if (System.Enum.TryParse(data.license, out LicenseType parsedLicense))
                {
                    license = parsedLicense;
                }

                Debug.Log($"[MetaDyn] Metadata synced from manifest: {manifestJson.name}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MetaDyn] Failed to sync from manifest: {e.Message}");
            }
        }

        [System.Serializable]
        private class ManifestSyncData
        {
            public string itemId;
            public string displayName;
            public string category;
            public string creator;
            public string license;
            public string version;
        }

        /// <summary>
        /// Generates a new unique ID if one doesn't exist.
        /// </summary>
        [ContextMenu("Generate New ID")]
        public void GenerateNewId()
        {
            itemId = System.Guid.NewGuid().ToString();
        }

        private void Reset()
        {
            if (string.IsNullOrEmpty(itemId))
            {
                GenerateNewId();
            }
            
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = gameObject.name;
            }
        }
    }
}
