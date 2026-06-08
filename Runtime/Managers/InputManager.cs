using UnityEngine;
using System.Collections.Generic;

namespace MetaDyn
{
    /// <summary>
    /// Centralized input management system for controlling when player input should be processed.
    /// Provides a stack-based locking mechanism to handle multiple UI systems (chat, menus, etc.)
    /// requesting input locks simultaneously.
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        #region Singleton

        private static InputManager _instance;

        public static InputManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    // Try to find existing instance
                    _instance = FindObjectOfType<InputManager>();

                    // Create new instance if none exists
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[InputManager]");
                        _instance = go.AddComponent<InputManager>();
                        DontDestroyOnLoad(go);
                        Debug.Log("[InputManager] Created singleton instance");
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Inspector Settings

        [Header("Debug Settings")]
        [SerializeField]
        [Tooltip("Enable debug logging for lock/unlock operations")]
        private bool enableDebugLogging = false;

        [Header("Input Lock State")]
        [SerializeField]
        [Tooltip("Current number of active locks")]
        private int activeLockCount = 0;

        [SerializeField]
        [Tooltip("Display current lock reasons (read-only)")]
        private List<string> currentLockReasons = new List<string>();

        #endregion

        #region Private State

        // Stack-based lock tracking (supports multiple simultaneous locks)
        private Stack<string> _lockStack = new Stack<string>();

        #endregion

        #region Public API
        
        // Virtual Input State (Mobile)
        public static Vector2 VirtualMoveInput = Vector2.zero;
        public static bool VirtualJumpInput = false;
        public static bool VirtualSprintInput = false;

        /// <summary>
        /// Returns the combined movement input (Physical + Virtual).
        /// </summary>
        public static Vector2 GetMoveInput()
        {
            if (IsInputLocked) return Vector2.zero;
            
            Vector2 physical = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            Vector2 combined = physical + VirtualMoveInput;
            return Vector2.ClampMagnitude(combined, 1.0f);
        }

        /// <summary>
        /// Returns true if Jump button is pressed (Physical or Virtual).
        /// </summary>
        public static bool GetJumpInput()
        {
            if (IsInputLocked) return false;
            return Input.GetButtonDown("Jump") || VirtualJumpInput;
        }

        /// <summary>
        /// Returns true if Sprint button is held (Physical or Virtual).
        /// </summary>
        public static bool GetSprintInput()
        {
            if (IsInputLocked) return false;
            return Input.GetButton("Sprint") || VirtualSprintInput;
        }

        /// <summary>
        /// Returns true if any input lock is currently active.
        /// </summary>
public static bool IsInputLocked
        {
            get 
            { 
                if (_instance == null) return false;
                return Instance._lockStack.Count > 0; 
            }
        }

        /// <summary>
        /// Lock player input with an optional reason for debugging.
        /// Multiple systems can lock input simultaneously - all must unlock before input is enabled.
        /// </summary>
        /// <param name="reason">Optional identifier for debugging (e.g., "ChatInput", "PauseMenu")</param>
        public static void LockInput(string reason = "Unknown")
        {
            Instance._lockStack.Push(reason);
            Instance.UpdateDebugDisplay();

            if (Instance.enableDebugLogging)
            {
                Debug.Log($"[InputManager] Input LOCKED by '{reason}' (Total locks: {Instance._lockStack.Count})");
            }
        }

        /// <summary>
        /// Unlock player input. Must match the most recent lock reason.
        /// If multiple locks are active, input remains locked until all are released.
        /// </summary>
        /// <param name="reason">Optional identifier - should match the LockInput reason</param>
        public static void UnlockInput(string reason = "Unknown")
        {
            if (Instance._lockStack.Count == 0)
            {
                Debug.LogWarning($"[InputManager] Attempted to unlock input with reason '{reason}', but no locks are active!");
                return;
            }

            string topReason = Instance._lockStack.Pop();
            Instance.UpdateDebugDisplay();

            if (Instance.enableDebugLogging)
            {
                Debug.Log($"[InputManager] Input UNLOCKED by '{reason}' (Expected: '{topReason}', Remaining locks: {Instance._lockStack.Count})");
            }

            // Warn if reason mismatch (not enforced, just for debugging)
            if (topReason != reason && Instance.enableDebugLogging)
            {
                Debug.LogWarning($"[InputManager] Lock/Unlock mismatch! Unlocked '{reason}' but expected '{topReason}'. This may indicate a bug.");
            }
        }

        /// <summary>
        /// Force clear all input locks. Use with caution - only for emergency situations.
        /// </summary>
        public static void ClearAllLocks()
        {
            int clearedCount = Instance._lockStack.Count;
            Instance._lockStack.Clear();
            Instance.UpdateDebugDisplay();

            if (Instance.enableDebugLogging)
            {
                Debug.LogWarning($"[InputManager] Force cleared {clearedCount} input locks!");
            }
        }

        /// <summary>
        /// Get the current number of active locks (for debugging).
        /// </summary>
        public static int GetActiveLockCount()
        {
            return Instance._lockStack.Count;
        }

        /// <summary>
        /// Enable or disable debug logging at runtime.
        /// </summary>
        public static void SetDebugLogging(bool enabled)
        {
            Instance.enableDebugLogging = enabled;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Enforce singleton pattern
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[InputManager] Duplicate instance detected - destroying");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        #endregion

        #region Private Methods

        private void UpdateDebugDisplay()
        {
            activeLockCount = _lockStack.Count;
            currentLockReasons.Clear();
            currentLockReasons.AddRange(_lockStack);
        }

        #endregion

        #region Debug Utilities

        /// <summary>
        /// Print current input lock state to console.
        /// </summary>
        public static void PrintLockState()
        {
            Debug.Log($"[InputManager] Input Locked: {IsInputLocked}, Active Locks: {Instance._lockStack.Count}");

            if (Instance._lockStack.Count > 0)
            {
                Debug.Log($"[InputManager] Lock Stack (top to bottom):");
                int index = 0;
                foreach (var reason in Instance._lockStack)
                {
                    Debug.Log($"  [{index}] {reason}");
                    index++;
                }
            }
        }

        #endregion
    }
}
