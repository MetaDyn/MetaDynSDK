using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using MetaDyn;

namespace MetaDyn.Networking
{
    /// <summary>
    /// Synchronizes player speaking state from Vivox across the network to drive the NameTag indicator.
    /// This replaces the legacy WebRTC-based speaking indication.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class MetaDynVivoxSpeakingIndicator : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private NameTag nameTag;

        private readonly NetworkVariable<bool> _isSpeaking = new NetworkVariable<bool>(
            false, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Owner
        );

        private void Awake()
        {
            if (nameTag == null)
                nameTag = GetComponentInChildren<NameTag>(true);
        }

        public override void OnNetworkSpawn()
        {
            if (nameTag == null)
                nameTag = GetComponentInChildren<NameTag>(true);

            _isSpeaking.OnValueChanged += OnSpeakingStateChanged;
            
            // Sync initial state
            if (nameTag != null)
                nameTag.SetSpeaking(_isSpeaking.Value);
            
            Debug.Log($"[MetaDyn VOICE] Speaking indicator spawned for {gameObject.name}. Owner={IsOwner}");
        }

        public override void OnNetworkDespawn()
        {
            _isSpeaking.OnValueChanged -= OnSpeakingStateChanged;
        }

        private void Update()
        {
            // Only the owner determines their own speaking state from their local Vivox instance
            if (IsOwner && VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
            {
                bool currentlySpeaking = CheckLocalSpeakingState();
                if (currentlySpeaking != _isSpeaking.Value)
                {
                    _isSpeaking.Value = currentlySpeaking;
                }
            }
        }

        private bool CheckLocalSpeakingState()
        {
            // Check if the local participant is speaking in any active channel
            // ActiveChannels is a dictionary where Value is the participant collection
            foreach (var channel in VivoxService.Instance.ActiveChannels)
            {
                foreach (var participant in channel.Value)
                {
                    if (participant.IsSelf)
                    {
                        return participant.SpeechDetected;
                    }
                }
            }
            return false;
        }

        private void OnSpeakingStateChanged(bool previousValue, bool newValue)
        {
            if (nameTag != null)
            {
                nameTag.SetSpeaking(newValue);
            }
        }
    }
}