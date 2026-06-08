using UnityEditor;
using UnityEngine;
using MetaDyn.Editor;
using System.Linq;

namespace MetaDyn.Editor
{
    /// <summary>
    /// Branded Welcome Panel for the MetaDyn SDK.
    /// Guides users through setup and providing quick access to resources.
    /// </summary>
    [InitializeOnLoad]
    public class MetaDynWelcomePanel : EditorWindow
    {
        private const string SHOW_ON_STARTUP_KEY = "MetaDyn_ShowWelcomeOnStartup";
        private Vector2 _scrollPosition;
        private static bool _showOnStartup;

        static MetaDynWelcomePanel()
        {
            EditorApplication.delayCall += OnEditorStartup;
        }

        private static void OnEditorStartup()
        {
            _showOnStartup = EditorPrefs.GetBool(SHOW_ON_STARTUP_KEY, true);
            if (_showOnStartup && !Application.isPlaying)
            {
                // Delay showing to ensure the editor is fully loaded
                EditorApplication.delayCall += ShowWindow;
            }
        }

        [MenuItem("MetaDyn/Welcome Panel", false, 0)]
        [MenuItem("Tools/MetaDyn/Welcome Panel", false, 0)]
        public static void ShowWindow()
{
            MetaDynWelcomePanel window = GetWindow<MetaDynWelcomePanel>(true, "MetaDyn Welcome", true);
            window.minSize = new Vector2(450, 550);
            window.maxSize = new Vector2(450, 700);
            window.Show();
        }

        private void OnEnable()
        {
            _showOnStartup = EditorPrefs.GetBool(SHOW_ON_STARTUP_KEY, true);
        }

        private void OnGUI()
        {
            MetaDynEditorHeader.DrawHeader("Welcome to MetaDyn", 
                "The unified immersive platform for Unity 6. Let's get your project production-ready.");

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawGettingStartedSection();
            GUILayout.Space(10);

            DrawToolkitSection();
            GUILayout.Space(10);

            DrawResourcesSection();
            GUILayout.Space(10);

            if (MetaDynSDK.IsMasterSDK)
            {
                DrawMasterSection();
                GUILayout.Space(10);
            }

            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawGettingStartedSection()
        {
            MetaDynStyle.DrawSectionHeader("🚀 Getting Started");
            MetaDynStyle.BeginSection();
            
            EditorGUILayout.LabelField("Follow these steps to configure your SDK:", EditorStyles.wordWrappedLabel);
            GUILayout.Space(5);

            if (DrawStep("1. Run Validation Scan", "Ensure your project settings and packages are NGO/UGS compliant."))
            {
                MetaDynProjectConfig.ShowWindow();
            }

            if (DrawStep("2. Setup Authentication", "Configure your Supabase credentials to enable player accounts and persistence."))
            {
                MetaDynProjectConfig.ShowWindow();
            }

            if (DrawStep("3. Configure World Settings", "Set your Space ID and Room Name in a MetaDyn Runtime Config asset."))
            {
                MetaDynProjectConfig.ShowWindow();
            }

            MetaDynStyle.EndSection();
        }

        private void DrawToolkitSection()
        {
            MetaDynStyle.DrawSectionHeader("🛠️ SDK Toolkit");
            MetaDynStyle.BeginSection();

            EditorGUILayout.LabelField("Quick access to core MetaDyn tools:", EditorStyles.miniLabel);
            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Deployment Center", GUILayout.Height(30)))
            {
                MetaDynProjectConfig.ShowWindow();
            }
            if (GUILayout.Button("Sync Tool", GUILayout.Height(30)))
            {
                MetaDynSDKSyncCheckWindow.ShowWindow();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Dashboard", GUILayout.Height(30)))
            {
                MetaDynDashboard.ShowWindow();
            }
            EditorGUILayout.EndHorizontal();

            MetaDynStyle.EndSection();
        }

        private void DrawResourcesSection()
        {
            MetaDynStyle.DrawSectionHeader("📚 Resources & Documentation");
            MetaDynStyle.BeginSection();

            if (GUILayout.Button("View Local Documentation", EditorStyles.linkLabel))
            {
                EditorUtility.RevealInFinder("Assets/Docs");
            }

            if (GUILayout.Button("MetaDyn Executive Summary", EditorStyles.linkLabel))
            {
                OpenLocalDoc("Assets/Docs/MetaDyn_Executive_Summary.md");
            }

            if (GUILayout.Button("Auth System Reference", EditorStyles.linkLabel))
            {
                OpenLocalDoc(".claude/Quick Reference/AUTH_SYSTEM.md");
            }

            if (GUILayout.Button("Infrastructure & Deployment Guide", EditorStyles.linkLabel))
            {
                OpenLocalDoc(".claude/Quick Reference/INFRASTRUCTURE.md");
            }

            if (GUILayout.Button("AI Embodiment Guide", EditorStyles.linkLabel))
            {
                OpenLocalDoc(".claude/Quick Reference/AI_EMBODIMENT.md");
            }

            if (GUILayout.Button("Functional Design Spec (FDS)", EditorStyles.linkLabel))
            {
                OpenLocalDoc(".claude/Planning/FDS.md");
            }

            MetaDynStyle.EndSection();
        }

        private void DrawMasterSection()
        {
            MetaDynStyle.DrawSectionHeader("👑 Master SDK Environment");
            MetaDynStyle.BeginSection();
            EditorGUILayout.HelpBox("You are currently in the Master SDK development environment. Use the Sync Tool to propagate changes to spoke projects.", MessageType.Info);
            MetaDynStyle.EndSection();
        }

        private void DrawFooter()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            
            EditorGUI.BeginChangeCheck();
            _showOnStartup = EditorGUILayout.ToggleLeft("Show this panel on startup", _showOnStartup);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(SHOW_ON_STARTUP_KEY, _showOnStartup);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"SDK v{MetaDynSDK.SDK_VERSION}", EditorStyles.miniLabel);
            
            EditorGUILayout.EndHorizontal();
        }

        private bool DrawStep(string title, string description)
        {
            bool clicked = false;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open", GUILayout.Width(60)))
            {
                clicked = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
            return clicked;
        }

        private void OpenLocalDoc(string path)
        {
            Object doc = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (doc != null)
            {
                AssetDatabase.OpenAsset(doc);
            }
            else
            {
                // Fallback to absolute path check if it's outside Assets
                string absolutePath = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, path);
                if (System.IO.File.Exists(absolutePath))
                {
                    Application.OpenURL("file://" + absolutePath);
                }
                else
                {
                    Debug.LogWarning($"[MetaDyn] Document not found: {path}");
                }
            }
        }
    }
}
