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

    private const string SIGNAL_MESSAGE_NAME = "WebRTCSignal";

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
            
            // Listen for signals from other peers
            NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(SIGNAL_MESSAGE_NAME, OnReliableDataReceived);
            
            // Connect to existing players
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.ClientId == NetworkManager.LocalClientId) continue;
                InitiateWebRTCConnection(client.ClientId);
            }

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
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
            
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var client))
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
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SIGNAL_MESSAGE_NAME);

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

        if (NetworkManager.ConnectedClients.TryGetValue(remoteClientId, out var client))
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

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.LocalClientId) return;
        InitiateWebRTCConnection(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (connectedPeers.Contains(clientId))
        {
            WebRTC_DisconnectPeer(clientId.ToString());
            connectedPeers.Remove(clientId);
            _lipSyncComponents.Remove(clientId.ToString());
        }
    }

    private void InitiateWebRTCConnection(ulong clientId)
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        if (!isInitialized) return;

        // Deterministic initiator: the one with the lower ID
        bool shouldInitiate = NetworkManager.LocalClientId < clientId;
        WebRTC_Connect(clientId.ToString(), shouldInitiate);
        connectedPeers.Add(clientId);
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
        var wrapper = new SignalWrapper { senderId = (int)NetworkManager.LocalClientId, signal = jsonSignal };
        string wrappedSignal = JsonUtility.ToJson(wrapper);
        
        var writer = new FastBufferWriter(wrappedSignal.Length * 2 + 4, Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(wrappedSignal);
        NetworkManager.CustomMessagingManager.SendNamedMessage(SIGNAL_MESSAGE_NAME, targetClientId, writer);
        writer.Dispose();
    }

    private void OnReliableDataReceived(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string wrappedSignal);
        var wrapper = JsonUtility.FromJson<SignalWrapper>(wrappedSignal);
        string jsonSignal = wrapper.signal;

        #if UNITY_WEBGL && !UNITY_EDITOR
        WebRTC_HandleSignal(senderId.ToString(), jsonSignal);
        #endif
    }

    [Serializable]
    private class SignalWrapper
    {
        public int senderId;
        public string signal;
    }
}
