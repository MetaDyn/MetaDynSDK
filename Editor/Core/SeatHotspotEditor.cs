using UnityEditor;
using UnityEngine;
using MetaDyn.Editor;

namespace MetaDyn
{
    [CustomEditor(typeof(SeatHotspot))]
    [CanEditMultipleObjects]
    public class SeatHotspotEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Seat Hotspot", 
                "Allows avatars to sit down at a specific location with optional animations and orientation enforcement.");

            serializedObject.Update();
            
            // Draw all properties except m_Script
            DrawPropertiesExcluding(serializedObject, "m_Script");

            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Find All Entrance Points in Scene", GUILayout.Height(25)))
            {
                var entrances = Object.FindObjectsByType<EntrancePoint>(FindObjectsSortMode.None);
                Debug.Log($"Found {entrances.Length} EntrancePoints in the scene.");
                foreach (var entrance in entrances)
                {
                    EditorGUIUtility.PingObject(entrance);
                }
            }
        }
    }
}
