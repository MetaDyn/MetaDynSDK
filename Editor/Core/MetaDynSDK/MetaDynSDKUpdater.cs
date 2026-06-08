using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MetaDyn
{
    /// <summary>
    /// Handles updating the MetaDyn SDK by downloading and extracting a zip from GitHub.
    /// </summary>
    public static class MetaDynSDKUpdater
    {
        private const string SDK_TEMP_FOLDER = "Temp/MetaDynUpdate";

        public static async void UpdateSDK(string version)
        {
            string downloadUrl = $"https://github.com/MetaDyn/MetaDynSDK/archive/refs/tags/v{version}.zip";
            string zipPath = Path.Combine(SDK_TEMP_FOLDER, "sdk_update.zip");
            string extractPath = Path.Combine(SDK_TEMP_FOLDER, "extracted");

            try
            {
                if (Directory.Exists(SDK_TEMP_FOLDER)) Directory.Delete(SDK_TEMP_FOLDER, true);
                Directory.CreateDirectory(SDK_TEMP_FOLDER);

                EditorUtility.DisplayProgressBar("MetaDyn SDK Update", $"Downloading v{version}...", 0.2f);
                
                using (UnityWebRequest request = UnityWebRequest.Get(downloadUrl))
                {
                    request.downloadHandler = new DownloadHandlerFile(zipPath);
                    var operation = request.SendWebRequest();
                    
                    while (!operation.isDone)
                    {
                        EditorUtility.DisplayProgressBar("MetaDyn SDK Update", $"Downloading v{version} ({Mathf.RoundToInt(operation.progress * 100)}%)...", 0.2f + (operation.progress * 0.5f));
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        throw new Exception($"Download failed: {request.error}");
                    }
                }

                EditorUtility.DisplayProgressBar("MetaDyn SDK Update", "Extracting files...", 0.8f);
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // GitHub package archives extract to MetaDynSDK-x.y.z/. Older archives may wrap Assets/MetaDyn.
                string repoRoot = Directory.GetDirectories(extractPath)[0];
                string sourceMetaDyn = Path.Combine(repoRoot, "Assets/MetaDyn");
                string targetMetaDyn = "Assets/MetaDyn";

                if (!Directory.Exists(sourceMetaDyn))
                {
                    sourceMetaDyn = repoRoot;
                    if (!Directory.Exists(sourceMetaDyn)) throw new Exception("Could not find SDK source in the downloaded archive.");
                }

                EditorUtility.DisplayProgressBar("MetaDyn SDK Update", "Replacing SDK files...", 0.9f);
                
                // Replace Files (Professional implementation would use AssetDatabase for better stability)
                if (Directory.Exists(targetMetaDyn)) Directory.Delete(targetMetaDyn, true);
                
                // Note: Direct IO Move might cause Unity to lose track of some meta files temporarily,
                // but for a full SDK replace it is the most reliable way to ensure a clean state.
                CopyDirectory(sourceMetaDyn, targetMetaDyn);

                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("SDK Updated", $"Successfully updated to v{version}. Unity will now recompile.", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn] SDK Update Failed: {e.Message}");
                EditorUtility.DisplayDialog("Update Error", $"An error occurred during update: {e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (Directory.Exists(SDK_TEMP_FOLDER)) Directory.Delete(SDK_TEMP_FOLDER, true);
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, targetSubDir);
            }
        }
    }
}
