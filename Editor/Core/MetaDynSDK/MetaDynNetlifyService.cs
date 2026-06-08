using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MetaDyn
{
    /// <summary>
    /// Service for deploying WebGL builds to Netlify using their direct ZIP upload API.
    /// This supports builds of any size (bypassing the 100MB GitHub REST API limit).
    /// </summary>
    public static class MetaDynNetlifyService
    {
        private const string NETLIFY_API = "https://api.netlify.com/api/v1";

        public delegate void ProgressCallback(float progress, string message);
        public delegate void CompletionCallback(bool success, string message, string siteId = null, string url = null);

        /// <summary>
        /// Compresses the build directory and uploads it directly to Netlify.
        /// </summary>
        public static async void DeployToNetlify(
            string localPath,
            MetaDynServerProfile profile,
            ProgressCallback onProgress = null,
            CompletionCallback onComplete = null)
        {
            string tempZip = "";
            try
            {
                onProgress?.Invoke(0.1f, "Preparing deployment...");

                // 1. Ensure site exists or create it
                string siteId = profile.netlifySiteId;
                if (string.IsNullOrEmpty(siteId))
                {
                    onProgress?.Invoke(0.2f, "Creating new Netlify site...");
                    siteId = await CreateSite(profile);
                    if (string.IsNullOrEmpty(siteId)) throw new Exception("Failed to create Netlify site.");
                }
                else if (!string.IsNullOrEmpty(profile.netlifySubdomain) &&
                         (string.IsNullOrEmpty(profile.deployedURL) || !profile.deployedURL.Contains(profile.netlifySubdomain)))
                {
                    // If the site exists but the URL doesn't match the desired name, try to rename it again
                    onProgress?.Invoke(0.22f, "Updating site subdomain...");
                    await RenameSite(profile, siteId);
                }

                // 1.5. Ensure configuration files exist for compression support
                onProgress?.Invoke(0.25f, "Configuring server headers for compression...");
                EnsureNetlifyConfiguration(localPath);

                // 2. Zip the folder
                onProgress?.Invoke(0.3f, "Compressing build (this may take a moment for large files)...");
                tempZip = Path.Combine(Path.GetTempPath(), $"metadyn_deploy_{Guid.NewGuid():N}.zip");

                if (File.Exists(tempZip)) File.Delete(tempZip);

                // Zip creation - use Task.Run to keep UI responsive
                await Task.Run(() => ZipFile.CreateFromDirectory(localPath, tempZip));

                FileInfo zipInfo = new FileInfo(tempZip);
                onProgress?.Invoke(0.5f, $"Uploading build ({zipInfo.Length / (1024f * 1024f):F1} MB)...");

                // 3. Upload Zip
                string deployUrl = await UploadZip(profile, siteId, tempZip, onProgress);

                // 4. Cleanup
                if (File.Exists(tempZip)) File.Delete(tempZip);

                onComplete?.Invoke(true, $"Successfully deployed to Netlify!\n\nURL: {deployUrl}", siteId, deployUrl);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn Netlify] Deployment failed: {e.Message}");
                if (!string.IsNullOrEmpty(tempZip) && File.Exists(tempZip)) File.Delete(tempZip);
                onComplete?.Invoke(false, $"Netlify Deployment Error: {e.Message}", null, null);
            }
        }

        private static void EnsureNetlifyConfiguration(string buildPath)
        {
            // We generate both _headers and netlify.toml for maximum compatibility across Netlify's deployment engines
            // These rules specifically target Unity 6 WebGL build output patterns, including spaces and nested paths.

            string headersPath = Path.Combine(buildPath, "_headers");
            string tomlPath = Path.Combine(buildPath, "netlify.toml");

            var utf8NoBOM = new UTF8Encoding(false);

            // 1. Generate _headers
            string headersContent = @"# Netlify Headers for Unity WebGL Compression Support
# Matches both direct and nested Build paths
/Build/*.br
  Content-Encoding: br
/Build/*.js.br
  Content-Type: application/javascript
/Build/*.wasm.br
  Content-Type: application/wasm
/Build/*.data.br
  Content-Type: application/octet-stream
/Build/*.framework.js.br
  Content-Type: application/javascript

/Build/*.gz
  Content-Encoding: gzip
/Build/*.js.gz
  Content-Type: application/javascript
/Build/*.wasm.gz
  Content-Type: application/wasm
/Build/*.data.gz
  Content-Type: application/octet-stream
/Build/*.framework.js.gz
  Content-Type: application/javascript
";

            // 2. Generate netlify.toml (Backup configuration)
            string tomlContent = @"# Netlify Configuration for Unity WebGL
# Provides header rules that support splats for Unity 6 builds

[[headers]]
  for = ""/Build/*.br""
  [headers.values]
    Content-Encoding = ""br""

[[headers]]
  for = ""/Build/*.js.br""
  [headers.values]
    Content-Type = ""application/javascript""

[[headers]]
  for = ""/Build/*.wasm.br""
  [headers.values]
    Content-Type = ""application/wasm""

[[headers]]
  for = ""/Build/*.data.br""
  [headers.values]
    Content-Type = ""application/octet-stream""

[[headers]]
  for = ""/Build/*.gz""
  [headers.values]
    Content-Encoding = ""gzip""

[[headers]]
  for = ""/Build/*.js.gz""
  [headers.values]
    Content-Type = ""application/javascript""

[[headers]]
  for = ""/Build/*.wasm.gz""
  [headers.values]
    Content-Type = ""application/wasm""

[[headers]]
  for = ""/Build/*.data.gz""
  [headers.values]
    Content-Type = ""application/octet-stream""
";

            try
            {
                File.WriteAllText(headersPath, headersContent, utf8NoBOM);
                File.WriteAllText(tomlPath, tomlContent, utf8NoBOM);
                Debug.Log("[MetaDyn Netlify] Generated _headers and netlify.toml for compression support.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaDyn Netlify] Failed to create configuration files: {e.Message}. Deployment will continue but may fail to load in browser.");
            }
        }

        private static async Task<string> CreateSite(MetaDynServerProfile p)
        {
            // Step 1: Create a site with a random name (guaranteed success)
            byte[] body = Encoding.UTF8.GetBytes("{}");
            string siteId = null;

            using (UnityWebRequest request = new UnityWebRequest($"{NETLIFY_API}/sites", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, p.netlifyToken);
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to create site: {request.error}\n{request.downloadHandler.text}");
                }

                string resp = request.downloadHandler.text;
                var match = System.Text.RegularExpressions.Regex.Match(resp, "\"id\":\"(.*?)\"");
                siteId = match.Success ? match.Groups[1].Value : null;
            }

            if (string.IsNullOrEmpty(siteId)) return null;

            // Step 2: Try to rename the site
            await RenameSite(p, siteId);

            return siteId;
        }

        private static async Task RenameSite(MetaDynServerProfile p, string siteId)
        {
            if (string.IsNullOrEmpty(p.netlifySubdomain)) return;

            // Step A: Try the requested name
            bool success = await AttemptRename(p, siteId, p.netlifySubdomain);

            // Step B: If it failed with 422 (likely brand filter), try with a 'space-' prefix
            if (!success && p.netlifySubdomain.StartsWith("meta", StringComparison.OrdinalIgnoreCase))
            {
                string fallbackName = $"space-{p.netlifySubdomain}";
                Debug.Log($"[MetaDyn Netlify] Requested name '{p.netlifySubdomain}' was rejected. Retrying with 'space-' prefix: '{fallbackName}'...");
                await AttemptRename(p, siteId, fallbackName);
            }
        }

        private static async Task<bool> AttemptRename(MetaDynServerProfile p, string siteId, string targetName)
        {
            string patchJson = $"{{\"name\":\"{targetName}\"}}";
            byte[] patchBody = Encoding.UTF8.GetBytes(patchJson);

            using (UnityWebRequest request = new UnityWebRequest($"{NETLIFY_API}/sites/{siteId}", "PATCH"))
            {
                request.uploadHandler = new UploadHandlerRaw(patchBody);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, p.netlifyToken);
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[MetaDyn Netlify] Successfully set custom subdomain: {targetName}");
                    return true;
                }

                string errorDetail = request.downloadHandler.text;
                Debug.LogWarning($"[MetaDyn Netlify] Rename to '{targetName}' failed: {request.error}\nResponse: {errorDetail}");
                return false;
            }
        }

        private static async Task<string> UploadZip(MetaDynServerProfile p, string siteId, string zipPath, ProgressCallback onProgress)
        {
            byte[] zipData = File.ReadAllBytes(zipPath);

            using (UnityWebRequest request = new UnityWebRequest($"{NETLIFY_API}/sites/{siteId}/deploys", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(zipData);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/zip");
                SetHeaders(request, p.netlifyToken);

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    onProgress?.Invoke(0.5f + (operation.progress * 0.45f), $"Uploading build... {operation.progress * 100:F0}%");
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Upload failed: {request.error}\n{request.downloadHandler.text}");
                }

                string resp = request.downloadHandler.text;
                // Get the final URL from the response
                var match = System.Text.RegularExpressions.Regex.Match(resp, "\"ssl_url\":\"(.*?)\"");
                if (!match.Success) match = System.Text.RegularExpressions.Regex.Match(resp, "\"url\":\"(.*?)\"");

                return match.Success ? match.Groups[1].Value : "https://app.netlify.com";
            }
        }

        private static void SetHeaders(UnityWebRequest r, string token)
        {
            r.SetRequestHeader("Authorization", $"Bearer {token}");
            r.SetRequestHeader("User-Agent", "MetaDyn-Unity-SDK");
        }
    }
}
