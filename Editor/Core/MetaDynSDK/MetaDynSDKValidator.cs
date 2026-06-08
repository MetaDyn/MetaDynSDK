using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay.Models;
using MetaDyn.Networking;

namespace MetaDyn
{
    public enum ValidationStatus
    {
        Ok,
        Warning,
        Error
    }

    public struct ValidationResult
    {
        public string category;
        public string message;
        public ValidationStatus status;
        public Action fixAction;

        public ValidationResult(string category, string message, ValidationStatus status, Action fixAction = null)
        {
            this.category = category;
            this.message = message;
            this.status = status;
            this.fixAction = fixAction;
        }
    }

    public static class MetaDynSDKValidator
    {
        public static List<ValidationResult> ValidateProject()
        {
            List<ValidationResult> results = new List<ValidationResult>();

            // 1. UGS Linkage
            CheckUGSLinkage(results);

            // 2. Packages
            CheckPackages(results);

            // 3. Runtime Config
            CheckRuntimeConfig(results);

            // 4. Scene Configuration
            CheckSceneSetup(results);

            return results;
        }

        private static void CheckUGSLinkage(List<ValidationResult> results)
        {
            string projectId = CloudProjectSettings.projectId;
            if (string.IsNullOrEmpty(projectId))
            {
                results.Add(new ValidationResult("UGS", "Project is not linked to Unity Cloud (Project ID missing).", ValidationStatus.Error, () =>
                {
                    SettingsService.OpenProjectSettings("Project/Services");
                }));
            }
            else
            {
                results.Add(new ValidationResult("UGS", $"Project linked: {projectId}", ValidationStatus.Ok));
            }
        }

        private static void CheckPackages(List<ValidationResult> results)
        {
            bool hasNgo = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Unity.Netcode.Runtime");
            bool hasVivox = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Unity.Services.Vivox");
            bool hasMultiplayer = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Unity.Services.Multiplayer");

            if (!hasNgo) results.Add(new ValidationResult("Packages", "Netcode for GameObjects (NGO) missing.", ValidationStatus.Error));
            if (!hasVivox) results.Add(new ValidationResult("Packages", "Vivox (Voice) missing.", ValidationStatus.Error));
            if (!hasMultiplayer) results.Add(new ValidationResult("Packages", "Multiplayer (including Relay/Sessions) missing.", ValidationStatus.Error));

            if (hasNgo && hasVivox && hasMultiplayer)
            {
                results.Add(new ValidationResult("Packages", "Core UGS/NGO packages installed.", ValidationStatus.Ok));
            }
        }

        private static void CheckRuntimeConfig(List<ValidationResult> results)
        {
            MetaDynRuntimeConfig config = Resources.Load<MetaDynRuntimeConfig>("MetaDynRuntimeConfig");
            if (config == null)
            {
                results.Add(new ValidationResult("Config", "MetaDynRuntimeConfig not found in Resources folder.", ValidationStatus.Error, () =>
                {
                    MetaDynProjectConfig.ShowWindow();
                }));
            }
            else
            {
                if (string.IsNullOrEmpty(config.spaceId))
                {
                    results.Add(new ValidationResult("Config", "Space ID is missing in Runtime Config.", ValidationStatus.Error, () =>
                    {
                        MetaDynProjectConfig.ShowWindow();
                    }));
                }
                else
                {
                    results.Add(new ValidationResult("Config", $"Using Space: {config.worldDisplayName} ({config.spaceId})", ValidationStatus.Ok));
                }
            }
        }

        private static void CheckSceneSetup(List<ValidationResult> results)
        {
            var networkManager = GameObject.FindAnyObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                results.Add(new ValidationResult("Scene", "NetworkManager missing in active scene.", ValidationStatus.Error));
            }
            else
            {
                results.Add(new ValidationResult("Scene", "NetworkManager found.", ValidationStatus.Ok));

                if (networkManager.NetworkConfig.PlayerPrefab == null)
                {
                    results.Add(new ValidationResult("Scene", "Player Prefab not assigned in NetworkManager.", ValidationStatus.Error));
                }

                var transport = networkManager.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    results.Add(new ValidationResult("Scene", "UnityTransport component missing on NetworkManager.", ValidationStatus.Error));
                }
                else
                {
                    // Note: In some NGO versions, Protocol is set via ConnectionData.
                    // For now we just check it and warn.
                    if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
                    {
                        // UnityTransport uses a internal Protocol type in some versions.
                        // We'll use a simpler check for now.
                        results.Add(new ValidationResult("Scene", "WebGL detected. Ensure Transport Protocol is set to WSS in Inspector.", ValidationStatus.Warning));

                        // Validate WebGL Memory Size - Crucial to prevent abort("OOM")
                        if (PlayerSettings.WebGL.memorySize < 512)
                        {
                            results.Add(new ValidationResult("Build", $"WebGL Memory Size is low ({PlayerSettings.WebGL.memorySize}MB). 512MB or higher is recommended for URP projects.",
                                ValidationStatus.Error, () => {
                                    PlayerSettings.WebGL.memorySize = 512;
                                    Debug.Log("[MetaDyn] WebGL Memory Size updated to 512MB.");
                                }));
                        }
                    }
                }
            }

            var entrances = GameObject.FindObjectsByType<EntrancePoint>(FindObjectsSortMode.None);
            if (entrances.Length == 0)
            {
                results.Add(new ValidationResult("Scene", "No EntrancePoints found in scene.", ValidationStatus.Warning));
            }
            else
            {
                results.Add(new ValidationResult("Scene", $"Found {entrances.Length} EntrancePoints.", ValidationStatus.Ok));
            }
        }
    }
}
