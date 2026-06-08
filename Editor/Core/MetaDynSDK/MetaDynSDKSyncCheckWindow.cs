using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using MetaDyn.Editor;

namespace MetaDyn
{
    /// <summary>
    /// Read-only SDK comparison tool for checking the current project against another Unity project
    /// or the canonical MetaDyn SDK/Starter GitHub source.
    /// </summary>
    public class MetaDynSDKSyncCheckWindow : EditorWindow
    {
        private const string InventoryDocPath = ".claude/Quick Reference/SDK_TOOLKIT_INVENTORY.md";
        private const string LocalManifestPath = "Assets/MetaDyn/Editor/Core/MetaDynSDK/MetaDynSDKManifest.json";

        private static readonly string[] RequiredProjectFolders =
        {
            "Assets",
            "ProjectSettings"
        };

        private static readonly string[] IncludedSdkRoots =
        {
            "Assets/MetaDyn",
            "Assets/Plugins/WebGL"
        };

        private static readonly string[] LegacyPhotonPaths =
        {
            "Assets/Photon",
            "Assets/Plugins/Photon"
        };

        private static readonly string[] ExplicitSdkFiles =
        {
            "Assets/StreamingAssets/microphone-processor.js",
            "Assets/MetaDyn/Runtime/Core/Starter/UIGameMenu.cs",
            "Assets/MetaDyn/Runtime/Core/Starter/PlayerInput.cs"
        };

        private static readonly HashSet<string> RelocatableBaselineScriptFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PlayerInput.cs",
            "UIGameMenu.cs"
        };

        private static readonly HashSet<string> CrossProjectSharedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Assets/MetaDyn/Runtime/Dashboard/LoginUI.cs",
            "Assets/MetaDyn/Runtime/Core/Starter/UIGameMenu.cs",
            "Assets/MetaDyn/Runtime/Dashboard/WebAuthBridge.cs",
            "Assets/MetaDyn/Runtime/Dashboard/SupabaseAuthManager.cs",
            "Assets/MetaDyn/Runtime/AI/MetaDynVoiceController.cs"
        };

        private static readonly HashSet<string> ManualMergePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Assets/MetaDyn/Runtime/AI/MetaDynVoiceController.cs",
            "Assets/MetaDyn/Runtime/Dashboard/LoginUI.cs",
            "Assets/MetaDyn/Runtime/Core/Starter/UIGameMenu.cs"
        };

        private enum ComparisonSourceMode
        {
            LocalProject,
            CanonicalGitHub
        }

        private enum ComparisonActionKind
        {
            None,
            PullFromSource,
            ReviewAndMerge,
            ReviewProjectSpecific,
            ManifestUpdate
        }

        private Vector2 _scrollPosition;
        private string _selectedFolder = string.Empty;
        private string _resolvedCompareProjectPath = string.Empty;
        private ComparisonReport _report;
        private string _statusMessage = "Choose a comparison source and run a read-only SDK check.";
        private MessageType _statusType = MessageType.Info;
        private ComparisonSourceMode _sourceMode = ComparisonSourceMode.LocalProject;
        private bool _includeMetaFiles = true;
        private bool _includeInventoryManifest = true;
        private bool _includeSdkRoots = true;
        private bool _includeExplicitBaselineFiles = true;
        private bool _includeManifestTrackedRoots = true;
        private bool _showSameFiles;
        private bool _showProjects = true;
        private bool _showScope;
        private bool _showMissingFiles = true;
        private bool _showExtraFiles = true;
        private bool _showDifferentFiles = true;
        private bool _showActionPlan = true;
        private bool _showCurrentManifestGaps;
        private bool _showSourceManifestGaps;
        private bool _showClassificationLegend = true;

        public static void ShowWindow()
        {
            MetaDynSDKSyncCheckWindow window = GetWindow<MetaDynSDKSyncCheckWindow>("SDK Sync Check");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        private void OnGUI()
        {
            string currentProjectPath = GetCurrentProjectRoot();
            ManifestData localManifest = LoadManifest(currentProjectPath);

            MetaDynEditorHeader.DrawHeader("SDK Sync Tool", 
                "Compare your current project against the canonical MetaDyn master source or another local project. Identify drift and restore baseline files.");

            if (MetaDynSDK.IsMasterSDK)
            {
                EditorGUILayout.BeginHorizontal();
                var masterTagStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    normal = { textColor = new Color(1f, 0.8f, 0f) },
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                GUILayout.Label("  MASTER SDK ENVIRONMENT ACTIVE  ", masterTagStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(5);
            }
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            MetaDynStyle.DrawSectionHeader("Comparison Settings");
            MetaDynStyle.BeginSection();
            DrawProjectPaths(currentProjectPath, localManifest);
            MetaDynStyle.EndSection();

            GUILayout.Space(10);
            
            MetaDynStyle.DrawSectionHeader("Scan Scope");
            MetaDynStyle.BeginSection();
            DrawScopeOptions();
            MetaDynStyle.EndSection();

            GUILayout.Space(10);
            EditorGUILayout.HelpBox(_statusMessage, _statusType);

            GUILayout.Space(10);
            using (new EditorGUI.DisabledScope(!CanRunComparison(localManifest)))
            {
                if (GUILayout.Button("Run SDK Sync Check", GUILayout.Height(35)))
                {
                    RunComparison(currentProjectPath, localManifest);
                }
            }

            GUILayout.Space(15);
            DrawResults();

            EditorGUILayout.EndScrollView();
        }

        private void DrawProjectPaths(string currentProjectPath, ManifestData localManifest)
        {
            _showProjects = EditorGUILayout.BeginFoldoutHeaderGroup(_showProjects, "Projects");
            if (_showProjects)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                DrawReadOnlyPathField("Current Project", currentProjectPath);

                _sourceMode = (ComparisonSourceMode)EditorGUILayout.EnumPopup("Compare Source", _sourceMode);

                if (_sourceMode == ComparisonSourceMode.LocalProject)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Compare Against", EditorStyles.miniBoldLabel);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(_selectedFolder) ? "No folder selected" : _selectedFolder, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                    {
                        BrowseForProject();
                    }

                    EditorGUILayout.EndHorizontal();

                    if (!string.IsNullOrWhiteSpace(_resolvedCompareProjectPath))
                    {
                        DrawReadOnlyPathField("Resolved Project Root", _resolvedCompareProjectPath);
                    }
                }
                else
                {
                    DrawCanonicalGitHubSource(localManifest);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawCanonicalGitHubSource(ManifestData localManifest)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Canonical GitHub Source", EditorStyles.miniBoldLabel);

            string archiveUrl = GetCanonicalArchiveUrl(localManifest);
            string releaseNotesUrl = GetCanonicalReleaseNotesUrl(localManifest);
            string repoDisplay = GetCanonicalRepoDisplay(localManifest);

            DrawReadOnlyPathField("Repository", repoDisplay);
            DrawReadOnlyPathField("Archive URL", string.IsNullOrEmpty(archiveUrl) ? "Not configured in local manifest" : archiveUrl);
            DrawReadOnlyPathField("Release Notes", string.IsNullOrEmpty(releaseNotesUrl) ? "Not configured in local manifest" : releaseNotesUrl);

            EditorGUILayout.HelpBox(
                "GitHub mode downloads the configured SDK archive to a temporary folder, compares it read-only, and then removes the temp snapshot. No project files are modified.",
                MessageType.None);
        }

        private void DrawScopeOptions()
        {
            _showScope = EditorGUILayout.BeginFoldoutHeaderGroup(_showScope, "Scope");
            if (_showScope)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _includeInventoryManifest = EditorGUILayout.ToggleLeft("Include paths listed in SDK_TOOLKIT_INVENTORY.md", _includeInventoryManifest);
                _includeManifestTrackedRoots = EditorGUILayout.ToggleLeft("Include tracked roots from MetaDynSDKManifest.json", _includeManifestTrackedRoots);
                _includeSdkRoots = EditorGUILayout.ToggleLeft("Include SDK root folders (Assets/MetaDyn, Assets/Plugins/WebGL)", _includeSdkRoots);
                _includeExplicitBaselineFiles = EditorGUILayout.ToggleLeft("Include explicit baseline platform files outside Assets/MetaDyn", _includeExplicitBaselineFiles);
                _includeMetaFiles = EditorGUILayout.ToggleLeft("Include Unity .meta files", _includeMetaFiles);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawResults()
        {
            if (_report == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Source: {_report.SourceLabel}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Same files: {_report.SameFiles.Count}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Missing in current: {_report.MissingInCurrent.Count}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Extra in current: {_report.ExtraInCurrent.Count}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Different files: {_report.DifferentContent.Count}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Current manifest entries parsed: {_report.CurrentInventoryEntries}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Source manifest entries parsed: {_report.SourceInventoryEntries}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Current manifest gaps: {_report.CurrentManifestGaps.Count}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Source manifest gaps: {_report.SourceManifestGaps.Count}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            DrawClassificationLegend();
            DrawActionPlanSection();
            DrawPathSection(ref _showMissingFiles, "Missing In Current", _report.MissingInCurrent);
            DrawPathSection(ref _showExtraFiles, "Extra In Current", _report.ExtraInCurrent);
            DrawPathSection(ref _showDifferentFiles, "Different Contents", _report.DifferentContent);
            DrawPathSection(ref _showSameFiles, "Same Files", _report.SameFiles);
            DrawPathSection(ref _showCurrentManifestGaps, "Current Manifest Gaps", _report.CurrentManifestGaps);
            DrawPathSection(ref _showSourceManifestGaps, "Source Manifest Gaps", _report.SourceManifestGaps);
        }

        private void DrawActionPlanSection()
        {
            _showActionPlan = EditorGUILayout.Foldout(_showActionPlan, $"Recommended Actions ({_report.ActionPlan.Count})", true);
            if (!_showActionPlan)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_report.ActionPlan.Count == 0)
            {
                EditorGUILayout.LabelField("No actions needed.", EditorStyles.miniLabel);
            }
            else
            {
                foreach (RecommendedAction action in _report.ActionPlan)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{action.Title} ({action.Paths.Count})", EditorStyles.miniBoldLabel);
                    
                    bool showExecute = action.Kind == ComparisonActionKind.PullFromSource || action.Kind == ComparisonActionKind.ReviewAndMerge;
                    string buttonText = action.Kind == ComparisonActionKind.PullFromSource ? "Restore All" : "Update All";

                    if (showExecute)
                    {
                        if (GUILayout.Button(buttonText, GUILayout.Width(100f)))
                        {
                            ExecuteAction(action);
                        }
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            GUILayout.Button("Planned", GUILayout.Width(100f));
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField(action.Description, EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(4);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void ExecuteAction(RecommendedAction action)
        {
            if (_report == null || string.IsNullOrEmpty(_report.SourceRootPath)) return;

            string confirmTitle = action.Kind == ComparisonActionKind.PullFromSource ? "Restore Files" : "Update Files";
            string confirmMsg = $"Are you sure you want to {confirmTitle.ToLower()} for {action.Paths.Count} files? This will copy files from the source project and cannot be undone.";

            if (!EditorUtility.DisplayDialog(confirmTitle, confirmMsg, "Execute Sync", "Cancel"))
                return;

            int count = 0;
            try
            {
                foreach (string relativePath in action.Paths)
                {
                    string sourcePath = Path.Combine(_report.SourceRootPath, relativePath);
                    string targetPath = Path.Combine(GetCurrentProjectRoot(), relativePath);

                    if (!File.Exists(sourcePath))
                    {
                        Debug.LogWarning($"[SDK Sync] Source file missing: {sourcePath}");
                        continue;
                    }

                    string targetDir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                    File.Copy(sourcePath, targetPath, true);
                    count++;
                }

                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Sync Complete", $"Successfully synced {count} files from source.", "OK");
                
                // Re-run comparison to update UI
                string currentProjectPath = GetCurrentProjectRoot();
                ManifestData localManifest = LoadManifest(currentProjectPath);
                RunComparison(currentProjectPath, localManifest);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Sync Failed", $"An error occurred: {ex.Message}", "OK");
                Debug.LogException(ex);
            }
        }

        private static void DrawReadOnlyPathField(string label, string value)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(value, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        private void DrawPathSection(ref bool foldout, string title, IReadOnlyList<string> paths)
        {
            foldout = EditorGUILayout.Foldout(foldout, $"{title} ({paths.Count})", true);
            if (!foldout)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (paths.Count == 0)
            {
                EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    DrawTaggedPath(paths[i]);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawClassificationLegend()
        {
            _showClassificationLegend = EditorGUILayout.Foldout(_showClassificationLegend, "Classification Legend", true);
            if (!_showClassificationLegend)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("[Shared SDK] Reusable MetaDyn SDK file under Assets/MetaDyn or another shared SDK path.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("[Shared Baseline] Core baseline platform file that all spaces/projects are expected to carry.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("[Cross-Project] Explicitly tracked shared file called out in the SDK cross-project sync notes.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("[Manual Merge] Known hotspot where downstream projects often keep intentional customizations.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("[Legacy Photon] Part of the legacy networking stack; expected to be missing in UGS-only projects.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("[Project-Specific] Drift that exists only in the current comparison and is likely specialized to that build until proven otherwise.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawTaggedPath(string path)
        {
            string[] tags = GetPathTags(path);
            string display = tags.Length == 0
                ? path
                : string.Join(" ", tags) + " " + path;

            EditorGUILayout.SelectableLabel(display, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        private string[] GetPathTags(string relativePath)
        {
            List<string> tags = new List<string>();
            string normalizedPath = NormalizeRelativePath(relativePath);

            if (IsSharedSdkPath(normalizedPath))
            {
                tags.Add("[Shared SDK]");
            }

            if (IsSharedBaselinePath(normalizedPath))
            {
                tags.Add("[Shared Baseline]");
            }

            if (CrossProjectSharedPaths.Contains(normalizedPath))
            {
                tags.Add("[Cross-Project]");
            }

            if (ManualMergePaths.Contains(normalizedPath))
            {
                tags.Add("[Manual Merge]");
            }

            foreach (var legacy in LegacyPhotonPaths)
            {
                if (normalizedPath.StartsWith(legacy, StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("[Legacy Photon]");
                    break;
                }
            }

            if (tags.Count == 0)
            {
                tags.Add("[Project-Specific]");
            }

            return tags.ToArray();
        }

        private static bool IsLegacyPhotonPath(string relativePath)
        {
            string normalizedPath = NormalizeRelativePath(relativePath);
            foreach (var legacy in LegacyPhotonPaths)
            {
                if (normalizedPath.StartsWith(legacy, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsSharedSdkPath(string relativePath)
{
            string normalizedPath = NormalizeRelativePath(relativePath);
            string pathWithoutMeta = normalizedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(0, normalizedPath.Length - ".meta".Length)
                : normalizedPath;

            return pathWithoutMeta.StartsWith("Assets/MetaDyn/", StringComparison.OrdinalIgnoreCase)
                || pathWithoutMeta.StartsWith("Assets/Plugins/WebGL/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pathWithoutMeta, "Assets/StreamingAssets/microphone-processor.js", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSharedBaselinePath(string relativePath)
        {
            string normalizedPath = NormalizeRelativePath(relativePath);
            string pathWithoutMeta = normalizedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(0, normalizedPath.Length - ".meta".Length)
                : normalizedPath;

            return ExplicitSdkFiles.Contains(pathWithoutMeta);
        }

        private bool CanRunComparison(ManifestData localManifest)
        {
            if (_sourceMode == ComparisonSourceMode.LocalProject)
                return !string.IsNullOrWhiteSpace(_resolvedCompareProjectPath);

            return localManifest != null && !string.IsNullOrWhiteSpace(GetCanonicalArchiveUrl(localManifest));
        }

        private void BrowseForProject()
        {
            string chosenFolder = EditorUtility.OpenFolderPanel("Choose Unity Project To Compare", GetCurrentProjectRoot(), string.Empty);
            if (string.IsNullOrWhiteSpace(chosenFolder))
                return;

            _selectedFolder = NormalizePath(chosenFolder);
            _resolvedCompareProjectPath = ResolveUnityProjectRoot(_selectedFolder);
            _report = null;

            if (string.IsNullOrWhiteSpace(_resolvedCompareProjectPath))
            {
                _statusMessage = "The selected folder is not a Unity project root and no nested Unity project root was found one level down.";
                _statusType = MessageType.Warning;
                return;
            }

            if (PathsEqual(GetCurrentProjectRoot(), _resolvedCompareProjectPath))
            {
                _statusMessage = "The selected project resolves to the current open project. Choose a different project folder.";
                _statusType = MessageType.Warning;
                _resolvedCompareProjectPath = string.Empty;
                return;
            }

            _statusMessage = "Ready to compare. The scan is read-only and checks only SDK-scoped files.";
            _statusType = MessageType.Info;
        }

        private void RunComparison(string currentProjectPath, ManifestData localManifest)
        {
            try
            {
                if (_sourceMode == ComparisonSourceMode.LocalProject)
                {
                    SourceContext sourceContext = CreateLocalProjectSourceContext(_resolvedCompareProjectPath);
                    _report = BuildComparisonReport(currentProjectPath, sourceContext);
                }
                else
                {
                    _report = RunCanonicalGitHubComparison(currentProjectPath, localManifest);
                }

                bool fusionRequired = localManifest != null && localManifest.fusion != null && localManifest.fusion.required;

                // Filter Photon drift out of the "Is In Sync" check if Photon isn't required
                int missingCount = _report.MissingInCurrent.Count;
                int extraCount = _report.ExtraInCurrent.Count;
                int diffCount = _report.DifferentContent.Count;

                if (!fusionRequired)
                {
                    missingCount = _report.MissingInCurrent.Count(p => !IsLegacyPhotonPath(p));
                    extraCount = _report.ExtraInCurrent.Count(p => !IsLegacyPhotonPath(p));
                    diffCount = _report.DifferentContent.Count(p => !IsLegacyPhotonPath(p));
                }

                bool isInSync = missingCount == 0 && extraCount == 0 && diffCount == 0;

                _statusMessage = isInSync
                    ? $"SDK-scoped files match {_report.SourceLabel}."
                    : $"SDK drift detected vs {_report.SourceLabel}. Missing: {_report.MissingInCurrent.Count}, Extra: {_report.ExtraInCurrent.Count}, Different: {_report.DifferentContent.Count}.";
                
                if (!isInSync && !fusionRequired && (missingCount != _report.MissingInCurrent.Count || extraCount != _report.ExtraInCurrent.Count))
                {
                    _statusMessage += $" ({missingCount} non-Photon missing)";
                }

                _statusType = isInSync ? MessageType.Info : MessageType.Warning;
            }
            catch (Exception ex)
            {
_report = null;
                _statusMessage = $"SDK sync check failed: {ex.Message}";
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private ComparisonReport RunCanonicalGitHubComparison(string currentProjectPath, ManifestData localManifest)
        {
            if (localManifest == null)
                throw new InvalidOperationException("Local MetaDynSDKManifest.json is required for canonical GitHub comparison mode.");

            SourceContext sourceContext = PrepareCanonicalGitHubSource(localManifest);
            try
            {
                return BuildComparisonReport(currentProjectPath, sourceContext);
            }
            finally
            {
                SafeDeleteDirectory(sourceContext.CleanupDirectory);
            }
        }

        private SourceContext CreateLocalProjectSourceContext(string compareProjectPath)
        {
            SdkScope scope = BuildSdkScope(compareProjectPath);
            return new SourceContext
            {
                Label = "Selected Project",
                RootPath = compareProjectPath,
                Scope = scope
            };
        }

        private SourceContext PrepareCanonicalGitHubSource(ManifestData localManifest)
        {
            string archiveUrl = GetCanonicalArchiveUrl(localManifest);
            if (string.IsNullOrWhiteSpace(archiveUrl))
                throw new InvalidOperationException("No canonical GitHub archive URL is configured in MetaDynSDKManifest.json.");

            string tempRoot = Path.Combine(Path.GetTempPath(), "MetaDynSDKSyncCheck", Guid.NewGuid().ToString("N"));
            string zipPath = Path.Combine(tempRoot, "canonical-sdk.zip");
            string extractPath = Path.Combine(tempRoot, "extract");

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(extractPath);

            DownloadFile(archiveUrl, zipPath, "Downloading canonical MetaDyn SDK archive...");

            EditorUtility.DisplayProgressBar("SDK Sync Check", "Extracting canonical MetaDyn SDK archive...", 0.7f);
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            string repoRoot = Directory.GetDirectories(extractPath).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(repoRoot))
                repoRoot = extractPath;

            SdkScope scope = BuildSdkScope(repoRoot);
            return new SourceContext
            {
                Label = string.IsNullOrWhiteSpace(localManifest.sdkName) ? "Canonical GitHub SDK" : $"{localManifest.sdkName} GitHub",
                RootPath = repoRoot,
                Scope = scope,
                CleanupDirectory = tempRoot
            };
        }

        private ComparisonReport BuildComparisonReport(string currentProjectPath, SourceContext sourceContext)
        {
            SdkScope currentScope = BuildSdkScope(currentProjectPath);
            HashSet<string> allPaths = new HashSet<string>(currentScope.PathMap.Keys, StringComparer.OrdinalIgnoreCase);
            allPaths.UnionWith(sourceContext.Scope.PathMap.Keys);

            List<string> sameFiles = new List<string>();
            List<string> missingInCurrent = new List<string>();
            List<string> extraInCurrent = new List<string>();
            List<string> differentContent = new List<string>();

            foreach (string relativePath in allPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                bool inCurrent = currentScope.PathMap.ContainsKey(relativePath);
                bool inSource = sourceContext.Scope.PathMap.ContainsKey(relativePath);

                if (!inCurrent && inSource)
                {
                    missingInCurrent.Add(relativePath);
                    continue;
                }

                if (inCurrent && !inSource)
                {
                    extraInCurrent.Add(relativePath);
                    continue;
                }

                string currentHash = ComputeFileHash(Path.Combine(currentProjectPath, currentScope.PathMap[relativePath]));
                string sourceHash = ComputeFileHash(Path.Combine(sourceContext.RootPath, sourceContext.Scope.PathMap[relativePath]));
                if (string.Equals(currentHash, sourceHash, StringComparison.Ordinal))
                {
                    sameFiles.Add(relativePath);
                }
                else
                {
                    differentContent.Add(relativePath);
                }
            }

            return new ComparisonReport
            {
                SourceLabel = sourceContext.Label,
                SourceRootPath = sourceContext.RootPath,
                SameFiles = sameFiles,
                MissingInCurrent = missingInCurrent,
                ExtraInCurrent = extraInCurrent,
                DifferentContent = differentContent,
                CurrentInventoryEntries = currentScope.InventoryEntries.Count,
                SourceInventoryEntries = sourceContext.Scope.InventoryEntries.Count,
                CurrentManifestGaps = currentScope.ManifestGaps,
                SourceManifestGaps = sourceContext.Scope.ManifestGaps,
                ActionPlan = BuildActionPlan(missingInCurrent, extraInCurrent, differentContent, currentScope.ManifestGaps, sourceContext.Scope.ManifestGaps)
            };
}

        private List<RecommendedAction> BuildActionPlan(
            List<string> missingInCurrent,
            List<string> extraInCurrent,
            List<string> differentContent,
            List<string> currentManifestGaps,
            List<string> sourceManifestGaps)
        {
            List<RecommendedAction> actions = new List<RecommendedAction>();

            if (missingInCurrent.Count > 0)
            {
                actions.Add(new RecommendedAction
                {
                    Kind = ComparisonActionKind.PullFromSource,
                    Title = "Restore Missing Files",
                    Description = "These files exist in the selected source of truth but are missing from the current project. Later this can power one-click restore/pull actions.",
                    Paths = missingInCurrent
                });
            }

            if (differentContent.Count > 0)
            {
                actions.Add(new RecommendedAction
                {
                    Kind = ComparisonActionKind.ReviewAndMerge,
                    Title = "Review Changed Files",
                    Description = "These files exist in both places but differ. Later this can drive recommended merge, overwrite, or diff actions.",
                    Paths = differentContent
                });
            }

            if (extraInCurrent.Count > 0)
            {
                actions.Add(new RecommendedAction
                {
                    Kind = ComparisonActionKind.ReviewProjectSpecific,
                    Title = "Review Extra Files",
                    Description = "These files exist only in the current project. Later this can help decide whether each file is project-specific or should be pushed back into the canonical SDK/starter source.",
                    Paths = extraInCurrent
                });
            }

            if (currentManifestGaps.Count > 0 || sourceManifestGaps.Count > 0)
            {
                List<string> combinedManifestGaps = currentManifestGaps
                    .Concat(sourceManifestGaps)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                actions.Add(new RecommendedAction
                {
                    Kind = ComparisonActionKind.ManifestUpdate,
                    Title = "Update SDK Manifest/Inventory",
                    Description = "These files appear SDK-owned from scope rules but are missing from the documented inventory in one or both sources.",
                    Paths = combinedManifestGaps
                });
            }

            return actions;
        }

        private SdkScope BuildSdkScope(string projectRoot)
        {
            Dictionary<string, string> pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> inventoryEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // ALWAYS use the current project's manifest and inventory to define the scope,
            // even if we are scanning a different project root for files.
            string currentProjectRoot = GetCurrentProjectRoot();
            ManifestData manifest = LoadManifest(currentProjectRoot);

            if (_includeInventoryManifest)
            {
                foreach (string inventoryPath in ParseInventoryPaths(currentProjectRoot))
                {
                    inventoryEntries.Add(inventoryPath);
                    AddResolvedPathIfFileExists(projectRoot, inventoryPath, pathMap);
                }
            }

            if (_includeManifestTrackedRoots && manifest != null && manifest.trackedRoots != null)
            {
                foreach (string trackedPath in manifest.trackedRoots)
                {
                    if (Directory.Exists(Path.Combine(projectRoot, NormalizeRelativePath(trackedPath))))
                    {
                        AddFilesUnderRoot(projectRoot, trackedPath, pathMap);
                    }
                    else
                    {
                        AddResolvedPathIfFileExists(projectRoot, trackedPath, pathMap);
                    }
                }
            }

            if (_includeSdkRoots)
            {
                foreach (string relativeRoot in IncludedSdkRoots)
                {
                    AddFilesUnderRoot(projectRoot, relativeRoot, pathMap);
                }
            }

            if (_includeExplicitBaselineFiles)
            {
                foreach (string explicitFile in ExplicitSdkFiles)
                {
                    AddResolvedPathIfFileExists(projectRoot, explicitFile, pathMap);
                }
            }

            if (_includeMetaFiles)
            {
                List<KeyValuePair<string, string>> currentFiles = pathMap.ToList();
                foreach (KeyValuePair<string, string> relativePath in currentFiles)
                {
                    AddExactPathIfFileExists(projectRoot, relativePath.Key + ".meta", relativePath.Value + ".meta", pathMap);
                }
            }

            List<string> manifestGaps = FindManifestGaps(pathMap.Keys, inventoryEntries);

            return new SdkScope
            {
                PathMap = pathMap,
                InventoryEntries = inventoryEntries,
                ManifestGaps = manifestGaps
            };
        }

        private static ManifestData LoadManifest(string projectRoot)
        {
            string manifestPath = Path.Combine(projectRoot, LocalManifestPath);
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                return JsonUtility.FromJson<ManifestData>(File.ReadAllText(manifestPath));
            }
            catch
            {
                return null;
            }
        }

        private List<string> ParseInventoryPaths(string projectRoot)
        {
            string inventoryPath = Path.Combine(projectRoot, InventoryDocPath);
            if (!File.Exists(inventoryPath))
                return new List<string>();

            List<string> results = new List<string>();
            foreach (string rawLine in File.ReadAllLines(inventoryPath))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("- `", StringComparison.Ordinal))
                    continue;

                int start = line.IndexOf('`');
                int end = line.LastIndexOf('`');
                if (start < 0 || end <= start)
                    continue;

                string candidatePath = line.Substring(start + 1, end - start - 1).Trim();
                if (!IsSdkRelativePath(candidatePath))
                    continue;

                results.Add(NormalizeRelativePath(candidatePath));
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private List<string> FindManifestGaps(IEnumerable<string> includedPaths, HashSet<string> inventoryEntries)
        {
            HashSet<string> discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in includedPaths)
            {
                if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    discovered.Add(path);
                }
            }

            return discovered
                .Where(path => !inventoryEntries.Contains(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSdkRelativePath(string path)
        {
            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddFilesUnderRoot(string projectRoot, string relativeRoot, Dictionary<string, string> target)
        {
            foreach (string relativePath in EnumerateFilesUnderRoot(projectRoot, relativeRoot))
            {
                AddExactPathIfFileExists(projectRoot, relativePath, relativePath, target);
            }
        }

        private static IEnumerable<string> EnumerateFilesUnderRoot(string projectRoot, string relativeRoot)
        {
            string absoluteRoot = Path.Combine(projectRoot, NormalizeRelativePath(relativeRoot));
            if (!Directory.Exists(absoluteRoot))
                yield break;

            foreach (string absoluteFile in Directory.GetFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = NormalizeRelativePath(MakeRelativePath(projectRoot, absoluteFile));
                
                // Skip meta files during initial enumeration
                if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return relativePath;
// explicitly if the _includeMetaFiles toggle is enabled.
                if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return relativePath;
            }
        }

        private static void AddResolvedPathIfFileExists(string projectRoot, string logicalRelativePath, Dictionary<string, string> target)
        {
            if (!TryResolveSdkPath(projectRoot, logicalRelativePath, out string actualRelativePath))
                return;

            AddExactPathIfFileExists(projectRoot, logicalRelativePath, actualRelativePath, target);
        }

        private static void AddExactPathIfFileExists(string projectRoot, string logicalRelativePath, string actualRelativePath, Dictionary<string, string> target)
        {
            string normalizedLogicalPath = NormalizeRelativePath(logicalRelativePath);
            string normalizedActualPath = NormalizeRelativePath(actualRelativePath);
            string absolutePath = Path.Combine(projectRoot, normalizedActualPath);
            if (File.Exists(absolutePath))
            {
                target[normalizedLogicalPath] = normalizedActualPath;
            }
        }

        private static bool TryResolveSdkPath(string projectRoot, string logicalRelativePath, out string actualRelativePath)
        {
            string normalizedPath = NormalizeRelativePath(logicalRelativePath);
            string absolutePath = Path.Combine(projectRoot, normalizedPath);
            if (File.Exists(absolutePath))
            {
                actualRelativePath = normalizedPath;
                return true;
            }

            if (TryResolveRelocatableBaselineScriptPath(projectRoot, normalizedPath, out actualRelativePath))
                return true;

            actualRelativePath = string.Empty;
            return false;
        }

        private static bool TryResolveRelocatableBaselineScriptPath(string projectRoot, string logicalRelativePath, out string actualRelativePath)
        {
            string normalizedPath = NormalizeRelativePath(logicalRelativePath);
            bool isMetaFile = normalizedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
            string baseLogicalPath = isMetaFile
                ? normalizedPath.Substring(0, normalizedPath.Length - ".meta".Length)
                : normalizedPath;

            string fileName = Path.GetFileName(baseLogicalPath);
            if (!RelocatableBaselineScriptFileNames.Contains(fileName))
            {
                actualRelativePath = string.Empty;
                return false;
            }

            string assetsRoot = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assetsRoot))
            {
                actualRelativePath = string.Empty;
                return false;
            }

            List<string> candidates = Directory
                .GetFiles(assetsRoot, fileName, SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(MakeRelativePath(projectRoot, path)))
                .Where(path => IsLikelyDynamicBaselineScriptPath(path, fileName))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
            {
                actualRelativePath = string.Empty;
                return false;
            }

            string resolvedPath = candidates.Count == 1
                ? candidates[0]
                : ChoosePreferredFilenameMatch(candidates);

            actualRelativePath = isMetaFile ? resolvedPath + ".meta" : resolvedPath;
            return true;
        }

        private static bool IsLikelyDynamicBaselineScriptPath(string relativePath, string fileName)
        {
            string normalizedPath = NormalizeRelativePath(relativePath);
            string expectedSuffix = "/Scripts/" + fileName;
            return normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && normalizedPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static string ChoosePreferredFilenameMatch(List<string> candidates)
        {
            return candidates
                .OrderBy(path => CountPathSegments(path))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        private static int CountPathSegments(string relativePath)
        {
            return NormalizeRelativePath(relativePath)
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }

        private static string ComputeFileHash(string absolutePath)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(absolutePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static void DownloadFile(string url, string destinationPath, string progressMessage)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 60;
                request.downloadHandler = new DownloadHandlerFile(destinationPath);
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    EditorUtility.DisplayProgressBar("SDK Sync Check", progressMessage, Mathf.Clamp01(operation.progress * 0.6f));
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidOperationException($"Failed to download canonical SDK archive: {request.error}");
                }
            }
        }

        private static string GetCurrentProjectRoot()
        {
            return NormalizePath(Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory());
        }

        private static string ResolveUnityProjectRoot(string selectedFolder)
        {
            string normalizedFolder = NormalizePath(selectedFolder);
            if (IsUnityProjectRoot(normalizedFolder))
                return normalizedFolder;

            try
            {
                string[] childDirectories = Directory.GetDirectories(normalizedFolder);
                List<string> matchingChildren = childDirectories
                    .Select(NormalizePath)
                    .Where(IsUnityProjectRoot)
                    .ToList();

                if (matchingChildren.Count == 1)
                    return matchingChildren[0];
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static bool IsUnityProjectRoot(string candidatePath)
        {
            return RequiredProjectFolders.All(folder => Directory.Exists(Path.Combine(candidatePath, folder)));
        }

        private static string NormalizeRelativePath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string MakeRelativePath(string rootPath, string filePath)
        {
            string relative = Path.GetRelativePath(rootPath, filePath);
            return NormalizeRelativePath(relative);
        }

        private static string GetCanonicalArchiveUrl(ManifestData manifest)
        {
            return manifest == null ? string.Empty : manifest.downloadUrl ?? string.Empty;
        }

        private static string GetCanonicalReleaseNotesUrl(ManifestData manifest)
        {
            return manifest == null ? string.Empty : manifest.releaseNotesUrl ?? string.Empty;
        }

        private static string GetCanonicalRepoDisplay(ManifestData manifest)
        {
            if (manifest == null)
                return "Not configured";

            string releaseNotesUrl = manifest.releaseNotesUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(releaseNotesUrl))
                return "Configured via local SDK manifest";

            return releaseNotesUrl
                .Replace("https://github.com/", string.Empty)
                .Replace("/releases", string.Empty);
        }

        private static void SafeDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
                // Temporary snapshot cleanup is best-effort only.
            }
        }

        [Serializable]
        private class ManifestData
        {
            public string sdkName;
            public string latestVersion;
            public string releaseNotesUrl;
            public string downloadUrl;
            public string packageRoot;
            public string[] trackedRoots;
            public FusionManifestData fusion;
        }

        [Serializable]
        private class FusionManifestData
        {
            public bool required;
            public string supportedVersion;
            public string minimumVersion;
        }

        private class SdkScope
{
            public Dictionary<string, string> PathMap { get; set; }
            public HashSet<string> InventoryEntries { get; set; }
            public List<string> ManifestGaps { get; set; }
        }

        private class SourceContext
        {
            public string Label { get; set; }
            public string RootPath { get; set; }
            public SdkScope Scope { get; set; }
            public string CleanupDirectory { get; set; }
        }

        private class ComparisonReport
        {
            public string SourceLabel { get; set; }
            public string SourceRootPath { get; set; }
            public List<string> SameFiles { get; set; }
            public List<string> MissingInCurrent { get; set; }
            public List<string> ExtraInCurrent { get; set; }
            public List<string> DifferentContent { get; set; }
            public int CurrentInventoryEntries { get; set; }
            public int SourceInventoryEntries { get; set; }
            public List<string> CurrentManifestGaps { get; set; }
            public List<string> SourceManifestGaps { get; set; }
            public List<RecommendedAction> ActionPlan { get; set; }
        }

        private class RecommendedAction
        {
            public ComparisonActionKind Kind { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public List<string> Paths { get; set; }
        }
    }
}
