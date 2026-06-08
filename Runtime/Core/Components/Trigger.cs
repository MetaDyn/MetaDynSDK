using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;

namespace MetaDyn
{
    /// <summary>
    /// Local trigger zone component for MetaDyn SDK.
    /// Fires UnityEvents when local player enters/exits trigger zone.
    /// Updated for NGO/UGS compatibility.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Trigger : MonoBehaviour
    {
        [Header("Trigger Configuration")]
        [Tooltip("Should this trigger only activate once?")]
        public bool triggerOnce = false;

        [Tooltip("Cooldown time before trigger can activate again (seconds)")]
        [Range(0f, 60f)]
        public float cooldownTime = 0f;

        [Tooltip("Show trigger zone in game (not just editor gizmos)")]
        public bool showRuntimeIndicator = false;

        [Header("Visual Indicator")]
        [Tooltip("Color of the trigger zone indicator")]
        public Color indicatorColor = new Color(0, 1, 0, 0.3f);

        [Header("Events - LOCAL ONLY")]
        [Tooltip("Fired when LOCAL player enters trigger zone")]
        public UnityEvent OnEnter;

        [Tooltip("Fired when LOCAL player exits trigger zone")]
        public UnityEvent OnExit;

        // Private state
        private bool _hasTriggered = false;
        private float _lastTriggerTime = -999f;
        private bool _playerInside = false;
        private Collider _triggerCollider;
        private GameObject _runtimeIndicator;

        void Start()
        {
            // Verify collider setup
            _triggerCollider = GetComponent<Collider>();
            if (_triggerCollider == null)
            {
                Debug.LogError("[MetaDyn.Trigger] No Collider found! Add a Collider component with 'Is Trigger' enabled.");
                enabled = false;
                return;
            }

            if (!_triggerCollider.isTrigger)
            {
                Debug.LogWarning($"[MetaDyn.Trigger] Collider on {gameObject.name} is not set to 'Is Trigger'. Setting it now.");
                _triggerCollider.isTrigger = true;
            }

            if (showRuntimeIndicator)
            {
                CreateRuntimeIndicator();
            }
        }

        private void CreateRuntimeIndicator()
        {
            // Create a visual representation of the trigger zone
            _runtimeIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _runtimeIndicator.name = "TriggerIndicator";
            _runtimeIndicator.transform.SetParent(transform);
            _runtimeIndicator.transform.localPosition = Vector3.zero;
            _runtimeIndicator.transform.localRotation = Quaternion.identity;

            // Match the trigger collider size
            if (_triggerCollider is BoxCollider boxCollider)
            {
                _runtimeIndicator.transform.localScale = boxCollider.size;
            }

            // Make it semi-transparent
            Renderer renderer = _runtimeIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Use URP shader
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }

                if (shader != null)
                {
                    Material mat = new Material(shader);
                    mat.color = indicatorColor;

                    // Set URP transparent surface type
                    mat.SetFloat("_Surface", 1); // 0 = Opaque, 1 = Transparent
                    mat.SetFloat("_Blend", 0); // 0 = Alpha, 1 = Premultiply, 2 = Additive, 3 = Multiply
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3000;

                    // Enable transparency keywords for URP
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");

                    renderer.material = mat;
                }
            }

            // Remove the collider from the indicator
            Collider indicatorCollider = _runtimeIndicator.GetComponent<Collider>();
            if (indicatorCollider != null)
            {
                Destroy(indicatorCollider);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            // Only process for local player
            if (!IsLocalPlayer(other.gameObject))
                return;

            // Check if already triggered and set to trigger once
            if (triggerOnce && _hasTriggered)
                return;

            // Check cooldown
            if (Time.time < _lastTriggerTime + cooldownTime)
                return;

            _playerInside = true;
            _hasTriggered = true;
            _lastTriggerTime = Time.time;

            Debug.Log($"[MetaDyn.Trigger] Local player entered trigger zone: {gameObject.name}");
            OnEnter?.Invoke();
        }

        void OnTriggerExit(Collider other)
        {
            // Only process for local player
            if (!IsLocalPlayer(other.gameObject))
                return;

            if (!_playerInside)
                return;

            _playerInside = false;

            Debug.Log($"[MetaDyn.Trigger] Local player exited trigger zone: {gameObject.name}");
            OnExit?.Invoke();
        }

        /// <summary>
        /// Check if the GameObject is the local player
        /// </summary>
        private bool IsLocalPlayer(GameObject obj)
        {
            // Check for NGO NetworkObject
            var networkObject = obj.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                // Only trigger for local player (IsOwner)
                return networkObject.IsOwner;
            }

            // Check parent for NetworkObject
            var parentNetworkObject = obj.GetComponentInParent<NetworkObject>();
            if (parentNetworkObject != null)
            {
                return parentNetworkObject.IsOwner;
            }

            // Fallback: Check for Player tag
            if (obj.CompareTag("Player"))
            {
                return true;
            }

            return false;
        }

        public bool IsPlayerInside()
        {
            return _playerInside;
        }

        public void ResetTrigger()
        {
            _hasTriggered = false;
            _playerInside = false;
            Debug.Log($"[MetaDyn.Trigger] Trigger reset: {gameObject.name}");
        }

        public void ForceEnter()
        {
            OnEnter?.Invoke();
        }

        public void ForceExit()
        {
            OnExit?.Invoke();
        }

        void OnDestroy()
        {
            if (_runtimeIndicator != null)
            {
                Destroy(_runtimeIndicator);
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = _playerInside ? Color.green : new Color(0, 1, 0, 0.3f);

            if (col is BoxCollider boxCollider)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(boxCollider.center, boxCollider.size);
                Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
            }
            else if (col is SphereCollider sphereCollider)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawSphere(sphereCollider.center, sphereCollider.radius);
                Gizmos.DrawWireSphere(sphereCollider.center, sphereCollider.radius);
            }
            else if (col is CapsuleCollider capsuleCollider)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(capsuleCollider.center, capsuleCollider.radius);
            }
        }
#endif
    }
}
