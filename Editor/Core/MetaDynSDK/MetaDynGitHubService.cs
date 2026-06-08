using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MetaDyn
{
    /// <summary>
    /// Service for deploying WebGL builds to GitHub Pages using the GitHub REST API.
    /// </summary>
    public static class MetaDynGitHubService
    {
        private const string GITHUB_API = "https://api.github.com";
        private const int MAX_RETRY_ATTEMPTS = 4;
        private const long GITHUB_MAX_FILE_SIZE_BYTES = 100L * 1024L * 1024L;

        public delegate void ProgressCallback(float progress, string message);
        public delegate void CompletionCallback(bool success, string message);

        /// <summary>
        /// Deploys a local build directory to a GitHub repository's gh-pages branch.
        /// </summary>
        public static async void DeployToGitHub(
            string localPath,
            MetaDynServerProfile profile,
            string roomName,
            ProgressCallback onProgress = null,
            CompletionCallback onComplete = null)
        {
            try
            {
                // Determine repository name (use profile override or sanitized room name)
                string repoName = string.IsNullOrEmpty(profile.githubRepo) ?
                    System.Text.RegularExpressions.Regex.Replace(roomName, "[^a-zA-Z0-9-]", "-").ToLower() :
                    profile.githubRepo;

                onProgress?.Invoke(0.1f, "Verifying repository...");

                // 1. Ensure repo exists (create if missing)
                if (!await CheckRepoExists(profile, repoName))
                {
                    onProgress?.Invoke(0.15f, $"Creating repository '{repoName}'...");
                    await CreateRepo(profile, repoName);
                }

                // 2. Ensure gh-pages branch exists
                onProgress?.Invoke(0.18f, "Checking branch...");
                await EnsureBranchExists(profile, repoName);

                // 3. Ensure GitHub Pages serves the folder as static files without Jekyll processing
                EnsureGitHubPagesConfiguration(localPath);

                // 4. Create all file blobs first. The live branch is untouched until the final ref update.
                string[] files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
                int totalFiles = files.Length;
                ValidateGitHubPagesFileSizes(files, localPath);
                var treeEntries = new System.Collections.Generic.List<GitTreeEntry>(totalFiles);

                for (int i = 0; i < totalFiles; i++)
                {
                    string filePath = files[i];
                    string relPath = filePath.Substring(localPath.Length).Replace("\\", "/").TrimStart('/');

                    float progress = 0.2f + (0.55f * (i / (float)totalFiles));
                    onProgress?.Invoke(progress, $"Creating blob for {relPath}...");

                    byte[] fileData = File.ReadAllBytes(filePath);
                    string blobSha = await CreateBlob(profile, repoName, relPath, fileData);
                    treeEntries.Add(new GitTreeEntry(relPath, blobSha));
                }

                // 5. Atomically replace gh-pages with one new commit.
                onProgress?.Invoke(0.78f, "Creating deployment tree...");
                string treeSha = await CreateTree(profile, repoName, treeEntries);

                onProgress?.Invoke(0.85f, "Creating deployment commit...");
                string parentSha = await GetBranchHeadSha(profile, repoName, "gh-pages");
                if (string.IsNullOrEmpty(parentSha))
                {
                    throw new Exception("Could not resolve gh-pages head commit for deployment.");
                }

                string commitSha = await CreateCommit(profile, repoName, treeSha, parentSha);

                onProgress?.Invoke(0.9f, "Publishing gh-pages branch...");
                await UpdateReference(profile, repoName, "gh-pages", commitSha);

                // 6. Enable GitHub Pages if not already enabled
                onProgress?.Invoke(0.95f, "Configuring GitHub Pages...");
                await EnablePages(profile, repoName);

                string pagesUrl = $"https://{profile.githubUsername}.github.io/{repoName}/";
                onComplete?.Invoke(true, $"Successfully deployed to GitHub Pages!\n\nURL: {pagesUrl}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDyn GitHub] Deployment failed: {e.Message}");
                onComplete?.Invoke(false, $"GitHub Deployment Error: {e.Message}");
            }
        }

        private static void EnsureGitHubPagesConfiguration(string buildPath)
        {
            string noJekyllPath = Path.Combine(buildPath, ".nojekyll");
            if (!File.Exists(noJekyllPath))
            {
                File.WriteAllText(noJekyllPath, "");
            }
        }

        private static void ValidateGitHubPagesFileSizes(string[] files, string localPath)
        {
            var oversizedFiles = new System.Collections.Generic.List<string>();

            foreach (string file in files)
            {
                var info = new FileInfo(file);
                if (info.Length <= GITHUB_MAX_FILE_SIZE_BYTES) continue;

                string relPath = file.Substring(localPath.Length).Replace("\\", "/").TrimStart('/');
                oversizedFiles.Add($"{relPath} ({FormatBytes(info.Length)})");
            }

            if (oversizedFiles.Count == 0) return;

            string fileList = string.Join("\n", oversizedFiles.ToArray());
            throw new Exception(
                "GitHub Pages deployment cannot continue because GitHub blocks repository files larger than 100 MiB.\n\n" +
                fileList +
                "\n\nUse the Vercel or Netlify deployment strategy for this WebGL build, or reduce/split the Unity build output so every generated file is under 100 MiB."
            );
        }

        private static async Task EnsureBranchExists(MetaDynServerProfile p, string repoName)
        {
            // Check if gh-pages exists
            using (UnityWebRequest request = UnityWebRequest.Get($"{GITHUB_API}/repos/{p.githubUsername}/{repoName}/branches/gh-pages"))
            {
                SetHeaders(request, p.githubPAT);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success) return; // Branch exists
            }

            // If not, get default branch head SHA
            string defaultBranch = await GetDefaultBranch(p, repoName);
            string headSha = await GetBranchHeadSha(p, repoName, defaultBranch);

            if (string.IsNullOrEmpty(headSha))
            {
                throw new Exception($"Could not find base commit for branch creation on '{defaultBranch}'");
            }

            // Create gh-pages branch
            string json = $"{{\"ref\":\"refs/heads/gh-pages\", \"sha\":\"{headSha}\"}}";
            byte[] body = Encoding.UTF8.GetBytes(json);

            await SendGitHubRequestWithRetry(
                () =>
                {
                    var request = new UnityWebRequest($"{GITHUB_API}/repos/{p.githubUsername}/{repoName}/git/refs", "POST");
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    SetHeaders(request, p.githubPAT);
                    return request;
                },
                "Create gh-pages branch",
                repoName
            );
        }

        private static async Task<string> GetDefaultBranch(MetaDynServerProfile p, string repoName)
        {
            using (UnityWebRequest request = UnityWebRequest.Get($"{GITHUB_API}/repos/{p.githubUsername}/{repoName}"))
            {
                SetHeaders(request, p.githubPAT);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    return MatchJsonString(json, "default_branch") ?? "main";
                }
                return "main";
            }
        }

        private static async Task<string> GetBranchHeadSha(MetaDynServerProfile p, string repoName, string branch)
        {
            using (UnityWebRequest request = UnityWebRequest.Get($"{GITHUB_API}/repos/{p.githubUsername}/{repoName}/git/refs/heads/{branch}"))
            {
                SetHeaders(request, p.githubPAT);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    return MatchJsonString(json, "sha");
                }
                return null;
            }
        }

        private static async Task<bool> CheckRepoExists(MetaDynServerProfile p, string repoName)
        {
            using (UnityWebRequest request = UnityWebRequest.Get($"{GITHUB_API}/repos/{p.githubUsername}/{repoName}"))
            {
                SetHeaders(request, p.githubPAT);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                return request.result == UnityWebRequest.Result.Success;
            }
        }

        private static async Task CreateRepo(MetaDynServerProfile p, string name)
        {
            string json = $"{{\"name\":\"{name}\", \"description\":\"MetaDyn Space Deployment\", \"auto_init\":true}}";
            byte[] body = Encoding.UTF8.GetBytes(json);

            await SendGitHubRequestWithRetry(
                () =>
                {
                    var request = new UnityWebRequest($"{GITHUB_API}/user/repos", "POST");
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    SetHeaders(request, p.githubPAT);
                    return request;
                },
                "Create repository",
                name
            );
        }

        private static async Task<string> CreateBlob(MetaDynServerProfile p, string repo, string path, byte[] data)
        {
            string base64Content = Convert.ToBase64String(data);
            string json = $"{{\"content\":\"{base64Content}\",\"encoding\":\"base64\"}}";
            byte[] body = Encoding.UTF8.GetBytes(json);
            string context = $"{path} ({data.Length / (1024f * 1024f):F1} MB)";

            string response = await SendGitHubRequestWithRetry(
                () =>
                {
                    var request = new UnityWebRequest($"{GITHUB_API}/repos/{p.githubUsername}/{repo}/git/blobs", "POST");
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    SetHeaders(request, p.githubPAT);
                    return request;
                },
                "Create Git blob",
                context
            );

            string sha = MatchJsonString(response, "sha");
            if (string.IsNullOrEmpty(sha))
            {
                throw new Exception($"GitHub blob response did not include a SHA for {context}.");
            }

            return sha;
        }

        private static async Task<string> CreateTree(MetaDynServerProfile p, string repo, System.Collections.Generic.List<GitTreeEntry> entries)
        {
            string json = BuildTreeJson(entries);
            byte[] body = Encoding.UTF8.GetBytes(json);

            string response = await SendGitHubRequestWithRetry(
                () =>
                {
                    var request = new UnityWebRequest($"{GITHUB_API}/repos/{p.githubUsername}/{repo}/git/trees", "POST");
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    SetHeaders(request, p.githubPAT);
                    return request;
                },
                "Create Git tree",
                $"{repo} ({entries.Count} files)"
            );

            string sha = MatchJsonString(response, "sha");
            if (string.IsNullOrEmpty(sha))
            {
                throw new Exception("GitHub tree response did not include a SHA.");
            }

            return sha;
        }

        private static async Task<string> CreateCommit(MetaDynServerProfile p, string repo, string treeSha, string parentSha)
        {
            string json = $"{{\"message\":\"Deploy WebGL build via MetaDyn SDK\",\"tree\":\"{treeSha}\",\"parents\":[\"{parentSha}\"]}}";
            byte[] body = Encoding.UTF8.GetBytes(json);

            string response = await SendGitHubRequestWithRetry(
                () =>
                {
                    var request = new UnityWebRequest($"{GITHUB_API}/repos/{p.githubUsername}/{repo}/git/commits", "POST");
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    SetHeaders(request, p.githubPAT);
                    return request;
                },
                "Create Git commit",
                repo
            );

            string sha = MatchJsonString(response, "sha");
            if (string.IsNullOrEmpty(sha))
            {
                throw new Exception("GitHub commit response did not include a SHA.");
            }

            return sha;
        }

        private static async Task UpdateReference(MetaDynServerProfile p, string repo, string branch, string commitSha)
        {
            string json = $"{{\"sha\":\"{commitSha}\",\"force\":false}}";
            byte[] body = Encoding.UTF8.GetBytes(json);

            await SendGitHubRequestWithRetry(
                () =>
                {
                    var request = new UnityWebRequest($"{GITHUB_API}/repos/{p.githubUsername}/{repo}/git/refs/heads/{branch}", "PATCH");
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    SetHeaders(request, p.githubPAT);
                    return request;
                },
                "Publish GitHub Pages ref",
                $"{repo}/{branch}"
            );
        }

        private static async Task EnablePages(MetaDynServerProfile p, string repo)
        {
            // First check if pages already enabled
            using (UnityWebRequest checkReq = UnityWebRequest.Get($"{GITHUB_API}/repos/{p.githubUsername}/{repo}/pages"))
            {
                SetHeaders(checkReq, p.githubPAT);
                var op1 = checkReq.SendWebRequest();
                while (!op1.isDone) await Task.Yield();

                if (checkReq.result == UnityWebRequest.Result.Success) return; // Already enabled
            }

            // Enable pages pointing to gh-pages branch root
            string json = "{\"source\":{\"branch\":\"gh-pages\",\"path\":\"/\"}}";
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest request = new UnityWebRequest($"{GITHUB_API}/repos/{p.githubUsername}/{repo}/pages", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, p.githubPAT);
                request.SetRequestHeader("Accept", "application/vnd.github.switcheroo-preview+json"); // Required for some Pages API endpoints

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                // Note: 201 Created or 204 No Content are success for this endpoint
            }
        }

        private static void SetHeaders(UnityWebRequest r, string pat)
        {
            r.SetRequestHeader("Authorization", $"token {pat}");
            r.SetRequestHeader("User-Agent", "MetaDyn-Unity-SDK");
            r.SetRequestHeader("Accept", "application/vnd.github+json");
            r.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
            r.SetRequestHeader("Content-Type", "application/json");
        }

        private static async Task<string> SendGitHubRequestWithRetry(Func<UnityWebRequest> requestFactory, string operationName, string context)
        {
            string lastError = "";

            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                using (UnityWebRequest request = requestFactory())
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone) await Task.Yield();

                    string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        return responseText;
                    }

                    long code = request.responseCode;
                    lastError = $"HTTP {code} {request.error}\n{responseText}";
                    bool shouldRetry = IsRetryableGitHubError(request);

                    if (shouldRetry && attempt < MAX_RETRY_ATTEMPTS)
                    {
                        int delayMs = GetRetryDelayMilliseconds(attempt);
                        Debug.LogWarning($"[MetaDyn GitHub] {operationName} failed for {context}: {lastError}\nRetrying in {delayMs / 1000f:F1}s ({attempt + 1}/{MAX_RETRY_ATTEMPTS})...");
                        await Task.Delay(delayMs);
                        continue;
                    }

                    throw new Exception($"{operationName} failed for {context}: {lastError}");
                }
            }

            throw new Exception($"{operationName} failed for {context}: {lastError}");
        }

        private static bool IsRetryableGitHubError(UnityWebRequest request)
        {
            long code = request.responseCode;
            return request.result == UnityWebRequest.Result.ConnectionError ||
                   request.result == UnityWebRequest.Result.DataProcessingError ||
                   code == 408 ||
                   code == 429 ||
                   code == 500 ||
                   code == 502 ||
                   code == 503 ||
                   code == 504;
        }

        private static int GetRetryDelayMilliseconds(int attempt)
        {
            switch (attempt)
            {
                case 1: return 2000;
                case 2: return 5000;
                case 3: return 10000;
                default: return 15000;
            }
        }

        private static string FormatBytes(long bytes)
        {
            const double mib = 1024.0 * 1024.0;
            return $"{bytes / mib:F1} MiB";
        }

        private static string BuildTreeJson(System.Collections.Generic.List<GitTreeEntry> entries)
        {
            var builder = new StringBuilder();
            builder.Append("{\"tree\":[");

            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) builder.Append(",");
                builder.Append("{\"path\":\"")
                    .Append(EscapeJson(entries[i].Path))
                    .Append("\",\"mode\":\"100644\",\"type\":\"blob\",\"sha\":\"")
                    .Append(entries[i].Sha)
                    .Append("\"}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static string MatchJsonString(string json, string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, $"\"{key}\"\\s*:\\s*\"(.*?)\"");
            return match.Success ? match.Groups[1].Value : null;
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

        private readonly struct GitTreeEntry
        {
            public GitTreeEntry(string path, string sha)
            {
                Path = path;
                Sha = sha;
            }

            public string Path { get; }
            public string Sha { get; }
        }
    }
}
