using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using MetaDyn.Editor;

namespace MetaDyn
{
    /// <summary>
    /// MetaDyn SDK Dashboard - displays SDK information and status
    /// </summary>
    public class MetaDynDashboard : EditorWindow
    {
        private GUIStyle titleStyle;
        private GUIStyle versionStyle;
        private bool stylesInitialized = false;
        
        private string latestVersion = MetaDynSDK.SDK_VERSION;
        private bool isCheckingVersion = false;
        private string remoteManifestUrl = "https://raw.githubusercontent.com/MetaDyn/MetaDynSDK/main/Editor/Core/MetaDynSDK/MetaDynSDKManifest.json";

        public static void ShowWindow()
        {
            MetaDynDashboard window = GetWindow<MetaDynDashboard>("MetaDyn Dashboard");
            window.minSize = new Vector2(300, 200);
            window.Show();
        }

        private void OnEnable()
        {
            CheckForUpdates();
        }

        private async void CheckForUpdates()
        {
            if (isCheckingVersion) return;
            isCheckingVersion = true;

            try
            {
                using (var request = UnityEngine.Networking.UnityWebRequest.Get(remoteManifestUrl))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone) await System.Threading.Tasks.Task.Yield();

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        var manifest = JsonUtility.FromJson<MetaDynSDKManifestData>(request.downloadHandler.text);
                        latestVersion = manifest.latestVersion;
                        Repaint();
                    }
                }
            }
            catch (Exception) { /* Fail silently */ }
            finally
            {
                isCheckingVersion = false;
            }
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;
            
            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.2f, 0.6f, 1f) }
            };
            
            versionStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.gray }
            };
            
            stylesInitialized = true;
        }
        
        private void OnGUI()
        {
            InitializeStyles();
            string installedVersion = MetaDynSDK.SDK_VERSION;
            bool updateAvailable = IsUpdateAvailable(installedVersion, latestVersion);
            string statusText = isCheckingVersion ? "Checking..." : (updateAvailable ? "Update Available" : "Ready");
            bool isMaster = MetaDynSDK.IsMasterSDK;

            MetaDynEditorHeader.DrawHeader("SDK Dashboard", 
                "Official MetaDyn SDK management and update center. Verify your version and sync dependencies.");
            
            // Master SDK Badge
            if (isMaster)
            {
                EditorGUILayout.BeginHorizontal();
                var badgeStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    normal = { textColor = new Color(1f, 0.8f, 0f) }, // Gold
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 10
                };
                GUILayout.Label("  MASTER SDK ENVIRONMENT ACTIVE  ", badgeStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(5);
            }

            MetaDynStyle.DrawSectionHeader("Software Info");
            MetaDynStyle.BeginSection();
            EditorGUILayout.LabelField(MetaDynSDK.SDK_NAME, titleStyle, GUILayout.Height(25));
            EditorGUILayout.LabelField($"Installed Version: {installedVersion}", versionStyle);
            EditorGUILayout.LabelField($"Latest Available: {latestVersion}", versionStyle);
            MetaDynStyle.EndSection();
            
            GUILayout.Space(10);
            
            // Status information
            MetaDynStyle.DrawSectionHeader("System Status");
            MetaDynStyle.BeginSection();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Project Mode: {(isMaster ? "Source/Master" : "Fork/Divergent")}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Status: {statusText}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledGroupScope(!updateAvailable || isCheckingVersion))
            {
                if (GUILayout.Button("Update SDK", GUILayout.Width(110)))
                {
                    PerformUpdate();
                }
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Label($"Unity Version: {Application.unityVersion}", EditorStyles.miniLabel);
            MetaDynStyle.EndSection();

            GUILayout.Space(10);

            // Networking Stack section (UGS focus)
            MetaDynStyle.DrawSectionHeader("Networking Stack (UGS)");
            MetaDynStyle.BeginSection();
            
            DrawPackageStatus("Netcode for GameObjects", MetaDynSDK.SUPPORTED_NGO_VERSION, "com.unity.netcode.gameobjects");
            DrawPackageStatus("Vivox", MetaDynSDK.SUPPORTED_VIVOX_VERSION, "com.unity.services.vivox");
            DrawPackageStatus("Multiplayer (Relay/Lobby)", "1.2.1", "com.unity.services.multiplayer");
            
            GUILayout.Space(10);
            if (GUILayout.Button("Sync Project Manifest with SDK Baseline", GUILayout.Height(25)))
            {
                SyncProjectManifest();
            }

            GUILayout.Space(4);
            GUILayout.Label("Dependency Status: UGS/NGO Baseline Active", EditorStyles.miniLabel);
            MetaDynStyle.EndSection();
            
            GUILayout.FlexibleSpace();
            
            // Footer
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("MetaDyn Metaverse Stack", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(10);
        }

        private void DrawPackageStatus(string label, string supportedVersion, string packageId)
        {
            string installed = GetInstalledPackageVersion(packageId);
            bool isCorrect = installed != "Not found" && installed.Contains(supportedVersion.Split(' ')[0]);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{label}:", EditorStyles.miniLabel, GUILayout.Width(150));
            GUILayout.Label(installed, isCorrect ? EditorStyles.miniLabel : EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"(Target: {supportedVersion})", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private string GetInstalledPackageVersion(string packageId)
        {
            try
            {
                string manifestPath = "Packages/manifest.json";
                if (!File.Exists(manifestPath)) return "Not found";
                
                string content = File.ReadAllText(manifestPath);
                if (content.Contains(packageId))
                {
                    int index = content.IndexOf(packageId);
                    int start = content.IndexOf(":", index) + 1;
                    int end = content.IndexOf(",", start);
                    if (end == -1) end = content.IndexOf("}", start);
                    
                    return content.Substring(start, end - start).Trim().Replace("\"", "");
                }
            }
            catch { }
            return "Not found";
        }

        private void SyncProjectManifest()
        {
            if (!EditorUtility.DisplayDialog("Sync Manifest", 
                "This will update your project's manifest.json to match the MetaDyn SDK's recommended package versions. This may trigger a recompile.", 
                "Sync Now", "Cancel"))
                return;

            try
            {
                string manifestPath = "Packages/manifest.json";
                if (!File.Exists(manifestPath)) return;

                string content = File.ReadAllText(manifestPath);
                
                content = UpdatePackageVersion(content, "com.unity.netcode.gameobjects", MetaDynSDK.SUPPORTED_NGO_VERSION);
                content = UpdatePackageVersion(content, "com.unity.services.vivox", MetaDynSDK.SUPPORTED_VIVOX_VERSION);
                content = UpdatePackageVersion(content, "com.unity.services.multiplayer", "1.2.1");

                File.WriteAllText(manifestPath, content);
                AssetDatabase.Refresh();
                
                EditorUtility.DisplayDialog("Sync Complete", "Project manifest updated. Unity will now resolve dependencies.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Sync Failed", $"An error occurred: {ex.Message}", "OK");
            }
        }

        private string UpdatePackageVersion(string content, string packageId, string targetVersion)
        {
            if (!content.Contains(packageId)) return content;

            int index = content.IndexOf(packageId);
            int start = content.IndexOf(":", index) + 1;
            int end = content.IndexOf(",", start);
            if (end == -1) 
            {
                 int quoteStart = content.IndexOf("\"", start);
                 int quoteEnd = content.IndexOf("\"", quoteStart + 1);
                 end = quoteEnd + 1;
            }

            string oldLine = content.Substring(index, end - index);
            string newLine = $"\"{packageId}\": \"{targetVersion}\"";
            
            return content.Replace(oldLine, newLine);
        }

        private void PerformUpdate()
        {
            if (EditorUtility.DisplayDialog("Update SDK", 
                $"Do you want to update the MetaDyn SDK to version {latestVersion}? This will overwrite files in Assets/MetaDyn.", 
                "Update Now", "Cancel"))
            {
                MetaDynSDKUpdater.UpdateSDK(latestVersion);
            }
        }

        private static string GetLatestVersion()
        {
            return "1.3.1"; // Fallback
        }

        private static bool IsUpdateAvailable(string installedVersion, string latestVersion)
        {
            return !string.IsNullOrEmpty(latestVersion) && installedVersion != latestVersion;
        }

        [Serializable]
        private class MetaDynSDKManifestData
        {
            public string latestVersion;
        }
    }
}
