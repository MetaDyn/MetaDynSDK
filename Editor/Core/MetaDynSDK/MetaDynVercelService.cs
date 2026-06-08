using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MetaDyn
{
    /// <summary>
    /// Service for deploying WebGL builds to Vercel using uploaded file references.
    /// </summary>
    public static class MetaDynVercelService
    {
        private const string VERCEL_API = "https://api.vercel.com";

        public delegate void ProgressCallback(float progress, string message);
        public delegate void CompletionCallback(bool success, string message, string url = null);

        public static async void DeployToVercel(
            string localPath,
            MetaDynServerProfile profile,
            string roomName,
            ProgressCallback onProgress = null,
            CompletionCallback onComplete = null)
        {
            try
            {
                onProgress?.Invoke(0.1f, "Configuring Vercel WebGL headers...");
                EnsureVercelConfiguration(localPath);

                string projectName = GetProjectName(profile, roomName);
                string[] files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
                var deploymentFiles = new List<VercelFile>(files.Length);

                for (int i = 0; i < files.Length; i++)
                {
                    string path = files[i];
                    string relativePath = path.Substring(localPath.Length).Replace("\\", "/").TrimStart('/');
                    byte[] data = File.ReadAllBytes(path);
                    string sha = GetSha1(data);

                    float progress = 0.15f + (0.65f * (i / (float)Math.Max(files.Length, 1)));
                    onProgress?.Invoke(progress, $"Uploading {relativePath}...");

                    await UploadFile(profile, data, sha);
                    deploymentFiles.Add(new VercelFile(relativePath, sha));
                }

                onProgress?.Invoke(0.85f, "Creating Vercel deployment...");
                string deployUrl = await CreateDeployment(profile, projectName, deploymentFiles);

                onComplete?.Invoke(true, $"Successfully deployed to Vercel!\n\nURL: {deployUrl}", deployUrl);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn Vercel] Deployment failed: {e.Message}");
                onComplete?.Invoke(false, $"Vercel Deployment Error: {e.Message}", null);
            }
        }

        private static void EnsureVercelConfiguration(string buildPath)
        {
            string configPath = Path.Combine(buildPath, "vercel.json");
            var utf8NoBOM = new UTF8Encoding(false);
            string config = @"{
  ""$schema"": ""https://openapi.vercel.sh/vercel.json"",
  ""cleanUrls"": false,
  ""trailingSlash"": false,
  ""headers"": [
    {
      ""source"": ""/Build/(.*).wasm.br"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""br"" },
        { ""key"": ""Content-Type"", ""value"": ""application/wasm"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).framework.js.br"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""br"" },
        { ""key"": ""Content-Type"", ""value"": ""application/javascript"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).js.br"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""br"" },
        { ""key"": ""Content-Type"", ""value"": ""application/javascript"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).data.br"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""br"" },
        { ""key"": ""Content-Type"", ""value"": ""application/octet-stream"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).symbols.json.br"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""br"" },
        { ""key"": ""Content-Type"", ""value"": ""application/json"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).wasm.gz"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""gzip"" },
        { ""key"": ""Content-Type"", ""value"": ""application/wasm"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).framework.js.gz"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""gzip"" },
        { ""key"": ""Content-Type"", ""value"": ""application/javascript"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).js.gz"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""gzip"" },
        { ""key"": ""Content-Type"", ""value"": ""application/javascript"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).data.gz"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""gzip"" },
        { ""key"": ""Content-Type"", ""value"": ""application/octet-stream"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/Build/(.*).symbols.json.gz"",
      ""headers"": [
        { ""key"": ""Content-Encoding"", ""value"": ""gzip"" },
        { ""key"": ""Content-Type"", ""value"": ""application/json"" },
        { ""key"": ""Cache-Control"", ""value"": ""public, max-age=31536000, immutable"" }
      ]
    },
    {
      ""source"": ""/(.*)"",
      ""headers"": [
        { ""key"": ""X-Content-Type-Options"", ""value"": ""nosniff"" }
      ]
    }
  ]
}
";

            try
            {
                File.WriteAllText(configPath, config, utf8NoBOM);
                Debug.Log("[MetaDyn Vercel] Generated vercel.json for Unity WebGL compression support.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaDyn Vercel] Failed to create vercel.json: {e.Message}. Deployment will continue but may fail to load compressed WebGL assets.");
            }
        }

        private static async Task UploadFile(MetaDynServerProfile profile, byte[] data, string sha)
        {
            using (UnityWebRequest request = new UnityWebRequest($"{VERCEL_API}/v2/files{GetTeamQuery(profile)}", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(data);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, profile.vercelToken);
                request.SetRequestHeader("Content-Type", "application/octet-stream");
                request.SetRequestHeader("x-vercel-digest", sha);

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"File upload failed: {request.error}\n{request.downloadHandler.text}");
                }
            }
        }

        private static async Task<string> CreateDeployment(MetaDynServerProfile profile, string projectName, List<VercelFile> files)
        {
            string json = BuildDeploymentJson(profile, projectName, files);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest request = new UnityWebRequest($"{VERCEL_API}/v13/deployments{GetTeamQuery(profile)}", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, profile.vercelToken);
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to create deployment: {request.error}\n{request.downloadHandler.text}");
                }

                string response = request.downloadHandler.text;
                string url = MatchJsonString(response, "url");
                if (string.IsNullOrEmpty(url))
                {
                    return "https://vercel.com";
                }

                return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"https://{url}";
            }
        }

        private static string BuildDeploymentJson(MetaDynServerProfile profile, string projectName, List<VercelFile> files)
        {
            var builder = new StringBuilder();
            builder.Append("{");
            builder.Append("\"name\":\"").Append(EscapeJson(projectName)).Append("\",");
            if (profile.vercelProduction)
            {
                builder.Append("\"target\":\"production\",");
            }
            builder.Append("\"projectSettings\":{\"framework\":null,\"buildCommand\":null,\"devCommand\":null,\"installCommand\":null,\"outputDirectory\":\".\"},");
            builder.Append("\"files\":[");

            for (int i = 0; i < files.Count; i++)
            {
                if (i > 0) builder.Append(",");
                builder.Append("{\"file\":\"")
                    .Append(EscapeJson(files[i].Path))
                    .Append("\",\"sha\":\"")
                    .Append(files[i].Sha)
                    .Append("\"}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static string GetProjectName(MetaDynServerProfile profile, string roomName)
        {
            string rawName = string.IsNullOrEmpty(profile.vercelProjectName) ? roomName : profile.vercelProjectName;
            string sanitized = System.Text.RegularExpressions.Regex.Replace(rawName ?? "metadyn-space", "[^a-zA-Z0-9-]", "-").ToLowerInvariant();
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "-{2,}", "-").Trim('-');
            return string.IsNullOrEmpty(sanitized) ? "metadyn-space" : sanitized;
        }

        private static string GetTeamQuery(MetaDynServerProfile profile)
        {
            return string.IsNullOrEmpty(profile.vercelTeamId) ? "" : $"?teamId={UnityWebRequest.EscapeURL(profile.vercelTeamId)}";
        }

        private static string GetSha1(byte[] data)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(data);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static string MatchJsonString(string json, string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, $"\"{key}\"\\s*:\\s*\"(.*?)\"");
            return match.Success ? match.Groups[1].Value.Replace("\\/", "/") : null;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static void SetHeaders(UnityWebRequest request, string token)
        {
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("User-Agent", "MetaDyn-Unity-SDK");
        }

        private readonly struct VercelFile
        {
            public VercelFile(string path, string sha)
            {
                Path = path;
                Sha = sha;
            }

            public string Path { get; }
            public string Sha { get; }
        }
    }
}
