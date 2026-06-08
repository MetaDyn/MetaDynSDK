using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MetaDyn
{
    /// <summary>
    /// Handles deployment operations including SCP file transfers
    /// </summary>
    public class MetaDynDeploymentManager
    {
        public delegate void ProgressCallback(float progress, string message);
        public delegate void CompletionCallback(bool success, string message);
        
        /// <summary>
        /// Deploy a directory to a remote server via SCP
        /// </summary>
        public static void DeployToServer(
            string localPath,
            MetaDynServerProfile profile,
            ProgressCallback onProgress = null,
            CompletionCallback onComplete = null)
        {
            UnityEngine.Debug.Log("[MetaDyn DEPLOY] Deployment started.");
            
            if (!profile.IsValid())
            {
                UnityEngine.Debug.LogError("[MetaDyn DEPLOY] Invalid server profile.");
                onComplete?.Invoke(false, "Invalid server profile configuration");
                return;
            }
            
            if (!Directory.Exists(localPath))
            {
                UnityEngine.Debug.LogError($"[MetaDyn DEPLOY] Local path missing: {localPath}");
                onComplete?.Invoke(false, $"Local path does not exist: {localPath}");
                return;
            }
            
            // Use exact remote path as specified (no appending)
            string remoteDest = $"{profile.username}@{profile.serverAddress}:{profile.remotePath}";
            
            UnityEngine.Debug.Log($"[MetaDyn] Remote destination: {remoteDest}");
            
            try
            {
                UnityEngine.Debug.Log("[MetaDyn] About to call ExecuteSCP...");
                bool success = ExecuteSCP(localPath, remoteDest, profile, null); // Don't pass progress callback
                UnityEngine.Debug.Log($"[MetaDyn] ExecuteSCP returned: {success}");
                
                if (success)
                {
                    string deployedUrl = profile.deployedURL;
                    UnityEngine.Debug.Log($"[MetaDyn] SUCCESS! Deployed to: {deployedUrl}");
                    onComplete?.Invoke(true, $"Deployment successful!\n\nURL: {deployedUrl}");
                }
                else
                {
                    UnityEngine.Debug.LogError("[MetaDyn] ExecuteSCP failed");
                    onComplete?.Invoke(false, "Transfer failed. Check Unity Console for details.\n\n" +
                        "Common issues:\n" +
                        "• SSH key authentication not set up\n" +
                        "• Server path doesn't exist\n" +
                        "• Permission denied\n\n" +
                        "Setup SSH keys:\n" +
                        "1. ssh-keygen -t rsa -b 4096\n" +
                        "2. ssh-copy-id " + profile.username + "@" + profile.serverAddress);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[MetaDyn] Deployment error: {ex.Message}");
                UnityEngine.Debug.LogError($"[MetaDyn] Stack trace: {ex.StackTrace}");
                onComplete?.Invoke(false, $"Deployment error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Execute SCP command using native system process
        /// </summary>
        private static bool ExecuteSCP(
            string localPath,
            string remoteDest,
            MetaDynServerProfile profile,
            ProgressCallback onProgress)
        {
            // Try rsync first (better SSH auth handling), fall back to scp
            bool rsyncAvailable = CheckCommandExists("rsync");
            
            // First, create the remote directory via SSH
            UnityEngine.Debug.Log("[MetaDyn] Creating remote directory if it doesn't exist...");
            if (!CreateRemoteDirectory(profile, out string directoryError))
            {
                string failureMessage =
                    "Remote directory creation could not be verified.\n\n" +
                    $"Server: {profile.serverAddress}\n" +
                    $"Path: {profile.remotePath}\n\n" +
                    $"{directoryError}";

                UnityEngine.Debug.LogError($"[MetaDyn] {failureMessage}");
                EditorUtility.DisplayDialog("MetaDyn Deployment Failed", failureMessage, "OK");
                return false;
            }
            
            if (rsyncAvailable)
            {
                return ExecuteRSync(localPath, remoteDest, profile, onProgress);
            }
            else
            {
                return ExecuteSCPDirect(localPath, remoteDest, profile, onProgress);
            }
        }
        
        /// <summary>
        /// Create remote directory via SSH
        /// </summary>
        private static bool CreateRemoteDirectory(MetaDynServerProfile profile, out string errorMessage)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            
            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            startInfo.FileName = "ssh.exe";
            #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            startInfo.FileName = "/usr/bin/ssh";
            #else
            startInfo.FileName = "ssh";
            #endif
            
            // Build SSH command to create directory
            StringBuilder args = new StringBuilder();
            string escapedRemotePath = EscapeForSingleQuotedShell(profile.remotePath);
            args.Append($"-p {profile.sshPort} ");
            
            if (!string.IsNullOrEmpty(profile.sshKeyPath))
            {
                args.Append($"-i \"{profile.sshKeyPath}\" ");
            }
            
            args.Append("-o StrictHostKeyChecking=no ");
            args.Append("-o BatchMode=yes ");
            args.Append($"{profile.username}@{profile.serverAddress} ");
            args.Append($"\"mkdir -p '{escapedRemotePath}' && chmod 755 '{escapedRemotePath}' && test -d '{escapedRemotePath}'\"");
            
            startInfo.Arguments = args.ToString();
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            
            UnityEngine.Debug.Log($"[MetaDyn] SSH command: {startInfo.FileName} {startInfo.Arguments}");
            
            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    process.WaitForExit(10000); // 10 second timeout
                    
                    if (process.ExitCode == 0)
                    {
                        UnityEngine.Debug.Log("[MetaDyn] ✅ Remote directory created/verified");
                        errorMessage = string.Empty;
                        return true;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[MetaDyn] Directory creation returned code: {process.ExitCode}");
                        if (!string.IsNullOrEmpty(error))
                            UnityEngine.Debug.LogWarning($"[MetaDyn] Error: {error}");
                        errorMessage = BuildDirectoryErrorMessage(process.ExitCode, output, error);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[MetaDyn] Failed to create remote directory: {ex.Message}");
                errorMessage = $"SSH directory setup threw an exception: {ex.Message}";
                return false;
            }
        }

        private static string BuildDirectoryErrorMessage(int exitCode, string output, string error)
        {
            StringBuilder message = new StringBuilder();
            message.Append($"SSH preflight failed with exit code {exitCode}.");

            if (!string.IsNullOrWhiteSpace(error))
            {
                message.Append($"\n\nSSH error:\n{error.Trim()}");
            }
            else if (!string.IsNullOrWhiteSpace(output))
            {
                message.Append($"\n\nSSH output:\n{output.Trim()}");
            }
            else
            {
                message.Append("\n\nNo SSH output was returned.");
            }

            return message.ToString();
        }

        private static string EscapeForSingleQuotedShell(string value)
        {
            return value.Replace("'", "'\"'\"'");
        }
        
        /// <summary>
        /// Execute rsync command (better for SSH agent auth)
        /// </summary>
        private static bool ExecuteRSync(
            string localPath,
            string remoteDest,
            MetaDynServerProfile profile,
            ProgressCallback onProgress)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "rsync";
            
            // Build rsync arguments
            StringBuilder args = new StringBuilder();
            
            // Options
            args.Append("-avz "); // Archive, verbose, compress
            args.Append("--progress "); // Show progress
            args.Append($"--timeout={profile.connectionTimeout} ");
            
            // SSH options - FORCE non-interactive mode
            args.Append($"-e 'ssh -p {profile.sshPort} ");
            
            // Use custom SSH key if provided
            if (!string.IsNullOrEmpty(profile.sshKeyPath))
            {
                string expandedPath = profile.sshKeyPath.Replace("~", System.Environment.GetEnvironmentVariable("HOME"));
                args.Append($"-i {expandedPath} ");
            }
            
            args.Append("-o StrictHostKeyChecking=no ");
            args.Append("-o BatchMode=yes "); // NO interactive prompts
            args.Append("-o PasswordAuthentication=no "); // Only use keys
            args.Append("-o PubkeyAuthentication=yes "); // Prefer public key
            args.Append("-o ConnectTimeout=" + profile.connectionTimeout + "' ");
            
            // Source (with trailing slash to copy contents)
            if (!localPath.EndsWith("/"))
                localPath += "/";
            args.Append($"\"{localPath}\" ");
            
            // Destination
            args.Append($"\"{remoteDest}\"");
            
            startInfo.Arguments = args.ToString();
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            
            UnityEngine.Debug.Log($"[MetaDyn] Using rsync for deployment (non-interactive mode)...");
            UnityEngine.Debug.Log($"[MetaDyn] Command: rsync {args}");
            
            return ExecuteTransferCommand(startInfo, onProgress);
        }
        
        /// <summary>
        /// Execute SCP command directly
        /// </summary>
        private static bool ExecuteSCPDirect(
            string localPath,
            string remoteDest,
            MetaDynServerProfile profile,
            ProgressCallback onProgress)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            
            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Windows - use native OpenSSH
            startInfo.FileName = "scp.exe";
            #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            startInfo.FileName = "/usr/bin/scp";
            #else
            startInfo.FileName = "scp";
            #endif
            
            // Build SCP arguments
            StringBuilder args = new StringBuilder();
            
            // Options
            args.Append("-r "); // Recursive
            if (profile.useCompression)
                args.Append("-C "); // Compression
            
            args.Append($"-P {profile.sshPort} ");
            
            // Use custom SSH key if provided
            if (!string.IsNullOrEmpty(profile.sshKeyPath))
            {
                args.Append($"-i \"{profile.sshKeyPath}\" ");
            }
            
            args.Append("-o StrictHostKeyChecking=no ");
            args.Append($"-o ConnectTimeout={profile.connectionTimeout} ");
            args.Append("-o BatchMode=yes ");
            args.Append("-o PasswordAuthentication=no ");
            args.Append("-o PubkeyAuthentication=yes ");
            args.Append("-o PreferredAuthentications=publickey ");
            
            // Source and destination
            args.Append($"\"{localPath}\\*\" {remoteDest}");
            
            startInfo.Arguments = args.ToString();
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            
            UnityEngine.Debug.Log($"[MetaDyn] Using SCP for deployment (Windows native SSH)...");
            UnityEngine.Debug.Log($"[MetaDyn] Command: {startInfo.FileName} {startInfo.Arguments}");
            
            return ExecuteTransferCommand(startInfo, onProgress);
        }
        
        /// <summary>
        /// Execute transfer command and handle output
        /// </summary>
        private static bool ExecuteTransferCommand(ProcessStartInfo startInfo, ProgressCallback onProgress)
        {
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            
            try
            {
                UnityEngine.Debug.Log($"[MetaDyn] Starting process: {startInfo.FileName}");
                UnityEngine.Debug.Log($"[MetaDyn] Arguments: {startInfo.Arguments}");
                
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    
                    // Use async output reading to prevent blocking
                    process.OutputDataReceived += (sender, e) => {
                        if (e.Data != null)
                        {
                            outputBuilder.AppendLine(e.Data);
                            UnityEngine.Debug.Log($"[MetaDyn] Output: {e.Data}");
                        }
                    };
                    
                    process.ErrorDataReceived += (sender, e) => {
                        if (e.Data != null)
                        {
                            errorBuilder.AppendLine(e.Data);
                            UnityEngine.Debug.LogWarning($"[MetaDyn] Error: {e.Data}");
                        }
                    };
                    
                    UnityEngine.Debug.Log("[MetaDyn] Starting process...");
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    
                    UnityEngine.Debug.Log("[MetaDyn] Process started, waiting for completion (30s timeout)...");
                    
                    // Wait for exit with 30 second timeout
                    bool exited = process.WaitForExit(30000);
                    
                    if (!exited)
                    {
                        UnityEngine.Debug.LogError("[MetaDyn] ❌ Transfer timed out after 30 seconds");
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    
                    // Give a moment for async output to finish
                    System.Threading.Thread.Sleep(100);
                    
                    string output = outputBuilder.ToString();
                    string error = errorBuilder.ToString();
                    
                    UnityEngine.Debug.Log($"[MetaDyn] Process exited with code: {process.ExitCode}");
                    
                    if (process.ExitCode == 0)
                    {
                        UnityEngine.Debug.Log("[MetaDyn] ✅ Transfer successful!");
                        return true;
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"[MetaDyn] ❌ Transfer failed (exit code: {process.ExitCode})");
                        
                        if (error.Contains("Permission denied") || error.Contains("publickey"))
                        {
                            UnityEngine.Debug.LogError("[MetaDyn] SSH key authentication failed!");
                            UnityEngine.Debug.LogError("[MetaDyn] Test manually: ssh jza@192.168.0.193");
                        }
                        
                        if (string.IsNullOrEmpty(output) && string.IsNullOrEmpty(error))
                        {
                            UnityEngine.Debug.LogError("[MetaDyn] No output received. Command may have hung waiting for input.");
                        }
                        
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[MetaDyn] Failed to execute transfer: {ex.Message}");
                UnityEngine.Debug.LogError($"[MetaDyn] Stack trace: {ex.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// Check if a command exists in PATH
        /// </summary>
        private static bool CheckCommandExists(string command)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "which";
                startInfo.Arguments = command;
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.CreateNoWindow = true;
                
                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Test SSH connection to server
        /// </summary>
        public static bool TestConnection(MetaDynServerProfile profile, out string message)
        {
            if (!profile.IsValid())
            {
                message = "Invalid server profile";
                return false;
            }
            
            try
            {
                // Use TCP socket to test if SSH port is accessible
                using (System.Net.Sockets.TcpClient client = new System.Net.Sockets.TcpClient())
                {
                    UnityEngine.Debug.Log($"[MetaDyn] Testing connection to {profile.serverAddress}:{profile.sshPort}...");
                    
                    // Attempt to connect with timeout
                    var result = client.BeginConnect(profile.serverAddress, profile.sshPort, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(profile.connectionTimeout));
                    
                    if (!success)
                    {
                        message = $"Connection timed out after {profile.connectionTimeout} seconds.\n\nServer may be unreachable or SSH port {profile.sshPort} is blocked.";
                        return false;
                    }
                    
                    client.EndConnect(result);
                    
                    if (client.Connected)
                    {
                        message = $"✅ SSH port is accessible!\n\nServer: {profile.serverAddress}:{profile.sshPort}\nUser: {profile.username}\n\nNote: Actual authentication will occur during deployment.";
                        return true;
                    }
                    else
                    {
                        message = $"Could not connect to {profile.serverAddress}:{profile.sshPort}";
                        return false;
                    }
                }
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                message = $"Connection failed: {ex.Message}\n\nCheck:\n• Server address is correct\n• SSH is running on port {profile.sshPort}\n• Firewall allows connections";
                return false;
            }
            catch (Exception ex)
            {
                message = $"Connection error: {ex.Message}";
                return false;
            }
        }
        
        /// <summary>
        /// Get the default WebGL build output path
        /// </summary>
        public static string GetWebGLBuildPath()
        {
            // Unity's default WebGL build location
            return Path.Combine(Application.dataPath, "../Build/WebGL");
        }
        
        /// <summary>
        /// Check if WebGL build exists
        /// </summary>
        public static bool WebGLBuildExists(out string buildPath)
        {
            buildPath = GetWebGLBuildPath();
            return Directory.Exists(buildPath);
        }
    }
}
