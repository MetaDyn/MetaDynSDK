using System;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using UnityEngine.Networking;
using MetaDyn.Dashboard;

namespace MetaDyn.Editor
{
    public class MetaDynItemBundler : EditorWindow
    {
        private const string AUTH_TOKEN_KEY = "MetaDyn_AuthToken";

        [MenuItem("Tools/MetaDyn/Product Bundler")]
        public static void ShowWindow() => GetWindow<MetaDynItemBundler>("Item Bundler");

        public GameObject sourcePrefab;
        public string category = "Items";
        public bool exportGlb = true;
        public bool uploadToSupabase = true;
        
        private string _authToken = "";
        private Vector2 scrollPos;

        private void OnEnable()
        {
            _authToken = EditorPrefs.GetString(AUTH_TOKEN_KEY, "");
        }

        private void OnGUI()
        {
            MetaDynEditorHeader.DrawHeader("Item Bundler", "Organize a prefab and its dependencies into a clean Inventory bundle and generate a platform manifest.");

            // MetaDyn auto-height layout: Everything inside the ScrollView with FlexibleSpace before action buttons.
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginVertical(GUILayout.MinHeight(position.height - 100));
            
            // 1. Bundle Settings
            MetaDynStyle.DrawSectionHeader("Bundle Settings");
            MetaDynStyle.BeginSection();
            sourcePrefab = (GameObject)EditorGUILayout.ObjectField("Target Prefab", sourcePrefab, typeof(GameObject), false);
            category = EditorGUILayout.TextField("Category Folder", category);
            exportGlb = EditorGUILayout.Toggle("Export to GLB (Web Ready)", exportGlb);
            uploadToSupabase = EditorGUILayout.Toggle("Auto-Upload with Build", uploadToSupabase);
            MetaDynStyle.EndSection();

            // Refresh token from shared settings
            _authToken = EditorPrefs.GetString(AUTH_TOKEN_KEY, "").Trim();

            if (string.IsNullOrEmpty(_authToken))
            {
                EditorGUILayout.HelpBox("Cloud registry features are disabled. Please add your API Token in the MetaDyn Project Config window.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("✓ API Token Loaded", MessageType.None);
            }

            if (sourcePrefab != null)
            {
                var metadata = sourcePrefab.GetComponent<MetaDynItemMetadata>();
                if (metadata == null)
                {
                    EditorGUILayout.HelpBox("Warning: No MetaDynItemMetadata component found. One will be added during bundling.", MessageType.Warning);
                }
                else
                {
                    MetaDynStyle.DrawSectionHeader("Item Identity");
                    MetaDynStyle.BeginSection();
                    EditorGUILayout.LabelField("Display Name:", metadata.displayName);
                    EditorGUILayout.LabelField("Item ID:", metadata.itemId);
                    EditorGUILayout.LabelField("Category:", metadata.category);
                    
                    if (GUILayout.Button("Regenerate Item ID", GUILayout.Width(150)))
                    {
                        Undo.RecordObject(metadata, "Regenerate Item ID");
                        metadata.GenerateNewId();
                        EditorUtility.SetDirty(metadata);
                        Debug.Log($"[MetaDyn] Regenerated ID for {metadata.displayName}: {metadata.itemId}");
                    }
                    EditorGUILayout.HelpBox("If you get a 'Row Level Security' error during upload, it usually means this ID is already taken by another user. Try regenerating the ID.", MessageType.None);
                    MetaDynStyle.EndSection();
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(20);

            EditorGUI.BeginDisabledGroup(sourcePrefab == null);
            
            if (GUILayout.Button("🚀 BUILD BUNDLE", GUILayout.Height(40)))
            {
                _ = ExecuteBundle();
            }

            GUILayout.Space(5);

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_authToken));
            if (GUILayout.Button("☁️ UPLOAD TO REGISTRY", GUILayout.Height(40)))
            {
                _ = ExecuteManualUpload();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private async Task ExecuteManualUpload()
        {
            if (sourcePrefab == null) return;
            
            var metadata = sourcePrefab.GetComponent<MetaDynItemMetadata>();
            if (metadata == null)
            {
                EditorUtility.DisplayDialog("Error", "Prefab has no MetaDynItemMetadata. Build the bundle first.", "OK");
                return;
            }

            string itemName = sourcePrefab.name.Replace(" ", "_");
            string rootPath = $"Assets/MetaDyn/Inventory/{category}/{itemName}";
            string glbPath = $"{rootPath}/{itemName}.glb";
            string manifestPath = $"{rootPath}/item_manifest.json";

            if (!File.Exists(glbPath) || !File.Exists(manifestPath))
            {
                EditorUtility.DisplayDialog("Error", "GLB or Manifest missing. Build the bundle first.", "OK");
                return;
            }

            var progress = MetaDynRegistryProgressWindow.ShowProgress("Digital Registry", $"Uploading {itemName}");
            try
            {
                await ExecuteRegistryUploadInternal(glbPath, manifestPath, metadata, progress);
            }
            catch (Exception e)
            {
                progress.SetError(e.Message);
            }
        }

        private async Task ExecuteBundle()
        {
            string oldPath = AssetDatabase.GetAssetPath(sourcePrefab);
            if (string.IsNullOrEmpty(oldPath))
            {
                EditorUtility.DisplayDialog("Error", "Target Prefab must be an asset from the Project window.", "OK");
                return;
            }

            string itemName = sourcePrefab.name.Replace(" ", "_");
            string rootPath = $"Assets/MetaDyn/Inventory/{category}/{itemName}";
            string newPrefabPath = $"{rootPath}/{sourcePrefab.name}.prefab";
            string glbFileName = $"{itemName}.glb";
            string glbPath = $"{rootPath}/{glbFileName}";
            string manifestPath = $"{rootPath}/item_manifest.json";

            var progress = MetaDynRegistryProgressWindow.ShowProgress("Digital Registry", $"Bundling {itemName}");

            try
            {
                // 1. Create Directory Structure safely
                progress.UpdateStatus("Organizing folder structure...", 0.1f);
                EnsureFolder(rootPath);
                if (!AssetDatabase.IsValidFolder($"{rootPath}/Materials")) AssetDatabase.CreateFolder(rootPath, "Materials");
                if (!AssetDatabase.IsValidFolder($"{rootPath}/Sprites")) AssetDatabase.CreateFolder(rootPath, "Sprites");
                AssetDatabase.Refresh();

                // 2. Copy Prefab (instead of moving)
                if (oldPath != newPrefabPath)
                {
                    progress.UpdateStatus("Copying prefab to bundle...", 0.2f);
                    
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(newPrefabPath) != null)
                    {
                        AssetDatabase.DeleteAsset(newPrefabPath);
                    }

                    if (!AssetDatabase.CopyAsset(oldPath, newPrefabPath))
                    {
                        Debug.LogError($"[MetaDyn] Failed to copy prefab to {newPrefabPath}");
                    }
                    AssetDatabase.Refresh();
                    sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(newPrefabPath);
                }

                // 3. Copy Dependencies and Relink
                progress.UpdateStatus("Copying and relinking dependencies...", 0.3f);
                CopyAndRelink(sourcePrefab, rootPath);

                // 4. Add or Update Metadata
                var metadata = sourcePrefab.GetComponent<MetaDynItemMetadata>();
                if (metadata == null) metadata = sourcePrefab.AddComponent<MetaDynItemMetadata>();
                
                if (string.IsNullOrEmpty(metadata.itemId)) metadata.GenerateNewId();
                if (string.IsNullOrEmpty(metadata.displayName)) metadata.displayName = sourcePrefab.name;
                metadata.category = category;

                // 5. Export to GLB if requested
                string glbUrlPlaceholder = "";
                if (exportGlb)
                {
                    progress.UpdateStatus("Exporting GLB for dashboard...", 0.5f);
                    bool success = await ExportToGlb(sourcePrefab, glbPath);
                    if (success)
                    {
                        Debug.Log($"[MetaDyn] Exported GLB to: {glbPath}");
                        glbUrlPlaceholder = glbFileName;
                    }
                }

                // 6. Generate Manifest JSON
                progress.UpdateStatus("Writing item manifest...", 0.7f);
                var manifestData = new ManifestData(metadata);
                manifestData.glb_url = glbUrlPlaceholder;
                
                string json = JsonUtility.ToJson(manifestData, true);
                File.WriteAllText(manifestPath, json);
                AssetDatabase.ImportAsset(manifestPath);

                // 7. Link Manifest
                metadata.manifestJson = AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath);
                
                EditorUtility.SetDirty(sourcePrefab);
                AssetDatabase.SaveAssets();

                // 8. Auto-Upload to Registry
                if (uploadToSupabase && !string.IsNullOrEmpty(_authToken))
                {
                    progress.UpdateStatus("Initializing cloud upload...", 0.8f);
                    await ExecuteRegistryUploadInternal(glbPath, manifestPath, metadata, progress);
                }
                else
                {
                    progress.SetSuccess($"Successfully bundled {itemName} locally.");
                }
            }
            catch (Exception e)
            {
                progress.SetError(e.Message);
                Debug.LogError($"[MetaDyn] Bundling failed: {e.Message}");
            }
        }

        private async Task ExecuteRegistryUploadInternal(string localGlbPath, string localManifestPath, MetaDynItemMetadata meta, MetaDynRegistryProgressWindow progress)
        {
            string[] configGuids = AssetDatabase.FindAssets("t:SupabaseConfig");
            if (configGuids.Length == 0)
            {
                throw new Exception("No Cloud Configuration found!");
            }

            var config = AssetDatabase.LoadAssetAtPath<SupabaseConfig>(AssetDatabase.GUIDToAssetPath(configGuids[0]));
            string baseUrl = config.SupabaseUrl;
            string anonKey = config.AnonKey;
            
            // 1. Fetch User UID from token to ensure we have the correct creator ID for RLS
            progress.UpdateStatus("Verifying developer identity...", 0.82f);
            string userUid = await GetUserUid(baseUrl, anonKey, _authToken);
            if (string.IsNullOrEmpty(userUid))
            {
                throw new Exception("Could not verify developer identity. Please ensure your API Token is valid and fresh.");
            }
            
            string relativeFolder = $"{meta.category.ToLower()}/{meta.itemId}";

            // 2. Register Record in Database FIRST
            // Swapping order because Storage RLS often depends on the database record existing 
            // and being owned by the authenticated user.
            progress.UpdateStatus("Registering item in database...", 0.85f);
            await RegisterInDatabase(baseUrl, anonKey, _authToken, meta, relativeFolder, Path.GetFileName(localGlbPath), userUid);

            // 3. Upload Files to Storage
            progress.UpdateStatus("Uploading model asset...", 0.9f);
            await UploadFile(baseUrl, anonKey, _authToken, localGlbPath, $"items/{relativeFolder}/{Path.GetFileName(localGlbPath)}", "model/gltf-binary");

            progress.UpdateStatus("Uploading registration manifest...", 0.95f);
            await UploadFile(baseUrl, anonKey, _authToken, localManifestPath, $"items/{relativeFolder}/item_manifest.json", "application/json");

            Debug.Log($"[MetaDyn] Item fully registered in cloud registry: {meta.displayName} ({meta.itemId})");
            progress.SetSuccess($"Successfully registered {meta.displayName} to the platform registry.");
        }

        private async Task<string> GetUserUid(string baseUrl, string anonKey, string token)
        {
            string url = $"{baseUrl}/auth/v1/user";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", anonKey);
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[MetaDyn] Identity verification failed: {request.error}\n{request.downloadHandler.text}");
                    return null;
                }

                // Extract "id" from the JSON response
                string json = request.downloadHandler.text;
                if (json.Contains("\"id\":\""))
                {
                    int start = json.IndexOf("\"id\":\"") + 6;
                    int end = json.IndexOf("\"", start);
                    return json.Substring(start, end - start);
                }

                return null;
            }
        }

        private async Task RegisterInDatabase(string baseUrl, string anonKey, string token, MetaDynItemMetadata meta, string folder, string glbName, string creatorUid)
        {
            string url = $"{baseUrl}/rest/v1/items";
            
            // Construct Public URLs (Supabase storage public path)
            string publicGlbUrl = $"{baseUrl}/storage/v1/object/public/items/{folder}/{glbName}";
            string publicManifestUrl = $"{baseUrl}/storage/v1/object/public/items/{folder}/item_manifest.json";

            // Prepare JSON payload for the 'items' table
            // We use the verified creatorUid instead of the display name for the 'creator' field to satisfy RLS
            string jsonBody = $"{{" +
                               $"\"id\":\"{meta.itemId}\"," +
                               $"\"name\":\"{meta.displayName}\"," +
                               $"\"category\":\"{meta.category}\"," +
                               $"\"rarity\":\"{meta.rarity.ToString()}\"," +
                               $"\"creator\":\"{creatorUid}\"," + 
                               $"\"glb_url\":\"{publicGlbUrl}\"," +
                               $"\"manifest_url\":\"{publicManifestUrl}\"," +
                               $"\"version\":\"{meta.version}\"" +
                               $"}}";

            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                request.SetRequestHeader("apikey", anonKey);
                request.SetRequestHeader("Authorization", $"Bearer {token}");
                request.SetRequestHeader("Content-Type", "application/json");
                
                // Use 'resolution=merge-duplicates' to perform an UPSERT
                request.SetRequestHeader("Prefer", "resolution=merge-duplicates");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.downloadHandler.text;
                    if (errorMsg.Contains("JWT expired") || errorMsg.Contains("claim timestamp check failed"))
                    {
                        throw new Exception("Cloud Registry session has expired. Please refresh your API token in the MetaDyn Project Config window.");
                    }
                    
                    Debug.LogError($"[MetaDyn] Database registration failed: {request.error}\n{errorMsg}");
                    if (errorMsg.Contains("row-level security") || errorMsg.Contains("violates row-level security"))
                    {
                        Debug.LogError("[MetaDyn] SQL FIX REQUIRED: Run this in Supabase SQL Editor:\n" +
                                       "CREATE POLICY \"Allow auth inserts\" ON public.items FOR INSERT TO authenticated WITH CHECK (auth.uid() = creator);\n" +
                                       "CREATE POLICY \"Allow auth updates\" ON public.items FOR UPDATE TO authenticated USING (auth.uid() = creator);");
                        throw new Exception("Unauthorized: Row Level Security violation on 'items' table. See Console for SQL fix.");
                    }
                    
                    if (errorMsg.Contains("row-level security") || errorMsg.Contains("violates row-level security"))
                    {
                        throw new Exception("Unauthorized: You do not own this Item ID in the cloud registry. This usually happens if the ID was created by another user. Please click 'Regenerate Item ID' in the Bundler UI and try again.");
                    }
                    
                    throw new Exception($"Database registration failed: {request.error}. Ensure the Item ID is not owned by someone else.");
                }
                else
                {
                    Debug.Log($"[MetaDyn] Database record updated for item: {meta.itemId}");
                }
            }
        }

        private async Task UploadFile(string baseUrl, string anonKey, string token, string localPath, string remotePath, string contentType)
        {
            byte[] data = File.ReadAllBytes(localPath);
            string url = $"{baseUrl}/storage/v1/object/{remotePath}";

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(data);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                request.SetRequestHeader("apikey", anonKey);
                request.SetRequestHeader("Authorization", $"Bearer {token}");
                request.SetRequestHeader("Content-Type", contentType);
                request.SetRequestHeader("x-upsert", "true");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.downloadHandler.text;
                    if (errorMsg.Contains("JWT expired") || errorMsg.Contains("claim timestamp check failed"))
                    {
                        throw new Exception("Cloud Registry session has expired. Please refresh your API token in the MetaDyn Project Config window.");
                    }

                    Debug.LogError($"[MetaDyn] Failed to upload {Path.GetFileName(localPath)}: {request.error}\n{errorMsg}");
                    if (errorMsg.Contains("row-level security") || errorMsg.Contains("violates row-level security"))
                    {
                        Debug.LogError("[MetaDyn] SQL FIX REQUIRED: Run this in Supabase SQL Editor:\n" +
                                       "CREATE POLICY \"Allow auth uploads\" ON storage.objects FOR INSERT TO authenticated WITH CHECK (bucket_id = 'items');\n" +
                                       "CREATE POLICY \"Allow auth updates\" ON storage.objects FOR UPDATE TO authenticated USING (bucket_id = 'items');");
                        throw new Exception("Unauthorized: Row Level Security violation on Storage. See Console for SQL fix.");
                    }
                    throw new Exception($"Failed to upload {Path.GetFileName(localPath)}: {request.error}");
                }
                else
                {
                    Debug.Log($"[MetaDyn] Successfully uploaded: {remotePath}");
                }
            }
        }

        private async Task<bool> ExportToGlb(GameObject prefab, string path)
        {
            // GLTFast requires an instance in the scene for export
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            // Purge empty/missing meshes that cause glTFast stream errors (should be below 0 but was 0)
            var meshFilters = instance.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
                {
                    Debug.LogWarning($"[MetaDyn] Export: Removing empty mesh filter on {mf.gameObject.name}");
                    DestroyImmediate(mf);
                }
            }
            var skinnedMeshRenderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh == null || smr.sharedMesh.vertexCount == 0)
                {
                    Debug.LogWarning($"[MetaDyn] Export: Removing empty skinned mesh renderer on {smr.gameObject.name}");
                    DestroyImmediate(smr);
                }
            }

            try
            {
                // We use reflection to access GLTFast.Export since it's not auto-referenced by Assembly-CSharp
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var gltfastExportAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "glTFast.Export");
                if (gltfastExportAssembly == null)
                {
                    Debug.LogError("[MetaDyn] glTFast.Export assembly not found. Ensure the package 'com.unity.cloud.gltfast' is installed.");
                    return false;
                }

                var exportType = gltfastExportAssembly.GetType("GLTFast.Export.GameObjectExport");
                if (exportType == null)
                {
                    Debug.LogError("[MetaDyn] GLTFast.Export.GameObjectExport type not found.");
                    return false;
                }

                // Get required types for settings
                var exportSettingsType = gltfastExportAssembly.GetType("GLTFast.Export.ExportSettings");
                var gameObjectExportSettingsType = gltfastExportAssembly.GetType("GLTFast.Export.GameObjectExportSettings");
                var gltfFormatType = gltfastExportAssembly.GetType("GLTFast.Export.GltfFormat");

                if (exportSettingsType == null || gameObjectExportSettingsType == null || gltfFormatType == null)
                {
                    Debug.LogError("[MetaDyn] Required glTFast settings types not found. Ensure glTFast is up to date.");
                    return false;
                }

                // Instantiate settings
                object exportSettings = Activator.CreateInstance(exportSettingsType);
                object gameObjectExportSettings = Activator.CreateInstance(gameObjectExportSettingsType);

                // Set format to Binary (1)
                var formatProp = exportSettingsType.GetProperty("Format");
                if (formatProp != null)
                {
                    formatProp.SetValue(exportSettings, Enum.Parse(gltfFormatType, "Binary"));
                }

                // GameObjectExport has a constructor with optional parameters.
                var ctor = exportType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor == null)
                {
                    Debug.LogError("[MetaDyn] No constructor found for GLTFast.Export.GameObjectExport.");
                    return false;
                }
                
                // Fill arguments array
                object[] ctorArgs = new object[ctor.GetParameters().Length];
                if (ctorArgs.Length >= 1) ctorArgs[0] = exportSettings;
                if (ctorArgs.Length >= 2) ctorArgs[1] = gameObjectExportSettings;
                
                object exportInstance = ctor.Invoke(ctorArgs);
                
                // AddScene(GameObject[] gameObjects, string name = null)
                var addSceneMethod = exportType.GetMethods()
                    .FirstOrDefault(m => m.Name == "AddScene" && 
                                         m.GetParameters().Length >= 1 && 
                                         m.GetParameters()[0].ParameterType == typeof(GameObject[]));

                if (addSceneMethod == null)
                {
                    Debug.LogError("[MetaDyn] AddScene method not found on GameObjectExport.");
                    return false;
                }

                object[] addSceneArgs = new object[addSceneMethod.GetParameters().Length];
                addSceneArgs[0] = new[] { instance };
                if (addSceneArgs.Length >= 2) addSceneArgs[1] = (string)null;
                
                addSceneMethod.Invoke(exportInstance, addSceneArgs);

                // Task<bool> SaveToFileAndDispose(string path)
                var saveMethod = exportType.GetMethods()
                    .FirstOrDefault(m => m.Name == "SaveToFileAndDispose" && 
                                         m.GetParameters().Length >= 1 && 
                                         m.GetParameters()[0].ParameterType == typeof(string));

                if (saveMethod == null)
                {
                    Debug.LogError("[MetaDyn] SaveToFileAndDispose method not found on GameObjectExport.");
                    return false;
                }

                // Ensure the directory exists before glTFast tries to write to it
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string fullPath = Path.GetFullPath(path).Replace("\\", "/");
                
                object[] saveArgs = new object[saveMethod.GetParameters().Length];
                saveArgs[0] = fullPath;
                if (saveArgs.Length >= 2) saveArgs[1] = default(System.Threading.CancellationToken);
                
                Task<bool> task = (Task<bool>)saveMethod.Invoke(exportInstance, saveArgs);
                return await task;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn] Reflection error during GLB export: {e.Message}");
                return false;
            }
            finally
            {
                if (instance != null) DestroyImmediate(instance);
            }
        }

        private void CopyAndRelink(GameObject prefab, string rootPath)
        {
            string[] dependencyPaths = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab), true);

            // 1. Copy all dependencies to the bundle folder
            foreach (var path in dependencyPaths)
            {
                if (path == AssetDatabase.GetAssetPath(prefab)) continue;
                if (!path.StartsWith("Assets")) continue;
                if (path.StartsWith("Assets/MetaDyn/Core")) continue;
                if (path.Contains(rootPath)) continue;

                var dep = AssetDatabase.LoadMainAssetAtPath(path);
                if (dep == null) continue;

                string fileName = Path.GetFileName(path);
                string destPath = "";
                if (dep is Material)
                    destPath = $"{rootPath}/Materials/{fileName}";
                else if (dep is Texture || dep is Sprite)
                    destPath = $"{rootPath}/Sprites/{fileName}";
                
                if (!string.IsNullOrEmpty(destPath))
                {
                    // Only copy if it doesn't exist in destination
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(destPath) == null)
                    {
                        AssetDatabase.CopyAsset(path, destPath);
                    }
                }
            }
            AssetDatabase.Refresh();

            // 2. Relink Renderers on the Prefab to use the COPIED materials
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    
                    string matName = mats[i].name;
                    string bundleMatPath = $"{rootPath}/Materials/{matName}.mat";
                    Material bundleMat = AssetDatabase.LoadAssetAtPath<Material>(bundleMatPath);
                    
                    if (bundleMat != null && bundleMat != mats[i])
                    {
                        mats[i] = bundleMat;
                        changed = true;
                        
                        // Also ensure this material points to copied textures
                        RelinkMaterial(bundleMat, rootPath);
                    }
                }
                if (changed)
                {
                    renderer.sharedMaterials = mats;
                    EditorUtility.SetDirty(renderer);
                }
            }
            AssetDatabase.SaveAssets();
        }

        private void RelinkMaterial(Material mat, string rootPath)
        {
            Shader shader = mat.shader;
            int count = ShaderUtil.GetPropertyCount(shader);
            bool matChanged = false;

            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propName = ShaderUtil.GetPropertyName(shader, i);
                    Texture tex = mat.GetTexture(propName);
                    if (tex == null) continue;

                    // Try to find if a copy of this texture exists in our bundle Sprites folder
                    string texName = tex.name;
                    string[] guids = AssetDatabase.FindAssets(texName, new[] { $"{rootPath}/Sprites" });
                    
                    foreach (string guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        Texture bundleTex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                        if (bundleTex != null && bundleTex.name == texName && bundleTex != tex)
                        {
                            mat.SetTexture(propName, bundleTex);
                            matChanged = true;
                            break;
                        }
                    }
                }
            }

            if (matChanged)
            {
                EditorUtility.SetDirty(mat);
            }
        }

        private void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path).Replace("\\", "/");
                string folder = Path.GetFileName(path);
                if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        [System.Serializable]
        private class ManifestData
        {
            public string itemId;
            public string displayName;
            public string category;
            public string creator;
            public string license;
            public string version;
            public string glb_url;

            public ManifestData(MetaDynItemMetadata meta)
            {
                itemId = meta.itemId;
                displayName = meta.displayName;
                category = meta.category;
                creator = meta.creatorName;
                license = meta.license.ToString();
                version = meta.version;
                glb_url = "";
            }
        }
    }
}
