using UnityEditor;
using UnityEngine;

namespace MetaDyn.Editor
{
    [CustomEditor(typeof(MetaDynItemMetadata))]
    public class MetaDynItemMetadataEditor : UnityEditor.Editor
    {
        private SerializedProperty _itemId;
        private SerializedProperty _displayName;
        private SerializedProperty _creatorName;
        private SerializedProperty _category;
        private SerializedProperty _rarity;
        private SerializedProperty _version;
        private SerializedProperty _license;
        private SerializedProperty _licenseUrl;
        private SerializedProperty _manifestJson;
        private SerializedProperty _platformManifestUrl;
        private SerializedProperty _customMetadata;

        private void OnEnable()
        {
            _itemId = serializedObject.FindProperty("itemId");
            _displayName = serializedObject.FindProperty("displayName");
            _creatorName = serializedObject.FindProperty("creatorName");
            _category = serializedObject.FindProperty("category");
            _rarity = serializedObject.FindProperty("rarity");
            _version = serializedObject.FindProperty("version");
            _license = serializedObject.FindProperty("license");
            _licenseUrl = serializedObject.FindProperty("licenseUrl");
            _manifestJson = serializedObject.FindProperty("manifestJson");
            _platformManifestUrl = serializedObject.FindProperty("platformManifestUrl");
            _customMetadata = serializedObject.FindProperty("customMetadata");
        }

        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Item Metadata", "Configure identity, licensing, and platform integration for this MetaDyn asset.");

            serializedObject.Update();

            MetaDynStyle.DrawSectionHeader("Identity");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(_itemId);
            EditorGUILayout.PropertyField(_displayName);
            EditorGUILayout.PropertyField(_creatorName);
            
            if (GUILayout.Button("Generate New ID"))
            {
                var metadata = (MetaDynItemMetadata)target;
                Undo.RecordObject(metadata, "Generate New Item ID");
                metadata.GenerateNewId();
                EditorUtility.SetDirty(metadata);
            }
            MetaDynStyle.EndSection();

            MetaDynStyle.DrawSectionHeader("Classification");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(_category);
            EditorGUILayout.PropertyField(_rarity);
            EditorGUILayout.PropertyField(_version);
            MetaDynStyle.EndSection();

            MetaDynStyle.DrawSectionHeader("Licensing");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(_license);
            EditorGUILayout.PropertyField(_licenseUrl);
            MetaDynStyle.EndSection();

            MetaDynStyle.DrawSectionHeader("Platform Integration");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(_manifestJson);
            EditorGUILayout.PropertyField(_platformManifestUrl);
            EditorGUILayout.PropertyField(_customMetadata);

            GUILayout.Space(5);
            if (GUILayout.Button("Sync From Manifest", GUILayout.Height(30)))
            {
                var metadata = (MetaDynItemMetadata)target;
                Undo.RecordObject(metadata, "Sync Metadata from Manifest");
                metadata.SyncFromManifest();
                EditorUtility.SetDirty(metadata);
            }
            MetaDynStyle.EndSection();

            serializedObject.ApplyModifiedProperties();
        }
    }
}