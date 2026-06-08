using UnityEngine;
using Unity.Netcode;
using System.Collections;

namespace MetaDyn
{
    /// <summary>
    /// Networked door component for MetaDyn SDK.
    /// Handles opening/closing with synchronization across all clients.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class MetaDynDoor : NetworkBehaviour
    {
        [Header("Door Configuration")]
        [Tooltip("The transform that actually moves (defaults to this transform if null)")]
        public Transform doorTransform;

        [Tooltip("How much the door rotates when open")]
        public Vector3 openRotation = new Vector3(0, 90, 0);
        
        [Tooltip("How much the door moves when open (for sliding doors)")]
        public Vector3 openPositionOffset = Vector3.zero;

        [Tooltip("Time in seconds to open/close")]
        public float transitionTime = 1.0f;

        [Tooltip("Curve for the transition animation")]
        public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Audio")]
        public AudioSource audioSource;
        public AudioClip openClip;
        public AudioClip closeClip;

        // Synchronized state
        private NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Vector3 _closedRotation;
        private Vector3 _closedPosition;
        private Coroutine _transitionCoroutine;
        private Interactable _interactable;

        private void Awake()
        {
            if (doorTransform == null) doorTransform = transform;
            _closedRotation = doorTransform.localEulerAngles;
            _closedPosition = doorTransform.localPosition;
            
            _interactable = GetComponent<Interactable>();
        }

        public override void OnNetworkSpawn()
        {
            _isOpen.OnValueChanged += OnDoorStateChanged;
            
            // Set initial state
            ApplyDoorState(_isOpen.Value, true);
            
            if (_interactable != null)
            {
                _interactable.OnInteract.AddListener(ToggleDoor);
                UpdateInteractableText(_isOpen.Value);
            }
        }

        private void ToggleDoor()
        {
            if (!IsClient) return;
            ToggleDoorServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ToggleDoorServerRpc()
        {
            _isOpen.Value = !_isOpen.Value;
        }

        private void OnDoorStateChanged(bool previousValue, bool newValue)
        {
            ApplyDoorState(newValue, false);
            
            if (_interactable != null)
            {
                UpdateInteractableText(newValue);
            }
        }

        private void ApplyDoorState(bool open, bool immediate)
        {
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
            }

            if (immediate)
            {
                doorTransform.localEulerAngles = open ? _closedRotation + openRotation : _closedRotation;
                doorTransform.localPosition = open ? _closedPosition + openPositionOffset : _closedPosition;
            }
            else
            {
                _transitionCoroutine = StartCoroutine(TransitionDoor(open));
                
                if (audioSource != null)
                {
                    AudioClip clip = open ? openClip : closeClip;
                    if (clip != null) audioSource.PlayOneShot(clip);
                }
            }
        }

        private IEnumerator TransitionDoor(bool targetOpen)
        {
            Vector3 startRot = doorTransform.localEulerAngles;
            Vector3 targetRot = targetOpen ? _closedRotation + openRotation : _closedRotation;
            
            Vector3 startPos = doorTransform.localPosition;
            Vector3 targetPos = targetOpen ? _closedPosition + openPositionOffset : _closedPosition;
            
            float elapsed = 0;
            while (elapsed < transitionTime)
            {
                elapsed += Time.deltaTime;
                float t = transitionCurve.Evaluate(elapsed / transitionTime);
                
                doorTransform.localEulerAngles = Vector3.LerpUnclamped(startRot, targetRot, t);
                doorTransform.localPosition = Vector3.LerpUnclamped(startPos, targetPos, t);
                
                yield return null;
            }
            
            doorTransform.localEulerAngles = targetRot;
            doorTransform.localPosition = targetPos;
            _transitionCoroutine = null;
        }

        private void UpdateInteractableText(bool open)
        {
            if (_interactable != null)
            {
                _interactable.hoverText = open ? "Press E to Close Door" : "Press E to Open Door";
            }
        }
    }
}
