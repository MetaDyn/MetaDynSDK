using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using MetaDyn.Editor;
using MetaDyn.Dashboard;

namespace MetaDyn
{
    /// <summary>
    /// MetaDyn Project Configuration - Professional deployment interface
    /// </summary>
    public class MetaDynProjectConfig : EditorWindow
    {
        private const string ROOM_NAME_KEY = "MetaDyn_RoomName";
        private const string BUILD_PATH_KEY = "MetaDyn_BuildPath";
        private const string SELECTED_PROFILE_KEY = "MetaDyn_SelectedProfile";
        private const string SELECTED_RUNTIME_CONFIG_KEY = "MetaDyn_SelectedRuntimeConfig";

        private string roomName = "";
        private string buildPath = "";
        private MetaDynServerProfile selectedProfile;
        private List<MetaDynServerProfile> serverProfiles = new List<MetaDynServerProfile>();

        // Runtime config - the source of truth for world settings
        private MetaDynRuntimeConfig selectedRuntimeConfig;
        private List<MetaDynRuntimeConfig> runtimeConfigs = new List<MetaDynRuntimeConfig>();

        private Vector2 scrollPosition;
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle successStyle;
        private GUIStyle errorStyle;
        private bool stylesInitialized = false;

        private bool isDeploying = false;
        private MetaDynRegistryProgressWindow deployProgressWindow;

        // Validation
        private List<ValidationResult> validationResults = new List<ValidationResult>();

        // Supabase Config
        private SupabaseConfig supabaseConfig;
        private string lastSyncedName = "";

        // Developer Auth
        private string authToken = "";

        public static void ShowWindow()
        {
            MetaDynProjectConfig window = GetWindow<MetaDynProjectConfig>("MetaDyn Deploy");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        private void OnEnable()
        {
            // Load saved settings
            roomName = EditorPrefs.GetString(ROOM_NAME_KEY, "");
            buildPath = EditorPrefs.GetString(BUILD_PATH_KEY, MetaDynDeploymentManager.GetWebGLBuildPath());
            authToken = MetaDynProvisioningService.AuthToken;

            LoadServerProfiles();
            LoadRuntimeConfigs();

            // Load Supabase Config
            supabaseConfig = MetaDynProvisioningService.GetSupabaseConfig();

            // Load selected runtime config
            string runtimeConfigGuid = EditorPrefs.GetString(SELECTED_RUNTIME_CONFIG_KEY, "");
            if (!string.IsNullOrEmpty(runtimeConfigGuid))
            {
                string configPath = AssetDatabase.GUIDToAssetPath(runtimeConfigGuid);
                selectedRuntimeConfig = AssetDatabase.LoadAssetAtPath<MetaDynRuntimeConfig>(configPath);
            }

            // Fallback: If nothing selected or selection invalid, pick the first one found
            if (selectedRuntimeConfig == null && runtimeConfigs.Count > 0)
            {
                selectedRuntimeConfig = runtimeConfigs[0];
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedRuntimeConfig));
                EditorPrefs.SetString(SELECTED_RUNTIME_CONFIG_KEY, guid);
            }

            if (selectedRuntimeConfig != null)
            {
                // Assume in sync initially
                lastSyncedName = selectedRuntimeConfig.worldDisplayName;

                // Pull fresh from DB if we have an ID
                if (!string.IsNullOrEmpty(selectedRuntimeConfig.spaceId))
                {
                    FetchSpaceName(selectedRuntimeConfig.spaceId);
                }
            }

            // Load selected server profile
            string profileGuid = EditorPrefs.GetString(SELECTED_PROFILE_KEY, "");
            if (!string.IsNullOrEmpty(profileGuid))
            {
                string profilePath = AssetDatabase.GUIDToAssetPath(profileGuid);
                selectedProfile = AssetDatabase.LoadAssetAtPath<MetaDynServerProfile>(profilePath);
            }

            RunValidation();
        }

        private void RunValidation()
        {
            validationResults = MetaDynSDKValidator.ValidateProject();
            Repaint();
        }

        private async void FetchSpaceName(string spaceId)
        {
            if (supabaseConfig == null || string.IsNullOrEmpty(spaceId)) return;

            string url = $"{supabaseConfig.SupabaseUrl}/rest/v1/spaces?id=eq.{spaceId}&select=name";

            using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", supabaseConfig.AnonKey);

                // Use Token if available, otherwise Anon
                string authHeader = !string.IsNullOrEmpty(authToken) ? $"Bearer {authToken}" : $"Bearer {supabaseConfig.AnonKey}";
                request.SetRequestHeader("Authorization", authHeader);

                var operation = request.SendWebRequest();
                while (!operation.isDone) await System.Threading.Tasks.Task.Yield();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    // Extract name from [{"name":"Value"}]
                    if (json.Contains("\"name\":\""))
                    {
                        int start = json.IndexOf("\"name\":\"") + 8;
                        int end = json.IndexOf("\"", start);
                        string remoteName = json.Substring(start, end - start);

                        if (selectedRuntimeConfig != null)
                        {
                            selectedRuntimeConfig.worldDisplayName = remoteName;
                            lastSyncedName = remoteName; // Mark as in-sync
                            EditorUtility.SetDirty(selectedRuntimeConfig);
                            Repaint();
                            Debug.Log($"[MetaDyn] Dashboard data loaded: {remoteName}");
                        }
                    }
                }
            }
        }

        private async void PerformNameUpdate(string spaceId, string newName)
        {
            if (supabaseConfig == null || string.IsNullOrEmpty(spaceId)) return;

            string url = $"{supabaseConfig.SupabaseUrl}/rest/v1/spaces?id=eq.{spaceId}";
            string jsonBody = $"{{\"name\":\"{newName}\"}}";
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[MetaDyn] Syncing to: {url}");
            Debug.Log($"[MetaDyn] Payload: {jsonBody}");

            using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(url, "PATCH"))
            {
                request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", supabaseConfig.AnonKey);

                // Administrative Writes strictly require the Developer Token
                if (string.IsNullOrEmpty(authToken))
                {
                    Debug.LogError("[MetaDyn] Cannot sync to Dashboard: Developer Token is missing! Please enter your token in the Deployment Center.");
                    return;
                }

                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

                request.SetRequestHeader("Prefer", "return=representation"); // Request the updated row back

                var operation = request.SendWebRequest();
                while (!operation.isDone) await System.Threading.Tasks.Task.Yield();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    string response = request.downloadHandler.text;

                    if (string.IsNullOrEmpty(response) || response == "[]")
                    {
                        Debug.LogError("[MetaDyn] Sync reported success but returned NO data.");
                        Debug.LogError("Likely Cause: Row Level Security (RLS) blocked the update because you are using the Anon key.");
                        Debug.LogError("Fix: Enable Editor Authentication or check Supabase RLS policies.");
                    }
                    else
                    {
                        lastSyncedName = newName; // Reset dirty state
                        Debug.Log($"[MetaDyn] Successfully pushed name to Dashboard: {newName}");
                        Debug.Log($"Response: {response}");
                        Repaint();
                    }
                }
                else
                {
                    Debug.LogError($"[MetaDyn] Sync Failed!");
                    Debug.LogError($"Code: {request.responseCode}");
                    Debug.LogError($"Error: {request.error}");
                    Debug.LogError($"Response: {request.downloadHandler.text}");
                }
            }
        }

        private void SyncSpaceName(string spaceId, string localName)
        {
            // Just push local changes since we auto-pull on load
            PerformNameUpdate(spaceId, localName);
        }

        private void LoadRuntimeConfigs()
        {
            runtimeConfigs.Clear();
            string[] guids = AssetDatabase.FindAssets("t:MetaDynRuntimeConfig");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MetaDynRuntimeConfig config = AssetDatabase.LoadAssetAtPath<MetaDynRuntimeConfig>(path);
                if (config != null)
                {
                    runtimeConfigs.Add(config);
                }
            }
        }

        private void LoadServerProfiles()
        {
            serverProfiles.Clear();
            string[] guids = AssetDatabase.FindAssets("t:MetaDynServerProfile");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MetaDynServerProfile profile = AssetDatabase.LoadAssetAtPath<MetaDynServerProfile>(path);
                if (profile != null)
                {
                    serverProfiles.Add(profile);
                }
            }
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                normal = { textColor = new Color(0.2f, 0.6f, 1f) }
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            successStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.2f, 0.8f, 0.2f) },
                fontStyle = FontStyle.Bold
            };

            errorStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.8f, 0.2f, 0.2f) },
                fontStyle = FontStyle.Bold
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitializeStyles();

            MetaDynEditorHeader.DrawHeader("Deployment Center",
                "Configure world settings, build paths, and deploy your space to the MetaDyn platform.");

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // === VALIDATION ===
            MetaDynStyle.DrawSectionHeader("🔍 SDK Readiness Validation");
            MetaDynStyle.BeginSection();
            DrawValidationSection();
            MetaDynStyle.EndSection();

            GUILayout.Space(10);

            // === AUTHENTICATION ===
            MetaDynStyle.DrawSectionHeader("🔑 Developer Authentication");
            MetaDynStyle.BeginSection();
            DrawAuthenticationSection();
            MetaDynStyle.EndSection();

            GUILayout.Space(10);

            // === BUILD CONFIGURATION ===
            MetaDynStyle.DrawSectionHeader("📦 Build Configuration");
            MetaDynStyle.BeginSection();
            DrawBuildConfiguration();
            MetaDynStyle.EndSection();

            GUILayout.Space(10);

            // === SERVER CONFIGURATION ===
            MetaDynStyle.DrawSectionHeader("🌐 Server Configuration");
            MetaDynStyle.BeginSection();
            DrawServerConfiguration();
            MetaDynStyle.EndSection();

            GUILayout.Space(10);

            // === DEPLOYMENT SECTION ===
            MetaDynStyle.DrawSectionHeader("⚡ Deployment");
            MetaDynStyle.BeginSection();
            DrawDeploymentSection();
            MetaDynStyle.EndSection();

            GUILayout.Space(10);

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndScrollView();

            // Footer (outside scroll view)
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label($"SDK v{MetaDynSDK.SDK_VERSION}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(isDeploying ? "⚡ DEPLOYING..." : "✓ Ready", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawValidationSection()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Run Scan", GUILayout.Width(80)))
            {
                RunValidation();
            }
            EditorGUILayout.EndHorizontal();

            if (validationResults.Count == 0)
            {
                EditorGUILayout.LabelField("No validation data. Run scan to check project readiness.", EditorStyles.miniLabel);
            }

            foreach (var result in validationResults)
            {
                EditorGUILayout.BeginHorizontal();

                string icon = "";
                GUIStyle style = EditorStyles.label;
                switch (result.status)
                {
                    case ValidationStatus.Ok:
                        icon = "✓";
                        style = successStyle;
                        break;
                    case ValidationStatus.Warning:
                        icon = "⚠";
                        style = EditorStyles.label;
                        break;
                    case ValidationStatus.Error:
                        icon = "✖";
                        style = errorStyle;
                        break;
                }

                GUILayout.Label(icon, style, GUILayout.Width(15));
                EditorGUILayout.LabelField($"[{result.category}]", EditorStyles.boldLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField(result.message, EditorStyles.wordWrappedLabel);

                if (result.fixAction != null)
                {
                    if (GUILayout.Button("Fix", GUILayout.Width(40)))
                    {
                        result.fixAction.Invoke();
                        RunValidation();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            int errors = validationResults.Count(r => r.status == ValidationStatus.Error);
            if (errors > 0)
            {
                EditorGUILayout.HelpBox($"{errors} critical errors detected. Deployment may fail or result in non-functional builds.", MessageType.Error);
            }
            else if (validationResults.Count > 0)
            {
                EditorGUILayout.HelpBox("SDK configuration looks healthy for deployment.", MessageType.Info);
            }
        }

        private bool isProvisioning = false;

        private void DrawAuthenticationSection()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Auth Token:", GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            authToken = EditorGUILayout.PasswordField(authToken);
            if (EditorGUI.EndChangeCheck())
            {
                MetaDynProvisioningService.AuthToken = authToken;
            }

            if (GUILayout.Button("Paste", GUILayout.Width(50)))
            {
                authToken = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
                MetaDynProvisioningService.AuthToken = authToken;
                GUI.FocusControl(null); // Clear focus to update field
            }

            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                authToken = "";
                MetaDynProvisioningService.AuthToken = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(authToken))
            {
                EditorGUILayout.HelpBox("Paste your Developer Token from the MetaDyn Dashboard to enable SDK features.", MessageType.Warning);
            }
            else
            {
                bool isProvisioned = MetaDynProvisioningService.IsProvisioned;

                EditorGUILayout.BeginHorizontal();
                if (isProvisioned)
                {
                    EditorGUILayout.HelpBox("SDK provisioned and token loaded.", MessageType.Info);
                    if (GUILayout.Button("Verify Token", GUILayout.Width(100)))
                    {
                        VerifyToken();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("SDK not provisioned. Click Provision to link with your project.", MessageType.Warning);
                    using (new EditorGUI.DisabledScope(isProvisioning))
                    {
                        if (GUILayout.Button("Provision SDK", GUILayout.Width(120)))
                        {
                            PerformProvision();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private async void PerformProvision()
        {
            if (string.IsNullOrEmpty(authToken)) return;

            isProvisioning = true;
            Debug.Log("[MetaDyn] Provisioning SDK via Central Registry...");

            bool success = await MetaDynProvisioningService.ProvisionSDK(authToken);
            if (success)
            {
                supabaseConfig = MetaDynProvisioningService.GetSupabaseConfig();
                EditorUtility.DisplayDialog("Provisioning Successful", "SDK has been successfully linked to your project.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Provisioning Failed", "Failed to link SDK. Check the console for details.", "OK");
            }

            isProvisioning = false;
            Repaint();
        }

        private async void VerifyToken()
        {
            if (supabaseConfig == null) return;

            string url = $"{supabaseConfig.SupabaseUrl}/auth/v1/user";
            using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", supabaseConfig.AnonKey);
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await System.Threading.Tasks.Task.Yield();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    if (json.Contains("\"email\":\""))
                    {
                        int start = json.IndexOf("\"email\":\"") + 9;
                        int end = json.IndexOf("\"", start);
                        string email = json.Substring(start, end - start);
                        EditorUtility.DisplayDialog("Token Valid", $"Authenticated as: {email}", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Token Valid", "Token is valid, but email could not be parsed.", "OK");
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Token Invalid", $"Verification failed: {request.error}\n\n{request.downloadHandler.text}", "OK");
                }
            }
        }

        private void DrawBuildConfiguration()
        {
            // Runtime Config Selection
            EditorGUILayout.LabelField("World Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select a Runtime Config that defines the world settings for this build. All players using this build will automatically join the configured world.",
                MessageType.Info
            );

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Runtime Config:", GUILayout.Width(100));

            if (runtimeConfigs.Count == 0)
            {
                GUILayout.Label("No configs found", EditorStyles.miniLabel);
                if (GUILayout.Button("Create New", GUILayout.Width(80)))
                {
                    CreateNewRuntimeConfig();
                }
            }
            else
            {
                int selectedIndex = selectedRuntimeConfig != null ? runtimeConfigs.IndexOf(selectedRuntimeConfig) : -1;
                if (selectedIndex < 0 && runtimeConfigs.Count > 0)
                {
                    selectedIndex = 0;
                    selectedRuntimeConfig = runtimeConfigs[0];
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedRuntimeConfig));
                    EditorPrefs.SetString(SELECTED_RUNTIME_CONFIG_KEY, guid);
                }

                EditorGUI.BeginChangeCheck();
                string[] configNames = runtimeConfigs.Select(c => c.name + " (" + c.roomName + ")").ToArray();
                selectedIndex = EditorGUILayout.Popup(selectedIndex, configNames);

                if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < runtimeConfigs.Count)
                {
                    selectedRuntimeConfig = runtimeConfigs[selectedIndex];
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedRuntimeConfig));
                    EditorPrefs.SetString(SELECTED_RUNTIME_CONFIG_KEY, guid);
                }

                if (GUILayout.Button("Create", GUILayout.Width(50)))
                {
                    CreateNewRuntimeConfig();
                }

                if (GUILayout.Button("⟳", GUILayout.Width(25)))
                {
                    LoadRuntimeConfigs();
                }
            }
            EditorGUILayout.EndHorizontal();

            // Display and edit selected runtime config
            if (selectedRuntimeConfig != null)
            {
                GUILayout.Space(5);

                // VALIDATION: Is this the active config?
                string currentPath = AssetDatabase.GetAssetPath(selectedRuntimeConfig);
                bool isCorrectName = selectedRuntimeConfig.name == "MetaDynRuntimeConfig";
                bool isInResources = currentPath.Contains("/Resources/");
                bool isActive = isCorrectName && isInResources;

                if (!isActive)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.color = new Color(1f, 0.8f, 0.2f);
                    EditorGUILayout.LabelField("⚠ INACTIVE CONFIGURATION", EditorStyles.boldLabel);
                    GUI.color = Color.white;
                    EditorGUILayout.HelpBox("The Runtime (and builds) only load the config named 'MetaDynRuntimeConfig' located in a 'Resources' folder. This asset is currently disconnected.", MessageType.Warning);

                    if (GUILayout.Button("Make Active (Rename & Move to Resources)"))
                    {
                        MakeConfigActive(selectedRuntimeConfig);
                    }
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5);
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // Editable fields that write back to the ScriptableObject
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Space ID (GUID):", GUILayout.Width(100));
                string newSpaceId = EditorGUILayout.TextField(selectedRuntimeConfig.spaceId);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Owner ID (GUID):", GUILayout.Width(100));
                string newOwnerId = EditorGUILayout.TextField(selectedRuntimeConfig.ownerId);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Room Name:", GUILayout.Width(100));
                string newRoomName = EditorGUILayout.TextField(selectedRuntimeConfig.roomName);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Display Name:", GUILayout.Width(100));
                string newDisplayName = EditorGUILayout.TextField(selectedRuntimeConfig.worldDisplayName);

                // Sync Button logic - only enabled if local name != last synced name
                bool isDirty = selectedRuntimeConfig.worldDisplayName != lastSyncedName;
                GUI.enabled = isDirty && !string.IsNullOrEmpty(selectedRuntimeConfig.spaceId) && supabaseConfig != null;
                if (GUILayout.Button("Sync", GUILayout.Width(50), GUILayout.Height(18)))
                {
                    PerformNameUpdate(selectedRuntimeConfig.spaceId, selectedRuntimeConfig.worldDisplayName);
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Max Players:", GUILayout.Width(100));
                int newMaxPlayers = EditorGUILayout.IntSlider(selectedRuntimeConfig.maxPlayers, 1, 50);
                EditorGUILayout.EndHorizontal();

                // Save changes back to ScriptableObject
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(selectedRuntimeConfig, "Edit Config");
                    selectedRuntimeConfig.spaceId = newSpaceId;
                    selectedRuntimeConfig.ownerId = newOwnerId;
                    selectedRuntimeConfig.roomName = newRoomName;
                    selectedRuntimeConfig.worldDisplayName = newDisplayName;
                    selectedRuntimeConfig.maxPlayers = newMaxPlayers;
                    EditorUtility.SetDirty(selectedRuntimeConfig);
                }

                GUILayout.Space(5);

                // Show config asset location
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Asset:", AssetDatabase.GetAssetPath(selectedRuntimeConfig), EditorStyles.miniLabel);
                if (GUILayout.Button("Select", GUILayout.Width(50)))
                {
                    Selection.activeObject = selectedRuntimeConfig;
                    EditorGUIUtility.PingObject(selectedRuntimeConfig);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();

                if (selectedRuntimeConfig.IsValid())
                {
                    if (string.IsNullOrEmpty(selectedRuntimeConfig.spaceId))
                    {
                        EditorGUILayout.HelpBox(
                            "⚠ Space ID (GUID) is missing. Sync with Supabase to establish Source of Truth.",
                            MessageType.Warning
                        );
                    }

                    EditorGUILayout.HelpBox(
                        $"✓ Players will automatically join: \"{selectedRuntimeConfig.roomName}\"",
                        MessageType.Info
                    );
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "⚠ Room Name is required",
                        MessageType.Warning
                    );
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "⚠ No Runtime Config selected. Create or select one to configure world settings.",
                    MessageType.Warning
                );
            }

            GUILayout.Space(10);

            // Build Path
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Build Path:", GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            buildPath = EditorGUILayout.TextField(buildPath);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(BUILD_PATH_KEY, buildPath);
            }
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Build Directory", buildPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    buildPath = path;
                    EditorPrefs.SetString(BUILD_PATH_KEY, buildPath);
                }
            }
            EditorGUILayout.EndHorizontal();

            // Build path status
            if (Directory.Exists(buildPath))
            {
                EditorGUILayout.HelpBox($"✓ Build directory found: {buildPath}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("⚠ Build directory not found. Please build your project first or select the correct path.", MessageType.Warning);
            }

            GUILayout.Space(5);

            // Quick actions
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Default WebGL Path"))
            {
                buildPath = MetaDynDeploymentManager.GetWebGLBuildPath();
                EditorPrefs.SetString(BUILD_PATH_KEY, buildPath);
            }
            if (GUILayout.Button("Open Build Settings"))
            {
                EditorWindow.GetWindow(System.Type.GetType("UnityEditor.BuildPlayerWindow,UnityEditor"));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void MakeConfigActive(MetaDynRuntimeConfig config)
        {
            if (config == null) return;

            string resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            string currentPath = AssetDatabase.GetAssetPath(config);
            string newPath = Path.Combine(resourcesPath, "MetaDynRuntimeConfig.asset");

            // If another one exists at the destination, we should probably delete it or rename it
            if (AssetDatabase.LoadAssetAtPath<MetaDynRuntimeConfig>(newPath) != null && currentPath != newPath)
            {
                if (!EditorUtility.DisplayDialog("Replace Active Config",
                    "An active config already exists at Assets/Resources/MetaDynRuntimeConfig.asset. Do you want to replace it with this one?",
                    "Replace", "Cancel"))
                {
                    return;
                }
                AssetDatabase.DeleteAsset(newPath);
            }

            string error = AssetDatabase.MoveAsset(currentPath, newPath);
            if (string.IsNullOrEmpty(error))
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Update selection
                selectedRuntimeConfig = AssetDatabase.LoadAssetAtPath<MetaDynRuntimeConfig>(newPath);
                string guid = AssetDatabase.AssetPathToGUID(newPath);
                EditorPrefs.SetString(SELECTED_RUNTIME_CONFIG_KEY, guid);

                LoadRuntimeConfigs();
                Repaint();
                Debug.Log($"[MetaDyn] {config.name} is now the active Runtime Config.");
            }
            else
            {
                EditorUtility.DisplayDialog("Move Failed", $"Could not move asset: {error}", "OK");
            }
        }

        private void CreateNewRuntimeConfig()
{
            // Create Resources folder if needed
            string resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Runtime Config",
                "MetaDynRuntimeConfig",
                "asset",
                "Create a new MetaDyn Runtime Config",
                resourcesPath
            );

            if (!string.IsNullOrEmpty(path))
            {
                MetaDynRuntimeConfig newConfig = CreateInstance<MetaDynRuntimeConfig>();
                AssetDatabase.CreateAsset(newConfig, path);
                AssetDatabase.SaveAssets();
                LoadRuntimeConfigs();
                selectedRuntimeConfig = newConfig;

                string guid = AssetDatabase.AssetPathToGUID(path);
                EditorPrefs.SetString(SELECTED_RUNTIME_CONFIG_KEY, guid);

                // Select the new asset in the project
                Selection.activeObject = newConfig;
                EditorGUIUtility.PingObject(newConfig);
            }
        }

        private void DrawServerConfiguration()
        {
            // Server Profile Selection
            EditorGUILayout.BeginHorizontal();
GUILayout.Label("Server Profile:", GUILayout.Width(100));

            if (serverProfiles.Count == 0)
            {
                GUILayout.Label("No profiles found", EditorStyles.miniLabel);
                if (GUILayout.Button("Create New", GUILayout.Width(80)))
                {
                    CreateNewServerProfile();
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                int selectedIndex = selectedProfile != null ? serverProfiles.IndexOf(selectedProfile) : -1;
                if (selectedIndex < 0) selectedIndex = 0;

                selectedIndex = EditorGUILayout.Popup(selectedIndex, serverProfiles.Select(p => p.GetDisplayName()).ToArray());

                if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < serverProfiles.Count)
                {
                    selectedProfile = serverProfiles[selectedIndex];
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedProfile));
                    EditorPrefs.SetString(SELECTED_PROFILE_KEY, guid);
                }

                if (GUILayout.Button("Create New", GUILayout.Width(80)))
                {
                    CreateNewServerProfile();
                }

                if (GUILayout.Button("⟳", GUILayout.Width(30)))
                {
                    LoadServerProfiles();
                }
            }

            EditorGUILayout.EndHorizontal();

            // Display selected profile info
            if (selectedProfile != null)
            {
                GUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // Strategy Selector
                EditorGUI.BeginChangeCheck();
                var strategy = (DeploymentType)EditorGUILayout.EnumPopup("Strategy:", selectedProfile.deploymentType);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(selectedProfile, "Change Deployment Strategy");
                    selectedProfile.deploymentType = strategy;
                    EditorUtility.SetDirty(selectedProfile);
                }

                GUILayout.Space(5);

                if (selectedProfile.deploymentType == DeploymentType.SCP)
                {
                    DrawSCPConfiguration();
                }
                else if (selectedProfile.deploymentType == DeploymentType.GitHub)
                {
                    DrawGitHubConfiguration();
                }
                else if (selectedProfile.deploymentType == DeploymentType.Netlify)
                {
                    DrawNetlifyConfiguration();
                }

                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.HelpBox("Create a server profile to configure deployment settings.", MessageType.Info);
            }
        }

        private void DrawSCPConfiguration()
        {
            EditorGUI.BeginChangeCheck();
            string addr = EditorGUILayout.TextField("Server Address:", selectedProfile.serverAddress);
            string user = EditorGUILayout.TextField("Username:", selectedProfile.username);
            string path = EditorGUILayout.TextField("Remote Path:", selectedProfile.remotePath);
            string url = EditorGUILayout.TextField("Deploy URL:", selectedProfile.deployedURL);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selectedProfile, "Edit SCP Profile");
                selectedProfile.serverAddress = addr;
                selectedProfile.username = user;
                selectedProfile.remotePath = path;
                selectedProfile.deployedURL = url;
                EditorUtility.SetDirty(selectedProfile);
            }

            GUILayout.Space(5);
            if (GUILayout.Button("🔌 Test Connection", GUILayout.Height(25)))
            {
                TestServerConnection();
            }
        }

        private void DrawNetlifyConfiguration()
        {
            EditorGUI.BeginChangeCheck();
            string token = EditorGUILayout.PasswordField("Netlify Token:", selectedProfile.netlifyToken);
            string siteId = EditorGUILayout.TextField("Site ID:", selectedProfile.netlifySiteId);
            string sub = EditorGUILayout.TextField(new GUIContent("Subdomain:", "Desired netlify.app subdomain"), selectedProfile.netlifySubdomain);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selectedProfile, "Edit Netlify Profile");
                selectedProfile.netlifyToken = token;
                selectedProfile.netlifySiteId = siteId;
                selectedProfile.netlifySubdomain = sub;
                EditorUtility.SetDirty(selectedProfile);
            }

            if (string.IsNullOrEmpty(selectedProfile.netlifyToken))
            {
                EditorGUILayout.HelpBox("Enter your Netlify Personal Access Token (from User Settings > Applications).", MessageType.Warning);
            }
            else if (!string.IsNullOrEmpty(selectedProfile.netlifySiteId))
            {
                EditorGUILayout.HelpBox($"✓ Linked to Site ID: {selectedProfile.netlifySiteId}", MessageType.Info);
                EditorGUILayout.LabelField("Live URL:", selectedProfile.deployedURL, EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("A new site will be created on your Netlify account upon first deployment.", MessageType.Info);
            }
        }

        private void DrawGitHubConfiguration()
        {
            EditorGUI.BeginChangeCheck();
            string user = EditorGUILayout.TextField("GH Username:", selectedProfile.githubUsername);
            string repo = EditorGUILayout.TextField(new GUIContent("Repo Name:", "Leave empty to use sanitized Room Name"), selectedProfile.githubRepo);
            string pat = EditorGUILayout.PasswordField("Personal Token:", selectedProfile.githubPAT);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selectedProfile, "Edit GitHub Profile");
                selectedProfile.githubUsername = user;
                selectedProfile.githubRepo = repo;
                selectedProfile.githubPAT = pat;
                EditorUtility.SetDirty(selectedProfile);
            }

            if (string.IsNullOrEmpty(selectedProfile.githubPAT))
            {
                EditorGUILayout.HelpBox("Requires a GitHub Classic PAT with 'repo' scope permissions.", MessageType.Warning);
            }
            else
            {
                string rName = string.IsNullOrEmpty(selectedProfile.githubRepo) ?
                    System.Text.RegularExpressions.Regex.Replace(selectedRuntimeConfig != null ? selectedRuntimeConfig.roomName : "space", "[^a-zA-Z0-9-]", "-").ToLower() :
                    selectedProfile.githubRepo;

                EditorGUILayout.LabelField("Final URL:", $"https://{selectedProfile.githubUsername}.github.io/{rName}/", EditorStyles.miniLabel);
            }
        }

        private void DrawDeploymentSection()
        {
            // Deployment requirements check
            bool canDeploy = selectedRuntimeConfig != null &&
selectedRuntimeConfig.IsValid() &&
                           Directory.Exists(buildPath) &&
                           selectedProfile != null &&
                           selectedProfile.IsValid() &&
                           !isDeploying;

            GUI.enabled = canDeploy;

            // Big Deploy Button
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 40
            };

            string btnLabel = "🚀 DEPLOY";
            if (selectedProfile != null)
            {
                if (selectedProfile.deploymentType == DeploymentType.GitHub) btnLabel = "🚀 DEPLOY TO GITHUB";
                else if (selectedProfile.deploymentType == DeploymentType.Netlify) btnLabel = "🚀 DEPLOY TO NETLIFY";
                else btnLabel = "🚀 DEPLOY TO SERVER";
            }

            if (GUILayout.Button(btnLabel, buttonStyle))
            {
                StartDeployment();
            }

            GUI.enabled = true;

            // Show why deploy is disabled
            if (!canDeploy && !isDeploying)
            {
                GUILayout.Space(5);
                if (selectedRuntimeConfig == null || !selectedRuntimeConfig.IsValid())
                    EditorGUILayout.HelpBox("⚠ Select a valid Runtime Config with room name set", MessageType.Warning);
                if (!Directory.Exists(buildPath))
                    EditorGUILayout.HelpBox("⚠ Build directory not found", MessageType.Warning);
                if (selectedProfile == null || !selectedProfile.IsValid())
                    EditorGUILayout.HelpBox("⚠ Configure a valid deployment profile", MessageType.Warning);
            }
        }

        private void CreateNewServerProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Server Profile",
                "NewServerProfile",
                "asset",
                "Create a new MetaDyn server profile"
            );

            if (!string.IsNullOrEmpty(path))
            {
                MetaDynServerProfile newProfile = CreateInstance<MetaDynServerProfile>();
                AssetDatabase.CreateAsset(newProfile, path);
                AssetDatabase.SaveAssets();
                LoadServerProfiles();
                selectedProfile = newProfile;

                // Select the new asset in the project
                Selection.activeObject = newProfile;
                EditorGUIUtility.PingObject(newProfile);
            }
        }

        private void TestServerConnection()
        {
            if (selectedProfile == null) return;

            bool cancelled = false;
            bool completed = false;
            bool success = false;
            string message = "";

            // Show cancellable progress bar
            System.Threading.Thread testThread = new System.Threading.Thread(() =>
            {
                success = MetaDynDeploymentManager.TestConnection(selectedProfile, out message);
                completed = true;
            });

            testThread.Start();

            // Wait with progress bar using actual profile timeout
            float elapsed = 0f;
            int timeout = selectedProfile.connectionTimeout;

            while (!completed && elapsed < timeout && !cancelled)
            {
                cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "Testing Connection",
                    $"Connecting to {selectedProfile.serverAddress}...\n(Timeout: {timeout}s, Elapsed: {Mathf.RoundToInt(elapsed)}s)",
                    elapsed / timeout
                );

                System.Threading.Thread.Sleep(100);
                elapsed += 0.1f;
            }

            EditorUtility.ClearProgressBar();

            if (cancelled)
            {
                EditorUtility.DisplayDialog("Connection Test", "Connection test cancelled by user.", "OK");
                return;
            }

            if (!completed)
            {
                EditorUtility.DisplayDialog(
                    "Connection Timeout",
                    $"Connection test timed out after {timeout} seconds.\n\nCheck your server address and firewall settings.",
                    "OK"
                );
                return;
            }

            EditorUtility.DisplayDialog(
                success ? "✅ Connection Successful" : "❌ Connection Failed",
                message,
                "OK"
            );
        }

        /// <summary>
        /// Update the selected runtime configuration before deployment
        /// </summary>
        private void UpdateRuntimeConfig()
        {
            if (selectedRuntimeConfig == null)
            {
                Debug.LogError("[MetaDyn] No Runtime Config selected!");
                return;
            }

            // Ensure it's active before we try to deploy
            string currentPath = AssetDatabase.GetAssetPath(selectedRuntimeConfig);
            if (selectedRuntimeConfig.name != "MetaDynRuntimeConfig" || !currentPath.Contains("/Resources/"))
            {
                if (EditorUtility.DisplayDialog("Inactive Config",
                    $"The selected config '{selectedRuntimeConfig.name}' is not currently set as the active runtime config. You should make it active before deploying so the build uses this data.",
                    "Make Active & Continue", "Cancel Deployment"))
                {
                    MakeConfigActive(selectedRuntimeConfig);
                }
                else
                {
                    return;
                }
            }

            // Update build metadata on the selected config
            selectedRuntimeConfig.buildVersion = Application.version;
            selectedRuntimeConfig.buildTimestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (selectedProfile != null)
            {
                if (selectedProfile.deploymentType == DeploymentType.GitHub)
                {
                    string repoName = string.IsNullOrEmpty(selectedProfile.githubRepo) ?
                        System.Text.RegularExpressions.Regex.Replace(selectedRuntimeConfig.roomName, "[^a-zA-Z0-9-]", "-").ToLower() :
                        selectedProfile.githubRepo;
                    selectedRuntimeConfig.deploymentURL = $"https://{selectedProfile.githubUsername}.github.io/{repoName}/";
                }
                else if (selectedProfile.deploymentType == DeploymentType.Netlify && !string.IsNullOrEmpty(selectedProfile.deployedURL))
                {
                    selectedRuntimeConfig.deploymentURL = selectedProfile.deployedURL;
                }
                else
                {
                    selectedRuntimeConfig.deploymentURL = selectedProfile.deployedURL;
                }
            }

            // Save changes
            EditorUtility.SetDirty(selectedRuntimeConfig);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MetaDyn] Using Runtime Config: {selectedRuntimeConfig.name}");
            Debug.Log($"  - Room Name: {selectedRuntimeConfig.roomName}");
            Debug.Log($"  - Display Name: {selectedRuntimeConfig.worldDisplayName}");
            Debug.Log($"  - Max Players: {selectedRuntimeConfig.maxPlayers}");
            Debug.Log($"  - Build Version: {selectedRuntimeConfig.buildVersion}");
            Debug.Log($"  - Timestamp: {selectedRuntimeConfig.buildTimestamp}");

            if (!string.IsNullOrEmpty(selectedRuntimeConfig.deploymentURL))
            {
                Debug.Log($"  - Deployment URL: {selectedRuntimeConfig.deploymentURL}");
            }
        }

        private void StartDeployment()
        {
            // Update runtime config before deployment
            UpdateRuntimeConfig();

            isDeploying = true;
            string title = "Space Deployment";
            string status = $"Deploying to {selectedProfile.deploymentType}";

            deployProgressWindow = MetaDynRegistryProgressWindow.ShowProgress(title, status);
            deployProgressWindow.UpdateStatus("Initializing...", 0.05f);

            if (selectedProfile.deploymentType == DeploymentType.Netlify)
            {
                MetaDynNetlifyService.DeployToNetlify(
                    buildPath,
                    selectedProfile,
                    OnDeployProgress,
                    OnNetlifyDeployComplete
                );
            }
            else if (selectedProfile.deploymentType == DeploymentType.GitHub)
            {
                MetaDynGitHubService.DeployToGitHub(
                    buildPath,
                    selectedProfile,
                    selectedRuntimeConfig.roomName,
                    OnDeployProgress,
                    OnDeployComplete
                );
            }
            else
            {
                // Create a dynamic profile clone for this specific deployment
                MetaDynServerProfile dynamicProfile = Instantiate(selectedProfile);

                // Construct URL-safe subfolder: "RoomName-SpaceID"
                string safeRoomName = System.Text.RegularExpressions.Regex.Replace(selectedRuntimeConfig.roomName, "[^a-zA-Z0-9]", "-");
                string subFolder = $"{safeRoomName}-{selectedRuntimeConfig.spaceId}";

                // Append to remote path (ensure trailing slash)
                string basePath = dynamicProfile.remotePath.TrimEnd('/');
                dynamicProfile.remotePath = $"{basePath}/{subFolder}/";

                // Update deployed URL to match
                string baseUrl = dynamicProfile.deployedURL.TrimEnd('/');
                dynamicProfile.deployedURL = $"{baseUrl}/{subFolder}/";

                Debug.Log($"[MetaDyn] Dynamic Deployment Path: {dynamicProfile.remotePath}");
                Debug.Log($"[MetaDyn] Dynamic URL: {dynamicProfile.deployedURL}");

                // Start deployment in background with the dynamic profile
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    MetaDynDeploymentManager.DeployToServer(
                        buildPath,
                        dynamicProfile, // Use the clone
                        OnDeployProgress,
                        OnDeployComplete
                    );
                });
            }
        }

        private void OnNetlifyDeployComplete(bool success, string message, string siteId, string url)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (success && selectedProfile != null)
                {
                    Undo.RecordObject(selectedProfile, "Update Netlify Site Info");
                    selectedProfile.netlifySiteId = siteId;
                    selectedProfile.deployedURL = url;
                    EditorUtility.SetDirty(selectedProfile);

                    // Update runtime config with final URL
                    UpdateRuntimeConfig();
                }

                OnDeployComplete(success, message);
            };
        }

        private void OnDeployProgress(float progress, string message)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (deployProgressWindow != null)
                {
                    deployProgressWindow.UpdateStatus(message, progress);
                }
                Repaint();
            };
        }

        private void OnDeployComplete(bool success, string message)
        {
            // Use delayCall to ensure we're on the main thread
            UnityEditor.EditorApplication.delayCall += () =>
            {
                isDeploying = false;

                if (deployProgressWindow != null)
                {
                    if (success)
                    {
                        deployProgressWindow.SetSuccess(message);
                        Debug.Log($"[MetaDyn] ✅ {message}");
                    }
                    else
                    {
                        deployProgressWindow.SetError(message);
                        Debug.LogError($"[MetaDyn] ❌ {message}");
                    }
                }

                Repaint();
            };
        }
    }
}
