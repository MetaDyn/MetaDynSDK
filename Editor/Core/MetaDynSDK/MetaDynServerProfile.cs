using UnityEngine;

namespace MetaDyn
{
    public enum DeploymentType { SCP, GitHub, Netlify, Vercel }

    /// <summary>
    /// Server profile configuration for MetaDyn deployments
    /// </summary>
    [CreateAssetMenu(fileName = "NewServerProfile", menuName = "MetaDyn/Server Profile", order = 1)]
    public class MetaDynServerProfile : ScriptableObject
    {
        [Header("Deployment Strategy")]
        public DeploymentType deploymentType = DeploymentType.SCP;

        [Header("Profile Info")]
        public string profileName = "Production Server";

        [Header("SCP / SSH Settings")]
        [Tooltip("Server IP address or domain name")]
        public string serverAddress = "";

        [Tooltip("SSH port (default: 22)")]
        public int sshPort = 22;

        [Header("SCP Authentication")]
        [Tooltip("SSH username")]
        public string username = "";

        [Tooltip("SSH password - Leave empty if using SSH keys (recommended)")]
        public string password = "";

        [Tooltip("Path to SSH private key file (e.g., ~/.ssh/unity or ~/.ssh/id_rsa). Leave empty to use default.")]
        public string sshKeyPath = "";

        [Header("GitHub Settings")]
        public string githubUsername = "";
        public string githubRepo = ""; // Leave empty to auto-generate from room name
        [Tooltip("GitHub Personal Access Token (PAT)")]
        public string githubPAT = "";

        [Header("Netlify Settings")]
        [Tooltip("Netlify Personal Access Token")]
        public string netlifyToken = "";
        [Tooltip("Netlify Site ID (API ID). If empty, a new site will be created.")]
        public string netlifySiteId = "";
        [Tooltip("Desired subdomain (e.g. 'my-cool-space'). Netlify will assign a random one if empty or taken.")]
        public string netlifySubdomain = "";

        [Header("Vercel Settings")]
        [Tooltip("Vercel API token")]
        public string vercelToken = "";
        [Tooltip("Vercel project name. Leave empty to auto-generate from room name.")]
        public string vercelProjectName = "";
        [Tooltip("Optional Vercel team ID. Leave empty for personal account deployments.")]
        public string vercelTeamId = "";
        [Tooltip("Deploy to Vercel production target instead of preview.")]
        public bool vercelProduction = true;

        [Header("Deployment Settings")]
        [Tooltip("Exact remote directory path where builds will be uploaded (e.g., /var/www/unity-webgl/lunara/)")]
        public string remotePath = "/var/www/html/";

        [Tooltip("Exact URL where the deployed build will be accessible (e.g., https://yourdomain.com/unity-webgl/lunara/)")]
        public string deployedURL = "https://yourdomain.com/";

        [Header("Advanced Settings")]
        [Tooltip("Use compression during transfer")]
        public bool useCompression = true;

        [Tooltip("Timeout for connection in seconds")]
        public int connectionTimeout = 20;

        /// <summary>
        /// Validates if the profile has all required information
        /// </summary>
        public bool IsValid()
        {
            if (deploymentType == DeploymentType.Netlify)
            {
                return !string.IsNullOrEmpty(netlifyToken);
            }

            if (deploymentType == DeploymentType.Vercel)
            {
                return !string.IsNullOrEmpty(vercelToken);
            }

            if (deploymentType == DeploymentType.GitHub)
            {
                return !string.IsNullOrEmpty(githubUsername) &&
                       !string.IsNullOrEmpty(githubPAT);
            }

            return !string.IsNullOrEmpty(serverAddress) &&
                   !string.IsNullOrEmpty(username) &&
                   !string.IsNullOrEmpty(remotePath);
        }

        /// <summary>
        /// Gets a display-friendly description of this profile
        /// </summary>
        public string GetDisplayName()
        {
            if (deploymentType == DeploymentType.Netlify)
            {
                string site = string.IsNullOrEmpty(netlifySiteId) ? "New Site" : netlifySiteId;
                return $"{profileName} (Netlify: {site})";
            }

            if (deploymentType == DeploymentType.GitHub)
            {
                string repo = string.IsNullOrEmpty(githubRepo) ? "Auto-Repo" : githubRepo;
                return $"{profileName} (GitHub: {githubUsername}/{repo})";
            }

            if (deploymentType == DeploymentType.Vercel)
            {
                string project = string.IsNullOrEmpty(vercelProjectName) ? "Auto-Project" : vercelProjectName;
                return $"{profileName} (Vercel: {project})";
            }

            return $"{profileName} ({username}@{serverAddress})";
        }
    }
}
