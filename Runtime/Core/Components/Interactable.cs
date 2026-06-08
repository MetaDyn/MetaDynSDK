using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace MetaDyn
{
    /// <summary>
    /// Generic interactable object component for MetaDyn SDK.
    /// Supports mouse/touch click and customizable hotkey interaction.
    /// Fires UnityEvent when interacted with.
    /// </summary>
    public class Interactable : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Interaction Configuration")]
        [Tooltip("Key to press for interaction (default: E)")]
        public KeyCode interactionKey = KeyCode.E;

        [Tooltip("Maximum distance player can interact from")]
        [Range(1f, 10f)]
        public float interactionRange = 3f;

        [Tooltip("Can this object be clicked/tapped?")]
        public bool clickable = true;

        [Tooltip("Can this object be activated via hotkey?")]
        public bool useHotkey = true;

        [Header("Visual Feedback")]
        [Tooltip("Material to apply when hovering (optional)")]
        public Material hoverMaterial;

        [Tooltip("Scale multiplier when hovering (1.0 = no change)")]
        [Range(0.9f, 1.2f)]
        public float hoverScale = 1.05f;

        [Header("Runtime Indicator")]
        [Tooltip("Show a visual indicator in the game (not just editor gizmos)")]
        public bool showRuntimeIndicator = true;

        [Tooltip("Sprite to display as interaction indicator (will billboard to face camera)")]
        public Sprite interactionSprite;

        [Tooltip("Size of the sprite indicator")]
        public float indicatorSize = 1f;

        [Tooltip("Color tint of the sprite indicator")]
        public Color indicatorColor = Color.white;

        [Tooltip("Height offset for the sprite above the object")]
        public float indicatorHeightOffset = 1f;

        [Header("Hover Text")]
        [Tooltip("Text to display when in range (e.g., 'Press E to interact')")]
        public string hoverText = "Press E to interact";

        [Tooltip("Optional TextMeshPro component to use for hover text (auto-creates if not assigned)")]
        public TMPro.TMP_Text hoverTextComponent;

        [Tooltip("Font size for auto-created hover text")]
        public float hoverTextSize = 24f;

        [Tooltip("Color for hover text")]
        public Color hoverTextColor = Color.white;

        [Tooltip("Height offset for hover text below the sprite")]
        public float hoverTextHeightOffset = 0.5f;

        [Header("Events")]
        [Tooltip("Fired when player interacts with this object")]
        public UnityEvent OnInteract;

        [Tooltip("Fired when player enters interaction range")]
        public UnityEvent OnEnterRange;

        [Tooltip("Fired when player exits interaction range")]
        public UnityEvent OnExitRange;

        // Private state
        private bool _isInRange = false;
        private bool _isHovering = false;
        private Transform _playerTransform;
        private Renderer _renderer;
        private Material _originalMaterial;
        private Vector3 _originalScale;
        private GameObject _runtimeIndicator;

        void Start()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer != null && hoverMaterial != null)
            {
                _originalMaterial = _renderer.material;
            }
            _originalScale = transform.localScale;

            if (showRuntimeIndicator)
            {
                CreateRuntimeIndicator();
            }

            // If standalone hover text component is assigned, start it hidden
            if (hoverTextComponent != null && hoverTextComponent.transform.parent != _runtimeIndicator?.transform)
            {
                hoverTextComponent.gameObject.SetActive(false);
            }
        }

        private void CreateRuntimeIndicator()
        {
            if (interactionSprite == null)
            {
                // We'll skip warning here to keep the console clean for scenes without indicators
                return;
            }

            // Create a sprite to display the indicator
            _runtimeIndicator = new GameObject("InteractionIndicator");
            _runtimeIndicator.transform.SetParent(transform);
            _runtimeIndicator.transform.localPosition = new Vector3(0, indicatorHeightOffset, 0);
            _runtimeIndicator.transform.localRotation = Quaternion.identity;

            // Add SpriteRenderer
            var spriteRenderer = _runtimeIndicator.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = interactionSprite;
            spriteRenderer.color = indicatorColor;

            // Set sorting order to render on top
            spriteRenderer.sortingOrder = 100;

            // Scale the sprite
            _runtimeIndicator.transform.localScale = Vector3.one * indicatorSize;

            // Create hover text if not assigned
            if (hoverTextComponent == null && !string.IsNullOrEmpty(hoverText))
            {
                // Create a child GameObject for the text
                GameObject textObj = new GameObject("HoverText");
                textObj.transform.SetParent(_runtimeIndicator.transform);
                textObj.transform.localPosition = new Vector3(0, -hoverTextHeightOffset, 0);
                textObj.transform.localRotation = Quaternion.identity;

                // Add TextMeshPro component
                hoverTextComponent = textObj.AddComponent<TMPro.TextMeshPro>();
                hoverTextComponent.text = hoverText;
                hoverTextComponent.fontSize = hoverTextSize;
                hoverTextComponent.color = hoverTextColor;
                hoverTextComponent.alignment = TMPro.TextAlignmentOptions.Center;

                // Set sorting order on the renderer
                hoverTextComponent.GetComponent<MeshRenderer>().sortingOrder = 101;
            }

            // Start hidden (will show when in range)
            _runtimeIndicator.SetActive(false);
        }

        private void UpdateIndicatorVisibility()
        {
            if (_runtimeIndicator != null)
            {
                // Show indicator only when in range
                _runtimeIndicator.SetActive(_isInRange);
            }

            // Handle standalone hover text component (if assigned separately)
            if (hoverTextComponent != null && hoverTextComponent.transform.parent != _runtimeIndicator?.transform)
            {
                hoverTextComponent.gameObject.SetActive(_isInRange);
            }

            // Update text content if component exists
            if (hoverTextComponent != null && !string.IsNullOrEmpty(hoverText))
            {
                hoverTextComponent.text = hoverText;
            }
        }

        void Update()
        {
            // Billboard the indicator to always face the camera
            if (_runtimeIndicator != null && _runtimeIndicator.activeSelf && Camera.main != null)
            {
                _runtimeIndicator.transform.rotation = Camera.main.transform.rotation;
            }

            // Billboard standalone hover text component
            if (hoverTextComponent != null &&
                hoverTextComponent.gameObject.activeSelf &&
                hoverTextComponent.transform.parent != _runtimeIndicator?.transform &&
                Camera.main != null)
            {
                hoverTextComponent.transform.rotation = Camera.main.transform.rotation;
            }

            // Find player if not already found
            if (_playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    _playerTransform = player.transform;
                }
            }

            // Check range
            if (_playerTransform != null)
            {
                float distance = Vector3.Distance(transform.position, _playerTransform.position);
                bool wasInRange = _isInRange;
                _isInRange = distance <= interactionRange;

                // Range events
                if (_isInRange && !wasInRange)
                {
                    OnEnterRange?.Invoke();
                    UpdateIndicatorVisibility();
                    
                    // Notify mobile HUD
                    
                }
                else if (!_isInRange && wasInRange)
                {
                    OnExitRange?.Invoke();
                    ResetVisuals();
                    UpdateIndicatorVisibility();
                    
                    // Unregister from mobile HUD
                    
                }

                // Hotkey interaction
                if (_isInRange && useHotkey && Input.GetKeyDown(interactionKey))
                {
                    // Check if input is locked
                    if (InputManager.IsInputLocked)
                    {
                        return;
                    }

                    Interact();
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!clickable || !_isInRange) return;
            _isHovering = true;
            ApplyHoverVisuals();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!clickable) return;
            _isHovering = false;
            ResetVisuals();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!clickable || !_isInRange) return;

            // Check if input is locked
            if (InputManager.IsInputLocked)
            {
                return;
            }

            Interact();
        }

        /// <summary>
        /// Trigger interaction programmatically
        /// </summary>
        public void Interact()
        {
            Debug.Log($"[Interactable] {gameObject.name} interacted");
            OnInteract?.Invoke();
        }

        /// <summary>
        /// Check if player is currently in range
        /// </summary>
        public bool IsInRange()
        {
            return _isInRange;
        }

        /// <summary>
        /// Get current distance to player
        /// </summary>
        public float GetDistanceToPlayer()
        {
            if (_playerTransform == null) return float.MaxValue;
            return Vector3.Distance(transform.position, _playerTransform.position);
        }

        private void ApplyHoverVisuals()
        {
            // Apply hover material
            if (_renderer != null && hoverMaterial != null)
            {
                _renderer.material = hoverMaterial;
            }

            // Apply hover scale
            if (hoverScale != 1.0f)
            {
                transform.localScale = _originalScale * hoverScale;
            }
        }

        private void ResetVisuals()
        {
            // Reset material
            if (_renderer != null && _originalMaterial != null)
            {
                _renderer.material = _originalMaterial;
            }

            // Reset scale
            transform.localScale = _originalScale;
        }

        void OnDestroy()
        {
            // Clean up runtime indicator
            if (_runtimeIndicator != null)
            {
                Destroy(_runtimeIndicator);
            }
            
            // Clean up from HUD
            
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // Draw interaction range sphere
            Gizmos.color = _isInRange ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactionRange);

            // Draw icon at object position
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(transform.position, 0.1f);
        }

        void OnDrawGizmosSelected()
        {
            // Draw detailed interaction range when selected
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawSphere(transform.position, interactionRange);
        }
#endif

        private void OnEnable()
        {
            MobileInteractionHUD.RegisterInteractable(this);
        }

        private void OnDisable()
        {
            MobileInteractionHUD.UnregisterInteractable(this);
        }
    }
}
