using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

namespace MetaDyn
{
    /// <summary>
    /// Singleton HUD manager that displays a context-sensitive interaction button for mobile users.
    /// </summary>
    public class MobileInteractionHUD : MonoBehaviour
    {
        private static MobileInteractionHUD _instance;
        public static MobileInteractionHUD Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<MobileInteractionHUD>();

                }
                return _instance;
            }
        }

        [Header("UI Configuration")]
        public GameObject interactionButton;
        public TMPro.TMP_Text interactionText;
        public KeyCode mobileInteractionKey = KeyCode.E;

        private List<Interactable> _activeInteractables = new List<Interactable>();
        private Interactable _nearestInteractable;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            if (interactionButton != null)
            {
                interactionButton.SetActive(false);
            }
        }

        public static void RegisterInteractable(Interactable interactable)
        {
            if (Instance == null) return;
            if (!Instance._activeInteractables.Contains(interactable))
            {
                Instance._activeInteractables.Add(interactable);
            }
        }

        public static void UnregisterInteractable(Interactable interactable)
        {
            if (_instance == null) return;
            if (Instance._activeInteractables.Contains(interactable))
            {
                Instance._activeInteractables.Remove(interactable);
            }
        }

        private void Update()
        {
            // Only show on mobile or when forced? 
            // For now we'll show if any interactable is in range, 
            // but in a real build we might check Application.isMobilePlatform.
            
            UpdateNearestInteractable();
            UpdateHUDVisibility();
        }

        private void UpdateNearestInteractable()
        {
            if (_activeInteractables.Count == 0)
            {
                _nearestInteractable = null;
                return;
            }

            // Clean up nulls
            _activeInteractables.RemoveAll(i => i == null);

            // Find nearest
            _nearestInteractable = _activeInteractables
                .OrderBy(i => i.GetDistanceToPlayer())
                .FirstOrDefault();
        }

        private void UpdateHUDVisibility()
        {
            if (interactionButton == null) return;

            bool shouldShow = _nearestInteractable != null && _nearestInteractable.IsInRange();
            
            if (shouldShow != interactionButton.activeSelf)
            {
                interactionButton.SetActive(shouldShow);
            }

            if (shouldShow && interactionText != null)
            {
                interactionText.text = _nearestInteractable.hoverText;
            }
        }

        public void OnInteractionButtonPressed()
        {
            if (_nearestInteractable != null)
            {
                _nearestInteractable.Interact();
            }
        }
    }
}
