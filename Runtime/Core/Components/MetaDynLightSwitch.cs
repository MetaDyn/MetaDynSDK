using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace MetaDyn
{
    /// <summary>
    /// Networked light switch component for MetaDyn SDK.
    /// Toggles one or more lights with synchronization across all clients.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class MetaDynLightSwitch : NetworkBehaviour
    {
        [Header("Switch Configuration")]
        [Tooltip("Lights to be toggled by this switch")]
        public List<Light> targetLights = new List<Light>();

        [Tooltip("Optional: Mesh renderers to swap materials or toggle Emission")]
        public List<Renderer> targetRenderers = new List<Renderer>();

        [Header("Audio")]
        public AudioSource audioSource;
        public AudioClip switchOnClip;
        public AudioClip switchOffClip;

        // Synchronized state
        private NetworkVariable<bool> _isOn = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private Interactable _interactable;

        private void Awake()
        {
            _interactable = GetComponent<Interactable>();
        }

        public override void OnNetworkSpawn()
        {
            _isOn.OnValueChanged += OnSwitchStateChanged;
            
            // Set initial state
            ApplySwitchState(_isOn.Value, true);
            
            if (_interactable != null)
            {
                _interactable.OnInteract.AddListener(ToggleSwitch);
                UpdateInteractableText(_isOn.Value);
            }
        }

        private void ToggleSwitch()
        {
            if (!IsClient) return;
            ToggleSwitchServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ToggleSwitchServerRpc()
        {
            _isOn.Value = !_isOn.Value;
        }

        private void OnSwitchStateChanged(bool previousValue, bool newValue)
        {
            ApplySwitchState(newValue, false);
            
            if (_interactable != null)
            {
                UpdateInteractableText(newValue);
            }
        }

        private void ApplySwitchState(bool on, bool immediate)
        {
            foreach (var light in targetLights)
            {
                if (light != null) light.enabled = on;
            }

            foreach (var renderer in targetRenderers)
            {
                if (renderer != null)
                {
                    // For now, just toggle visibility or we could swap materials later
                    // renderer.enabled = on;
                    
                    // Better: Toggle Emission keyword if it exists
                    foreach (var mat in renderer.materials)
                    {
                        if (on) mat.EnableKeyword("_EMISSION");
                        else mat.DisableKeyword("_EMISSION");
                    }
                }
            }

            if (!immediate && audioSource != null)
            {
                AudioClip clip = on ? switchOnClip : switchOffClip;
                if (clip != null) audioSource.PlayOneShot(clip);
            }
        }

        private void UpdateInteractableText(bool on)
        {
            if (_interactable != null)
            {
                _interactable.hoverText = on ? "Press E to Turn Off Light" : "Press E to Turn On Light";
            }
        }
    }
}
