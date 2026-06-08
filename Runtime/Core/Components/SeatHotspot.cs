using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using MetaDyn.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MetaDyn
{
    /// <summary>
    /// Marks a position where avatars can sit down.
    /// Updated for NGO/UGS compatibility.
    /// </summary>
    public class SeatHotspot : MonoBehaviour
    {
        [Header("Seat Configuration")]
        [Tooltip("When enabled, avatars will face the forward direction (blue arrow).")]
        public bool forceAvatarOrientation = true;

        [Tooltip("Priority for auto-assignment. Lower numbers = higher priority.")]
        public int priority = 0;

        [Header("Animation")]
        [Tooltip("Animation clip to play when sitting")]
        public AnimationClip sitAnimation;

        [Tooltip("Animator controller to use when sitting (optional)")]
        public RuntimeAnimatorController sitAnimatorController;

        [Tooltip("If using Animator, the trigger parameter name to start sitting")]
        public string sitTriggerName = "Sit";

        [Tooltip("If using Animator, the trigger parameter name to stand up")]
        public string standTriggerName = "Stand";

        [Header("Interaction")]
        [Tooltip("Distance player must be within to interact with seat")]
        public float interactionRange = 2f;

        [Tooltip("Key to press to sit/stand")]
        public KeyCode interactionKey = KeyCode.E;

        [Header("Position Adjustment")]
        [Tooltip("Offset from this transform where the avatar will be positioned")]
        public Vector3 seatOffset = Vector3.zero;

        [Header("Visual Indicator")]
        [Tooltip("Show a visual indicator in the game")]
        public bool showRuntimeIndicator = true;

        [Tooltip("Sprite to display as seat indicator")]
        public Sprite seatIndicatorSprite;

        [Tooltip("Size of the sprite indicator")]
        public float indicatorSize = 1f;

        [Tooltip("Color tint of the sprite indicator")]
        public Color indicatorColor = Color.white;

        [Tooltip("Height offset for the sprite above the seat")]
        public float indicatorHeightOffset = 0.5f;

        // Runtime state
        private GameObject _occupyingAvatar;
        private bool _isOccupied = false;
        private Animator _avatarAnimator;
        private RuntimeAnimatorController _originalController;
        private GameObject _runtimeIndicator;

        public bool IsOccupied => _isOccupied;
        public GameObject OccupyingAvatar => _occupyingAvatar;

        private void Start()
        {
            if (showRuntimeIndicator)
            {
                CreateRuntimeIndicator();
            }
        }

        private void CreateRuntimeIndicator()
        {
            if (seatIndicatorSprite == null) return;

            _runtimeIndicator = new GameObject("SeatIndicator");
            _runtimeIndicator.transform.SetParent(transform);
            _runtimeIndicator.transform.localPosition = new Vector3(0, indicatorHeightOffset, 0);
            _runtimeIndicator.transform.localRotation = Quaternion.identity;

            var spriteRenderer = _runtimeIndicator.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = seatIndicatorSprite;
            spriteRenderer.color = indicatorColor;
            spriteRenderer.sortingOrder = 100;

            _runtimeIndicator.transform.localScale = Vector3.one * indicatorSize;
        }

        private void UpdateIndicatorVisibility()
        {
            if (_runtimeIndicator != null)
            {
                _runtimeIndicator.SetActive(!_isOccupied);
            }
        }

        private void LateUpdate()
        {
            if (_runtimeIndicator != null && _runtimeIndicator.activeSelf && Camera.main != null)
            {
                _runtimeIndicator.transform.rotation = Camera.main.transform.rotation;
            }
        }

        private void Update()
        {
            if (!_isOccupied)
            {
                CheckForPlayerInteraction();
            }
            else
            {
                CheckForStandUp();
            }
        }

        private void CheckForPlayerInteraction()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;
            
            var localPlayerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (localPlayerObj == null) return;

            float distance = Vector3.Distance(transform.position, localPlayerObj.transform.position);
            
            if (distance <= interactionRange && Input.GetKeyDown(interactionKey))
            {
                SitDown(localPlayerObj.gameObject);
            }
        }

        private void CheckForStandUp()
        {
            if (Input.GetKeyDown(interactionKey))
            {
                StandUp();
            }
        }

        public bool SitDown(GameObject avatar)
        {
            if (_isOccupied) return false;

            _occupyingAvatar = avatar;
            _isOccupied = true;

            Vector3 seatPosition = transform.position + transform.TransformDirection(seatOffset);
            avatar.transform.position = seatPosition;

            if (forceAvatarOrientation)
            {
                avatar.transform.rotation = transform.rotation;
            }

            DisableAvatarMovement(avatar);
            PlaySitAnimation(avatar);
            UpdateIndicatorVisibility();

            Debug.Log($"[MetaDyn.SeatHotspot] {avatar.name} sat down");
            return true;
        }

        public void StandUp()
        {
            if (!_isOccupied) return;

            StopSitAnimation();
            EnableAvatarMovement(_occupyingAvatar);

            _isOccupied = false;
            _occupyingAvatar = null;
            UpdateIndicatorVisibility();
        }

        private void PlaySitAnimation(GameObject avatar)
        {
            _avatarAnimator = avatar.GetComponent<Animator>();
            if (_avatarAnimator == null) return;

            _originalController = _avatarAnimator.runtimeAnimatorController;

            if (sitAnimation != null)
            {
                var overrideController = new AnimatorOverrideController(_originalController);
                var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrideController.GetOverrides(clips);
                
                for (int i = 0; i < clips.Count; i++)
                {
                    clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clips[i].Key, sitAnimation);
                }
                overrideController.ApplyOverrides(clips);
                
                _avatarAnimator.runtimeAnimatorController = overrideController;
                _avatarAnimator.Rebind();
            }
            else if (sitAnimatorController != null)
            {
                _avatarAnimator.runtimeAnimatorController = sitAnimatorController;
                if (!string.IsNullOrEmpty(sitTriggerName))
                {
                    _avatarAnimator.SetTrigger(sitTriggerName);
                }
            }
        }

        private void StopSitAnimation()
        {
            if (_avatarAnimator == null) return;

            if (_originalController != null)
            {
                _avatarAnimator.runtimeAnimatorController = _originalController;
                _avatarAnimator.Rebind();
                _originalController = null;
            }
        }

        private void DisableAvatarMovement(GameObject avatar)
        {
            var ugsController = avatar.GetComponent<MetaDynUGSPlayerController>();
            if (ugsController != null)
            {
                ugsController.enabled = false;
            }

            var cc = avatar.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
            }
        }

        private void EnableAvatarMovement(GameObject avatar)
        {
            var ugsController = avatar.GetComponent<MetaDynUGSPlayerController>();
            if (ugsController != null)
            {
                ugsController.enabled = true;
            }

            var cc = avatar.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = true;
            }
        }

        public void ForceStandUp()
        {
            StandUp();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            const float RADIUS = 0.3f;
            Gizmos.color = _isOccupied ? Color.red : Color.green;
            Gizmos.DrawWireSphere(transform.position, RADIUS);
            Gizmos.color = new Color(0, 1, 0, 0.1f);
            Gizmos.DrawWireSphere(transform.position, interactionRange);

            if (forceAvatarOrientation)
            {
                Vector3 arrowPos = transform.position + (transform.forward * RADIUS);
                Quaternion arrowRot = Quaternion.LookRotation(transform.forward);
                Handles.color = Color.cyan;
                Handles.ArrowHandleCap(-1, arrowPos, arrowRot, 0.4f, EventType.Repaint);
            }
        }
#endif
    }
}
