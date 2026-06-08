using UnityEditor;
using UnityEngine;
using MetaDyn.Editor;
using MetaDyn.Networking;
using MetaDyn.Dashboard;
using MetaDyn.AI;

namespace MetaDyn
{
    /// <summary>
    /// Shared styles for MetaDyn SDK editors.
    /// </summary>
    internal static class MetaDynStyle
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

    [CustomEditor(typeof(Interactable))]
    [CanEditMultipleObjects]
    public class InteractableEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Interactable", 
                "Handles world objects that players can click or press a key to interact with.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(Trigger))]
    [CanEditMultipleObjects]
    public class TriggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Trigger", 
                "Executes actions when a player or object enters/leaves a specific area.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(EntrancePoint))]
    [CanEditMultipleObjects]
    public class EntrancePointEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Entrance Point", 
                "Defines a valid spawn or teleport landing location within the space.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(ProjectionSurface))]
    [CanEditMultipleObjects]
    public class ProjectionSurfaceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Projection Surface", 
                "Renders dynamic content like video streams or AI vision feeds onto a surface.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(WeatherManager))]
    [CanEditMultipleObjects]
    public class WeatherManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Weather Manager", 
                "Controls the dynamic environment, including sky, clouds, and lighting presets.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(InputManager))]
    [CanEditMultipleObjects]
    public class InputManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Input Manager", 
                "Centralizes platform controls and handles input state for players and UI.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(SettingsManager))]
    [CanEditMultipleObjects]
    public class SettingsManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Settings Manager", 
                "Manages persistent user settings like graphics quality, audio volume, and controls.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(MetaDynDoor))]
    [CanEditMultipleObjects]
    public class MetaDynDoorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("MetaDyn Door", 
                "A networked door that can be opened and closed by interacting with it.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(MetaDynLightSwitch))]
    [CanEditMultipleObjects]
    public class MetaDynLightSwitchEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("MetaDyn Light Switch", 
                "A networked switch that toggles the state of connected lights and emissive materials.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(UIManager))]
    [CanEditMultipleObjects]
    public class UIManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("UI Manager", 
                "Coordinates global UI states, panel toggles, and screen-space overlays.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(EmoteManager))]
    [CanEditMultipleObjects]
    public class EmoteManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Emote Manager", 
                "Handles local and networked character animations and social expressions.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(SupabaseAuthManager))]
    [CanEditMultipleObjects]
    public class SupabaseAuthManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Auth Manager", 
                "Manages player authentication and session persistence via Supabase.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(WebAuthBridge))]
    [CanEditMultipleObjects]
    public class WebAuthBridgeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Web Auth Bridge", 
                "Bridges WebGL browser authentication with the Unity engine.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(LoginUI))]
    [CanEditMultipleObjects]
    public class LoginUIEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Login UI", 
                "Controller for the authentication user interface and input fields.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(MetaDynNetworkEventRelay))]
    [CanEditMultipleObjects]
    public class MetaDynNetworkEventRelayEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Network Event Relay", 
                "Synchronizes game events and actions across the network for all connected clients.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(MetaDynVoiceController))]
    [CanEditMultipleObjects]
    public class MetaDynVoiceControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Voice Controller", 
                "The core AI brain for MetaDyn. Manages Gemini (OpenRouter), Whisper (STT), ElevenLabs (TTS), and autonomous embodiment.");
            
            serializedObject.Update();

            MetaDynStyle.DrawSectionHeader("🛡️ Service Credentials");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("openRouterKey"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("openAIKeyForWhisper"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("elevenLabsKey"));
            MetaDynStyle.EndSection();

            MetaDynStyle.DrawSectionHeader("🧠 AI Brain Configuration");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("openRouterModel"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("systemInstruction"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startupPrompt"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("goodbyeMessage"));
            MetaDynStyle.EndSection();

            MetaDynStyle.DrawSectionHeader("👁️ Perception & Hardware");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("perceptionManager"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("headLookController"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("aiEye"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("movementController"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("memoryManager"));
            MetaDynStyle.EndSection();

            MetaDynStyle.DrawSectionHeader("🎙️ Audio (STT/TTS)");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableVoiceInput"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("whisperModel"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("whisperLanguage"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("microphoneRecorder"));
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("elVoiceId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("elVoiceModel"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("latencyOptimization"));
            MetaDynStyle.EndSection();

            MetaDynStyle.DrawSectionHeader("🎭 Performance & Visuals");
            MetaDynSection(new[] { "animateAvatar", "assistantAnimator", "talkingTrigger", "idleTrigger" });

            MetaDynStyle.DrawSectionHeader("🖥️ UI & Interaction");
            MetaDynSection(new[] { "inputField", "sendButton", "openButton", "closeButton", "chatPanel", "chatBubblePanel", "chatBubble", "statusText", "userTextColor", "chatTextColor", "audioSource", "charactersPerSecond" });

            MetaDynStyle.DrawSectionHeader("👤 User Detection Settings");
            MetaDynSection(new[] { "visionKeywords", "evaluateGreetingStateInSession", "markUserAsGreetedWhenEventFires", "autoGreetDetectedUsers", "defaultInternalEventInstruction", "detectedUserGreetingInstruction" });

            MetaDynStyle.DrawSectionHeader("📡 Events");
            MetaDynStyle.BeginSection();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnTalkingStarted"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnIdleStarted"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnVoiceInputReceived"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnTranscriptionCompleted"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnDetectedUserChangedSimple"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnDetectedUserChanged"));
            MetaDynStyle.EndSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void MetaDynSection(string[] properties)
        {
            MetaDynStyle.BeginSection();
            foreach (var prop in properties)
            {
                var p = serializedObject.FindProperty(prop);
                if (p != null) EditorGUILayout.PropertyField(p);
            }
            MetaDynStyle.EndSection();
        }
    }

    [CustomEditor(typeof(AIPerceptionManager))]
    [CanEditMultipleObjects]
    public class AIPerceptionManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Perception Manager", 
                "The 'Visual Cortex' of the AI. Scans the world for users and interactable objects.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(AIMemoryManager))]
    [CanEditMultipleObjects]
    public class AIMemoryManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Memory Manager", 
                "Handles long-term relationships and conversation history via the MetaDyn Cloud API.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(AIMovementController))]
    [CanEditMultipleObjects]
    public class AIMovementControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Movement Controller", 
                "Autonomous navigation using Unity NavMesh. Handles walking, following, and animation syncing.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(AIEye))]
    [CanEditMultipleObjects]
    public class AIEyeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("AI Eye", 
                "Manages visual snapshots and multimodal vision processing.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(MetaDynPlatformDetector))]
    [CanEditMultipleObjects]
    public class MetaDynPlatformDetectorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MetaDynEditorHeader.DrawHeader("Platform Detector", 
                "Detects the current runtime environment (Web, Mobile, XR, Desktop) and triggers configurable events.");
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }
}

[CustomEditor(typeof(WebRTCManager))]
[CanEditMultipleObjects]
public class WebRTCManagerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        MetaDynEditorHeader.DrawHeader("WebRTC Manager", 
            "Handles real-time peer-to-peer data and media streaming for the platform.");
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();
    }
}

