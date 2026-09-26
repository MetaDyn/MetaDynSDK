using UnityEngine;
using Unity.Netcode;
using System.Runtime.InteropServices;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MetaDyn;
using MetaDyn.UserList;

// Data structures for WebRTC JSON parsing
[Serializable]
public class WebRTCSignal
{
    public string targetId;
    public string sdp;       // For Offers/Answers
    public string candidate; // For ICE Candidates
}

public class WebRTCManager : NetworkBehaviour
{
    // --- DLL IMPORTS FROM .JSLIB ---
    [DllImport("__Internal")]
    private static extern void WebRTC_Init(int playerId);

    [DllImport("__Internal")]
    private static extern void WebRTC_Connect(string peerId, bool isInitiator);

    [DllImport("__Internal")]
    private static extern void WebRTC_HandleSignal(string peerId, string jsonSignal);

    [DllImport("__Internal")]
    private static extern void WebRTC_DisconnectPeer(string peerId);

    [DllImport("__Internal")]
    private static extern void WebRTC_SetMicrophoneMuted(bool muted);

    #if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int WebRTC_GetAudioSamples(string peerId, float[] buffer, int sampleCount);

    [DllImport("__Internal")]
    private static extern int WebRTC_GetAudioSampleRate(string peerId);

    [DllImport("__Internal")]
    private static extern void WebRTC_UpdatePeerPosition(string peerId, float x, float y, float z);

    [DllImport("__Internal")]
    private static extern void WebRTC_UpdateListenerPosition(float x, float y, float z, float forwardX, float forwardY, float forwardZ, float upX, float upY, float upZ);

    // NEW: Function to update spatial settings from Unity
    [DllImport("__Internal")]
    private static extern void WebRTC_SetSpatialAudioParams(float minDistance, float maxDistance, float rolloffFactor);
    #endif

    // --- INSPECTOR SETTINGS ---
    [Header("Spatial Audio Settings")]
    [Tooltip("Distance at which volume starts to drop. Smaller = more intimate.")]
    [SerializeField] private float minDistance = 1.0f; 
    
    [Tooltip("Distance at which sound becomes completely silent. Smaller = faster fall-off.")]
    [SerializeField] private float maxDistance = 20.0f;

    [Tooltip("How fast sound drops off. 1.0 is real world physics.")]
    [SerializeField] private float rolloffFactor = 1.0f;

    // --- PRIVATE FIELDS ---
    private HashSet<ulong> connectedPeers = new HashSet<ulong>();
    private bool isInitialized = false;
    private bool _previousMuteState = false; 
    private bool _hasAppliedInitialMute = false; 

    public readonly NetworkVariable<bool> IsSpeaking = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // NameTag logic removed - now handled by MetaDynVivoxSpeakingIndicator

    // Store references to lip sync components
    private Dictionary<string, MonoBehaviour> _lipSyncComponents = new Dictionary<string, MonoBehaviour>();

    // Audio level tracking for lip sync
    private Dictionary<string, float> _audioLevels = new Dictionary<string, float>();
    private Dictionary<string, bool> _isSpeaking = new Dictionary<string, bool>();
    private const float SPEAKING_THRESHOLD = 0.01f;
    private const int AUDIO_BUFFER_SIZE = 4096;
    private Dictionary<string, float[]> _audioBuffers = new Dictionary<string, float[]>();

    // JavaScript message receiver (so we don't rename the player GameObject)
    private GameObject _jsMessageReceiver;

    // --- NGO LIFECYCLE ---

    public override void OnNetworkSpawn()
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        if (IsOwner)
        {
            int localPlayerId = (int)NetworkManager.Singleton.LocalClientId;

            // Create a child GameObject for receiving JavaScript messages
            _jsMessageReceiver = new GameObject($"WebRTCManager_{localPlayerId}");
            _jsMessageReceiver.transform.SetParent(transform);
            _jsMessageReceiver.AddComponent<WebRTCJSMessageForwarder>().webRTCManager = this;

            // Init WebRTC
            WebRTC_Init(localPlayerId);

            // Apply Spatial Audio Settings from Inspector to the Browser
            Debug.Log($"[WebRTC] Applying spatial settings: Min={minDistance}, Max={maxDistance}");
            WebRTC_SetSpatialAudioParams(minDistance, maxDistance, rolloffFactor);

            isInitialized = true;
            Debug.Log($"[WebRTC] Initialized microphone for local player (ID: {localPlayerId})");
            
            // Connect to peers already present when this player object spawns.
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.ClientId == NetworkManager.LocalClientId) continue;
                InitiateWebRTCConnection(client.ClientId);
            }

            // OnClientConnectedCallback only reports the local client (and the server/session
            // owner). DA peer joins and departures are reported through OnConnectionEvent.
            NetworkManager.OnConnectionEvent += OnConnectionEvent;
        }
        else
        {
            // We are a remote player
            SetupSpatialAudioSource();
        }
        #else
        if (!IsOwner)
        {
            SetupSpatialAudioSource();
        }
        #endif
    }

    private void Update()
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        if (IsOwner && isInitialized && MetaDynUGSUserListManager.Instance != null)
        {
            // Sync Mute State
            if (MetaDynUGSUserListManager.Instance.TryGetUser(NetworkManager.LocalClientId, out var userData))
            {
                bool currentMuteState = userData.IsMuted;

                if (!_hasAppliedInitialMute)
                {
                    WebRTC_SetMicrophoneMuted(currentMuteState);
                    _previousMuteState = currentMuteState;
                    _hasAppliedInitialMute = true;
                }
                else if (currentMuteState != _previousMuteState)
                {
                    WebRTC_SetMicrophoneMuted(currentMuteState);
                    _previousMuteState = currentMuteState;
                }
            }

            // Sync Positions
            UpdateListenerPosition();
            UpdatePeerPositions();

            // Update lip sync
            UpdateLipSync();
        }
        #endif
    }

    #if UNITY_WEBGL && !UNITY_EDITOR
    private void UpdateLipSync()
    {
        foreach (var kvp in _lipSyncComponents.ToList())
        {
            string peerId = kvp.Key;
            MonoBehaviour lipSync = kvp.Value;

            if (lipSync == null || !_audioBuffers.ContainsKey(peerId))
                continue;

            // Pull audio samples from JavaScript
            int samplesReceived = WebRTC_GetAudioSamples(peerId, _audioBuffers[peerId], AUDIO_BUFFER_SIZE);

            if (samplesReceived > 0)
            {
                // Calculate RMS audio level
                float sum = 0f;
                for (int i = 0; i < samplesReceived; i++)
                {
                    sum += _audioBuffers[peerId][i] * _audioBuffers[peerId][i];
                }
                float rms = Mathf.Sqrt(sum / samplesReceived);

                // Smooth audio level
                _audioLevels[peerId] = Mathf.Lerp(_audioLevels[peerId], rms, 0.3f);

                // Check speaking threshold
                bool shouldBeSpeaking = _audioLevels[peerId] > SPEAKING_THRESHOLD;

                if (!_isSpeaking.ContainsKey(peerId)) _isSpeaking[peerId] = false;

                if (shouldBeSpeaking != _isSpeaking[peerId])
                {
                    _isSpeaking[peerId] = shouldBeSpeaking;

                    if (shouldBeSpeaking)
                    {
                        lipSync.SendMessage("StartSpeaking", SendMessageOptions.DontRequireReceiver);
                    }
                    else
                    {
                        lipSync.SendMessage("StopSpeaking", SendMessageOptions.DontRequireReceiver);
                    }
                }
            }
        }
        
        // Also update local IsSpeaking state based on own mic if needed (JS typically handles the browser mic state)
    }
    #endif

    #if UNITY_WEBGL && !UNITY_EDITOR
    private void UpdateListenerPosition()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        Transform camTransform = mainCamera.transform;
        Vector3 pos = camTransform.position;
        Vector3 forward = camTransform.forward;
        Vector3 up = camTransform.up;

        WebRTC_UpdateListenerPosition(
            pos.x, pos.y, pos.z,
            forward.x, forward.y, forward.z,
            up.x, up.y, up.z
        );
    }

    private void UpdatePeerPositions()
    {
        foreach (var clientId in connectedPeers)
        {
            string peerId = clientId.ToString();
            
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) &&
                client.PlayerObject != null)
            {
                Vector3 pos = client.PlayerObject.transform.position;
                WebRTC_UpdatePeerPosition(peerId, pos.x, pos.y, pos.z);
            }
        }
    }
    #endif

    public override void OnNetworkDespawn()
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        if (IsOwner)
        {
            NetworkManager.OnConnectionEvent -= OnConnectionEvent;

            foreach (var peerId in connectedPeers)
            {
                WebRTC_DisconnectPeer(peerId.ToString());
            }
            connectedPeers.Clear();

            // Clean up JavaScript message receiver
            if (_jsMessageReceiver != null)
            {
                Destroy(_jsMessageReceiver);
                _jsMessageReceiver = null;
            }
        }
        #endif

        _lipSyncComponents.Clear();
        _audioBuffers.Clear();
        _audioLevels.Clear();
        _isSpeaking.Clear();
    }

    private void SetupSpatialAudioSource()
    {
        Debug.Log($"[WebRTC] Remote player marked for spatial audio (handled by browser)");
    }

    // --- JAVASCRIPT CALLBACKS ---

    public void OnRemoteAudioStreamReady(string peerId)
    {
        Debug.Log($"[WebRTC] Remote audio stream ready for peer {peerId}");

        if (!IsOwner) return;

        ulong remoteClientId = ulong.Parse(peerId);

        if (NetworkManager.ConnectedClients.TryGetValue(remoteClientId, out var client) &&
            client.PlayerObject != null)
        {
            Debug.Log($"[WebRTC] Found PlayerObject for peer {peerId}: {client.PlayerObject.name}");

            // Look for Lip Sync Component on the remote player object
            MonoBehaviour lipSync = null;

            var avatarSDKLipSync = client.PlayerObject.GetComponentInChildren<AvatarSdkPlayerLipSync>();
            if (avatarSDKLipSync != null)
            {
                lipSync = avatarSDKLipSync;
            }
            else
            {
                var rpmLipSync = client.PlayerObject.GetComponentInChildren<Wolf3DPlayerLipSync>();
                if (rpmLipSync != null)
                {
                    lipSync = rpmLipSync;
                }
            }

            if (lipSync != null)
            {
                _lipSyncComponents[peerId] = lipSync;
                _audioBuffers[peerId] = new float[AUDIO_BUFFER_SIZE];
                _audioLevels[peerId] = 0f;
                _isSpeaking[peerId] = false;
                Debug.Log($"[WebRTC] Lip sync setup complete for peer {peerId}");
            }
        }
    }

    public void OnSpeakingStateChanged(string isSpeakingStr)
    {
        if (IsOwner)
        {
            IsSpeaking.Value = isSpeakingStr == "1" || isSpeakingStr.ToLower() == "true";
        }
    }

    // --- CONNECTION & SIGNALLING ---

    private void OnConnectionEvent(NetworkManager networkManager, ConnectionEventData eventData)
    {
        if (networkManager != NetworkManager || !IsOwner)
            return;

        switch (eventData.EventType)
        {
            case ConnectionEvent.ClientConnected:
                // A newly connected DA client receives the peers that were already present.
                // This also makes the path safe if the player NetworkObject subscribes before
                // NGO has populated ConnectedClientsList.
                if (eventData.ClientId == NetworkManager.LocalClientId && eventData.PeerClientIds.IsCreated)
                {
                    foreach (ulong peerClientId in eventData.PeerClientIds)
                    {
                        if (peerClientId != NetworkManager.LocalClientId)
                            InitiateWebRTCConnection(peerClientId);
                    }
                }
                else if (eventData.ClientId != NetworkManager.LocalClientId)
                {
                    InitiateWebRTCConnection(eventData.ClientId);
                }
                break;

            case ConnectionEvent.PeerConnected:
                InitiateWebRTCConnection(eventData.ClientId);
                break;

            case ConnectionEvent.ClientDisconnected:
            case ConnectionEvent.PeerDisconnected:
                DisconnectWebRTCPeer(eventData.ClientId);
                break;
        }
    }

    private void DisconnectWebRTCPeer(ulong clientId)
    {
        if (clientId == NetworkManager.LocalClientId || !connectedPeers.Remove(clientId))
            return;

        #if UNITY_WEBGL && !UNITY_EDITOR
        WebRTC_DisconnectPeer(clientId.ToString());
        #endif

        string peerId = clientId.ToString();
        _lipSyncComponents.Remove(peerId);
        _audioBuffers.Remove(peerId);
        _audioLevels.Remove(peerId);
        _isSpeaking.Remove(peerId);
        Debug.Log($"[WebRTC] Disconnected spatial voice peer {clientId}.");
    }

    private void InitiateWebRTCConnection(ulong clientId)
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        if (!isInitialized || clientId == NetworkManager.LocalClientId || !connectedPeers.Add(clientId))
            return;

        // Deterministic initiator: exactly one side creates the offer.
        bool shouldInitiate = NetworkManager.LocalClientId < clientId;
        WebRTC_Connect(clientId.ToString(), shouldInitiate);
        Debug.Log(
            $"[WebRTC] Registered spatial voice peer {clientId}. " +
            $"initiator={shouldInitiate}, distributedAuthority={NetworkManager.DistributedAuthorityMode}.");
        #endif
    }

    public void OnReceiveDescriptionFromJS(string json)
    {
        var data = JsonUtility.FromJson<WebRTCSignal>(json);
        ulong targetClientId = ulong.Parse(data.targetId);
        SendWebRTCSignal(targetClientId, data.sdp);
    }

    public void OnReceiveIceCandidateFromJS(string json)
    {
        var data = JsonUtility.FromJson<WebRTCSignal>(json);
        ulong targetClientId = ulong.Parse(data.targetId);
        SendWebRTCSignal(targetClientId, data.candidate);
    }

    private void SendWebRTCSignal(ulong targetClientId, string jsonSignal)
    {
        if (!IsOwner || targetClientId == NetworkManager.LocalClientId)
            return;

        ReceiveWebRTCSignalRpc(
            jsonSignal,
            RpcTarget.Single(targetClientId, RpcTargetUse.Temp));
    }

    /// <summary>
    /// Relays WebRTC SDP/ICE to one peer through NGO's supported targeted RPC path.
    /// The RPC is invoked on the sender's player object, then forwarded locally to the
    /// recipient's owned WebRTCManager, which is the only instance connected to JavaScript.
    /// </summary>
    [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Owner)]
    private void ReceiveWebRTCSignalRpc(string jsonSignal, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        NetworkObject localPlayerObject = NetworkManager.LocalClient?.PlayerObject;
        WebRTCManager localWebRtcManager = localPlayerObject != null
            ? localPlayerObject.GetComponent<WebRTCManager>()
            : null;

        if (localWebRtcManager == null || !localWebRtcManager.IsOwner)
        {
            Debug.LogWarning(
                $"[WebRTC] Could not route signaling from peer {senderClientId} to the local player.");
            return;
        }

        #if UNITY_WEBGL && !UNITY_EDITOR
        WebRTC_HandleSignal(senderClientId.ToString(), jsonSignal);
        #endif
    }
}
