using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using MetaDyn.Dashboard;

namespace MetaDyn.Editor
{
    /// <summary>
    /// Shared service for SDK provisioning and developer authentication.
    /// </summary>
    public static class MetaDynProvisioningService
    {
        public const string AUTH_TOKEN_KEY = "MetaDyn_AuthToken";
        private const string PROVISION_URL = "https://dashboard.metadyn.xyz/api/sdk/provision";

        public static string AuthToken
        {
            get => EditorPrefs.GetString(AUTH_TOKEN_KEY, "");
            set => EditorPrefs.SetString(AUTH_TOKEN_KEY, (value ?? string.Empty).Trim());
        }

        public static bool IsProvisioned
        {
            get
            {
                var config = GetSupabaseConfig();
                return config != null && !string.IsNullOrEmpty(config.SupabaseUrl);
            }
        }

        public static async Task<bool> ProvisionSDK(string token, Action<float, string> onProgress = null)
        {
            if (string.IsNullOrEmpty(token)) return false;

            onProgress?.Invoke(0.1f, "Contacting Registry...");

            try
            {
                using (UnityWebRequest request = UnityWebRequest.PostWwwForm(PROVISION_URL, ""))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {token}");
                    request.SetRequestHeader("X-Client-Version", MetaDynSDK.SDK_VERSION);

                    var operation = request.SendWebRequest();
                    while (!operation.isDone) await Task.Yield();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var data = JsonUtility.FromJson<ProvisionResponse>(request.downloadHandler.text);
                        if (data != null && !string.IsNullOrEmpty(data.supabase_url))
                        {
                            UpdateSupabaseConfig(data.supabase_url, data.anon_key);
                            return true;
                        }
                    }
                    else
                    {
                        Debug.LogError($"[MetaDyn] Provisioning failed: {request.downloadHandler.text}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn] Provisioning error: {e.Message}");
            }

            return false;
        }

        public static void UpdateSupabaseConfig(string url, string key)
        {
            var config = GetSupabaseConfig();
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<SupabaseConfig>();
                const string configDirectory = "Assets/MetaDyn/Runtime/Core";
                if (!Directory.Exists(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                    AssetDatabase.Refresh();
                }
                AssetDatabase.CreateAsset(config, $"{configDirectory}/SupabaseConfig.asset");
            }

            Undo.RecordObject(config, "Provision SDK");
            config.SupabaseUrl = url;
            config.AnonKey = key;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static SupabaseConfig GetSupabaseConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:SupabaseConfig");
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<SupabaseConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            return null;
        }

        [Serializable]
        private class ProvisionResponse
        {
            public string supabase_url;
            public string anon_key;
            public string project_name;
        }
    }
}
