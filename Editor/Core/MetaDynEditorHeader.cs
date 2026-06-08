using UnityEditor;
using UnityEngine;

namespace MetaDyn.Editor
{
    public static class MetaDynEditorHeader
    {
        private static Texture2D _logo;
        private static GUIStyle _headerStyle;
        private static GUIStyle _descriptionStyle;

        private static void Initialize()
        {
            if (_logo == null)
            {
                // Primary logo search
                string logoPath = "Assets/MetaDyn/Media/Images/metadyn_alphastax_logo_400.png";
                _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
            }

            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = new Color(0.2f, 0.6f, 1f) }
                };
            }

            if (_descriptionStyle == null)
            {
                _descriptionStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true,
                    fontSize = 11,
                    fontStyle = FontStyle.Italic,
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
                };
            }
        }

        public static void DrawHeader(string title, string description)
        {
            Initialize();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.BeginHorizontal();
            
            if (_logo != null)
            {
                Rect logoRect = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                GUI.DrawTexture(logoRect, _logo, ScaleMode.ScaleToFit);
                GUILayout.Space(10);
            }

            EditorGUILayout.BeginVertical();
            GUILayout.Space(5);
            EditorGUILayout.LabelField(title, _headerStyle);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(description))
            {
                GUILayout.Space(5);
                EditorGUILayout.LabelField(description, _descriptionStyle);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }
    }

    /// <summary>
    /// Shared styles for MetaDyn SDK editors.
    /// </summary>
    public static class MetaDynStyle
    {
        public static readonly Color PrimaryBlue = new Color(0.2f, 0.6f, 1f);
        public static readonly Color SubtleText = new Color(0.7f, 0.7f, 0.7f);

        private static GUIStyle _sectionHeaderStyle;
        public static GUIStyle SectionHeaderStyle
        {
            get
            {
                if (_sectionHeaderStyle == null)
                {
                    _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        normal = { textColor = PrimaryBlue }
                    };
                }
                return _sectionHeaderStyle;
            }
        }

        public static void DrawSectionHeader(string title)
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField(title.ToUpper(), SectionHeaderStyle);
            GUILayout.Space(2);
        }

        public static void BeginSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(2);
        }

        public static void EndSection()
        {
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }
    }
}
