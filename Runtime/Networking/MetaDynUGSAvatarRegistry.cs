using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MetaDyn.Networking
{
    /// <summary>
    /// Canonical registry for all spawnable player avatars in the UGS/NGO stack.
    /// </summary>
    [CreateAssetMenu(fileName = "MetaDynUGSAvatarRegistry", menuName = "MetaDyn/Networking/Avatar Registry")]
    public class MetaDynUGSAvatarRegistry : ScriptableObject
    {
        private static MetaDynUGSAvatarRegistry _instance;
        public static MetaDynUGSAvatarRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<MetaDynUGSAvatarRegistry>("MetaDynUGSAvatarRegistry");
                    if (_instance == null)
                    {
                        Debug.LogWarning("[MetaDyn Avatar Registry] Instance not found in Resources. Please create one and place it in a Resources folder.");
                    }
                }
                return _instance;
            }
        }

        [Header("Ready Player Me Avatars")]
        public List<AvatarEntry> readyPlayerMeAvatars = new List<AvatarEntry>();

        [Header("Avatar SDK Avatars")]
        public List<AvatarEntry> avatarSDKAvatars = new List<AvatarEntry>();

        [Header("Default Fallback")]
        public NetworkObject defaultPlayerPrefab;

        public NetworkObject GetPrefabByIndex(int index)
        {
            var all = GetAllAvatars();
            if (index >= 0 && index < all.Count && all[index].prefab != null)
            {
                return all[index].prefab;
            }

            return defaultPlayerPrefab;
        }

        public List<AvatarEntry> GetAllAvatars()
        {
            var allAvatars = new List<AvatarEntry>();
            allAvatars.AddRange(readyPlayerMeAvatars);
            allAvatars.AddRange(avatarSDKAvatars);
            return allAvatars;
        }

        [Serializable]
        public class AvatarEntry
        {
            public string name;
            public NetworkObject prefab;
            public Sprite thumbnail;
        }
    }
}
