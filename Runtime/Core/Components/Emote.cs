using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using MetaDyn.Networking;

namespace MetaDyn
{
    /// <summary>
    /// Manages emote animations for avatars.
    /// Designed to work with UI buttons and hotkeys.
    /// Updated for NGO/UGS compatibility.
    /// </summary>
    public class EmoteManager : MonoBehaviour
    {
        public static EmoteManager Instance { get; private set; }

        [Header("Emote Configuration")]
        [Tooltip("List of available emotes")]
        public List<EmoteData> emotes = new List<EmoteData>();

        [Header("Settings")]
        [Tooltip("Can the player move while emoting?")]
        public bool allowMovementDuringEmote = false;

        [Tooltip("Auto-stop emote after this many seconds (0 = loop forever)")]
        public float autoStopAfter = 0f;

        [Tooltip("Show debug logs")]
        public bool debugMode = false;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            // Check for hotkey presses on the local client
            CheckHotkeyInput();
        }

        private void CheckHotkeyInput()
        {
            // Only process input if NetworkManager is active and we have a local player
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

            // Prevent emotes if input is locked (e.g., typing in chat)
            if (InputManager.IsInputLocked) return;
            
            var localPlayerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
if (localPlayerObj == null) return;

            var ugsController = localPlayerObj.GetComponent<MetaDynUGSPlayerController>();
            if (ugsController == null || ugsController.IsEmoting) return;

            // Check each emote's hotkey
            for (int i = 0; i < emotes.Count; i++)
            {
                var emote = emotes[i];
                if (emote.hotkey != KeyCode.None && Input.GetKeyDown(emote.hotkey))
                {
                    if (debugMode) Debug.Log($"[EmoteManager] Hotkey pressed for emote: {emote.emoteName}");
                    ugsController.RequestPlayEmote(i);
                    break;
                }
            }
        }

        /// <summary>
        /// Get an emote's data by name
        /// </summary>
        public EmoteData GetEmoteByName(string emoteName)
        {
            return emotes.Find(e => e.emoteName == emoteName);
        }

        /// <summary>
        /// Get an emote's data by index
        /// </summary>
        public EmoteData GetEmoteByIndex(int index)
        {
            if (index < 0 || index >= emotes.Count) return null;
            return emotes[index];
        }

        public int GetEmoteIndex(string emoteName)
        {
            return emotes.FindIndex(e => e.emoteName == emoteName);
        }

#if UNITY_EDITOR
        [ContextMenu("List Available Emotes")]
        private void EditorListEmotes()
        {
            Debug.Log("=== Available Emotes ===");
            for (int i = 0; i < emotes.Count; i++)
            {
                var emote = emotes[i];
                string hotkey = emote.hotkey != KeyCode.None ? $"[{emote.hotkey}]" : "[No hotkey]";
                string animation = emote.animation != null ? emote.animation.name : "MISSING";
                Debug.Log($"{i}: {emote.emoteName} {hotkey} - Animation: {animation}");
            }
        }
#endif
    }

    /// <summary>
    /// Data structure for an emote
    /// </summary>
    [System.Serializable]
    public class EmoteData
    {
        [Tooltip("Display name of the emote")]
        public string emoteName = "New Emote";

        [Tooltip("Animation clip to play")]
        public AnimationClip animation;

        [Tooltip("Optional audio clip to play with emote")]
        public AudioClip audioClip;

        [Tooltip("Optional hotkey to trigger this emote (None = no hotkey)")]
        public KeyCode hotkey = KeyCode.None;

        [Tooltip("Optional sprite icon for UI buttons")]
        public Sprite icon;
    }
}
