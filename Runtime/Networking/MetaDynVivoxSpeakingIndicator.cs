using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using MetaDyn;

namespace MetaDyn.Networking
{
    /// <summary>
    /// Drives the networked NameTag speaking indicator from Vivox participant updates on
    /// supported native platforms and from the existing browser WebRTC analyser on WebGL,
    /// where Vivox does not expose SpeechDetected participant updates.
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

#if UNITY_WEBGL
        private WebRTCManager _webRtcManager;
#endif

        private void Awake()
        {
            if (nameTag == null)
                nameTag = GetComponentInChildren<NameTag>(true);

#if UNITY_WEBGL
            _webRtcManager = GetComponent<WebRTCManager>();
#endif
        }

        public override void OnNetworkSpawn()
        {
            if (nameTag == null)
                nameTag = GetComponentInChildren<NameTag>(true);

#if UNITY_WEBGL
            // Vivox WebGL does not expose participant SpeechDetected updates. Reuse the
            // existing browser WebRTC microphone analyser and its owner-written state.
            if (_webRtcManager != null)
            {
                _webRtcManager.IsSpeaking.OnValueChanged += OnSpeakingStateChanged;
                OnSpeakingStateChanged(false, _webRtcManager.IsSpeaking.Value);
            }
            else
            {
                Debug.LogWarning(
                    $"[MetaDyn VOICE] No WebRTCManager found on '{gameObject.name}'; " +
                    "WebGL speaking indication is unavailable.");
                OnSpeakingStateChanged(false, false);
            }
#else
            _isSpeaking.OnValueChanged += OnSpeakingStateChanged;
            OnSpeakingStateChanged(false, _isSpeaking.Value);
#endif
            
            Debug.Log($"[MetaDyn VOICE] Speaking indicator spawned for {gameObject.name}. Owner={IsOwner}");
        }

        public override void OnNetworkDespawn()
        {
#if UNITY_WEBGL
            if (_webRtcManager != null)
                _webRtcManager.IsSpeaking.OnValueChanged -= OnSpeakingStateChanged;
#else
            _isSpeaking.OnValueChanged -= OnSpeakingStateChanged;
#endif
        }

        private void Update()
        {
#if !UNITY_WEBGL
            // Only the owner determines their own speaking state from their local Vivox instance
            if (IsOwner && VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
            {
                bool currentlySpeaking = CheckLocalSpeakingState();
                if (currentlySpeaking != _isSpeaking.Value)
                {
                    _isSpeaking.Value = currentlySpeaking;
                }
            }
#endif
        }

#if !UNITY_WEBGL
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
#endif

        private void OnSpeakingStateChanged(bool previousValue, bool newValue)
        {
            if (nameTag != null)
            {
                nameTag.SetSpeaking(newValue);
            }
        }
    }
}
