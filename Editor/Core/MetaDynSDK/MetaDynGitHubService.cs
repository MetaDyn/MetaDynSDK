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

                // 3. Enumerate files
                string[] files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
                int totalFiles = files.Length;

                for (int i = 0; i < totalFiles; i++)
                {
                    string filePath = files[i];
                    string relPath = filePath.Substring(localPath.Length).Replace("\\", "/").TrimStart('/');
                    
                    float progress = 0.2f + (0.7f * (i / (float)totalFiles));
                    onProgress?.Invoke(progress, $"Uploading {relPath}...");

                    byte[] fileData = File.ReadAllBytes(filePath);
                    
                    // Get existing file SHA if it exists (for update)
                    string sha = await GetFileSha(profile, repoName, relPath);
                    
                    // Upload/Update file
                    await UploadFile(profile, repoName, relPath, fileData, sha);
                }

                // 3. Enable GitHub Pages if not already enabled
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

            using (UnityWebRequest request = new UnityWebRequest($"{GITHUB_API}/repos/{p.githubUsername}/{repoName}/git/refs", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, p.githubPAT);
                
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to create gh-pages branch: {request.error}\n{request.downloadHandler.text}");
                }
            }
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
                    var match = System.Text.RegularExpressions.Regex.Match(json, "\"default_branch\":\"(.*?)\"");
                    return match.Success ? match.Groups[1].Value : "main";
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
                    var match = System.Text.RegularExpressions.Regex.Match(json, "\"sha\":\"(.*?)\"");
                    return match.Success ? match.Groups[1].Value : null;
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

            using (UnityWebRequest request = new UnityWebRequest($"{GITHUB_API}/user/repos", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, p.githubPAT);
                
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to create repository: {request.error}\n{request.downloadHandler.text}");
                }
            }
        }

        private static async Task<string> GetFileSha(MetaDynServerProfile p, string repo, string path)
        {
            // We check the gh-pages branch specifically
            using (UnityWebRequest request = UnityWebRequest.Get($"{GITHUB_API}/repos/{p.githubUsername}/{repo}/contents/{path}?ref=gh-pages"))
            {
                SetHeaders(request, p.githubPAT);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success) return null;

                // Simple JSON extraction for SHA
                string json = request.downloadHandler.text;
                var match = System.Text.RegularExpressions.Regex.Match(json, "\"sha\":\"(.*?)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
        }

        private static async Task UploadFile(MetaDynServerProfile p, string repo, string path, byte[] data, string sha)
        {
            string base64Content = Convert.ToBase64String(data);
            string json = $"{{\"message\":\"Deploy via MetaDyn SDK\", \"content\":\"{base64Content}\", \"branch\":\"gh-pages\"" + 
                          (sha != null ? $", \"sha\":\"{sha}\"" : "") + "}";
            
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest request = new UnityWebRequest($"{GITHUB_API}/repos/{p.githubUsername}/{repo}/contents/{path}", "PUT"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, p.githubPAT);
                
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to upload {path}: {request.error}\n{request.downloadHandler.text}");
                }
            }
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
            r.SetRequestHeader("Content-Type", "application/json");
        }
    }
}