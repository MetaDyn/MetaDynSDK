using UnityEngine;

namespace MetaDyn.Dashboard
{
    /// <summary>
    /// Configuration for Supabase connection.
    /// Create via: Assets > Create > MetaDyn > Supabase Config
    /// </summary>
    [CreateAssetMenu(fileName = "SupabaseConfig", menuName = "MetaDyn/Supabase Config", order = 1)]
    public class SupabaseConfig : ScriptableObject
    {
        [Header("Supabase Project Settings")]
        [Tooltip("Your Supabase project URL (e.g., https://xxxxx.supabase.co)")]
        public string SupabaseUrl;

        [Tooltip("Your Supabase anonymous (public) key")]
        [TextArea(3, 5)]
        public string AnonKey;

        [Header("Info")]
        [TextArea(3, 10)]
        public string Instructions =
            "Get these values from your Supabase Dashboard:\n" +
            "1. Go to Settings > API\n" +
            "2. Copy 'Project URL' to SupabaseUrl\n" +
            "3. Copy 'anon/public' key to AnonKey\n\n" +
            "These should match the values in your React dashboard's supabaseClient.ts";

        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(SupabaseUrl) && !SupabaseUrl.StartsWith("https://"))
            {
                Debug.LogWarning("[SupabaseConfig] URL should start with https://");
            }

            if (!string.IsNullOrEmpty(AnonKey) && AnonKey.Length < 100)
            {
                Debug.LogWarning("[SupabaseConfig] Anon key looks too short. Make sure you copied the full key.");
            }
        }
    }
}
