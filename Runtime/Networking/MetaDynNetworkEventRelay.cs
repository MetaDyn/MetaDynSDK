using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDyn.Networking
{
    public enum MetaDynRpcAudience
    {
        Self,
        Others,
        Everyone
    }

    [Serializable]
    public sealed class MetaDynChannelEvent : UnityEvent<int> { }

    [Serializable]
    public sealed class MetaDynPayloadEvent : UnityEvent<string> { }

    [Serializable]
    public sealed class MetaDynEventNameEvent : UnityEvent<string> { }

    [Serializable]
    public sealed class MetaDynNetworkMessageEvent : UnityEvent<int, string, string> { }

    /// <summary>
    /// Inspector-friendly relay for sending lightweight scene events over the active NGO/UGS session.
    /// Requires this component's GameObject to have a spawned NetworkObject before remote RPCs can be sent.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class MetaDynNetworkEventRelay : NetworkBehaviour
    {
        [Header("Defaults")]
        [Tooltip("Default target audience used by Send methods that do not specify a target.")]
        [SerializeField] private MetaDynRpcAudience defaultAudience = MetaDynRpcAudience.Everyone;

        [Tooltip("Default byte-sized channel used by Send methods that do not specify a channel.")]
        [Range(0, byte.MaxValue)]
        [SerializeField] private int defaultChannel;

        [Tooltip("Optional event name included with outgoing messages.")]
        [SerializeField] private string defaultEventName = "default";

        [Header("Received Events")]
        [Tooltip("Invoked for every received network event.")]
        public UnityEvent OnReceived;

        [Tooltip("Invoked with the received channel as an int for UnityEvent compatibility.")]
        public MetaDynChannelEvent OnChannelReceived;

        [Tooltip("Invoked with the received payload string only.")]
        public MetaDynPayloadEvent OnPayloadReceived;

        [Tooltip("Invoked with the received event name string only.")]
        public MetaDynEventNameEvent OnEventNameReceived;

        [Tooltip("Invoked with channel, event name, and payload.")]
        public MetaDynNetworkMessageEvent OnMessageReceived;

        public int LastChannel { get; private set; }
        public string LastEventName { get; private set; } = string.Empty;
        public string LastPayload { get; private set; } = string.Empty;
        public ulong LastSenderClientId { get; private set; }

        public void Send()
        {
            Send(defaultAudience, ToChannel(defaultChannel), defaultEventName, string.Empty);
        }

        public void SendToSelf()
        {
            Send(MetaDynRpcAudience.Self, ToChannel(defaultChannel), defaultEventName, string.Empty);
        }

        public void SendToOthers()
        {
            Send(MetaDynRpcAudience.Others, ToChannel(defaultChannel), defaultEventName, string.Empty);
        }

        public void SendToEveryone()
        {
            Send(MetaDynRpcAudience.Everyone, ToChannel(defaultChannel), defaultEventName, string.Empty);
        }

        public void SendPayload(string payload)
        {
            Send(defaultAudience, ToChannel(defaultChannel), defaultEventName, payload);
        }

        public void SendPayloadToSelf(string payload)
        {
            Send(MetaDynRpcAudience.Self, ToChannel(defaultChannel), defaultEventName, payload);
        }

        public void SendPayloadToOthers(string payload)
        {
            Send(MetaDynRpcAudience.Others, ToChannel(defaultChannel), defaultEventName, payload);
        }

        public void SendPayloadToEveryone(string payload)
        {
            Send(MetaDynRpcAudience.Everyone, ToChannel(defaultChannel), defaultEventName, payload);
        }

        public void SendOnChannel(int channel)
        {
            Send(defaultAudience, ToChannel(channel), defaultEventName, string.Empty);
        }

        public void SendOnChannelToSelf(int channel)
        {
            Send(MetaDynRpcAudience.Self, ToChannel(channel), defaultEventName, string.Empty);
        }

        public void SendOnChannelToOthers(int channel)
        {
            Send(MetaDynRpcAudience.Others, ToChannel(channel), defaultEventName, string.Empty);
        }

        public void SendOnChannelToEveryone(int channel)
        {
            Send(MetaDynRpcAudience.Everyone, ToChannel(channel), defaultEventName, string.Empty);
        }

        public void SendEventName(string eventName)
        {
            Send(defaultAudience, ToChannel(defaultChannel), eventName, string.Empty);
        }

        public void SendEventNameToSelf(string eventName)
        {
            Send(MetaDynRpcAudience.Self, ToChannel(defaultChannel), eventName, string.Empty);
        }

        public void SendEventNameToOthers(string eventName)
        {
            Send(MetaDynRpcAudience.Others, ToChannel(defaultChannel), eventName, string.Empty);
        }

        public void SendEventNameToEveryone(string eventName)
        {
            Send(MetaDynRpcAudience.Everyone, ToChannel(defaultChannel), eventName, string.Empty);
        }

        public void Send(byte channel, string eventName = "", string payload = "")
        {
            Send(defaultAudience, channel, eventName, payload);
        }

        public void Send(MetaDynRpcAudience audience, byte channel, string eventName = "", string payload = "")
        {
            eventName ??= string.Empty;
            payload ??= string.Empty;

            if (audience == MetaDynRpcAudience.Self)
            {
                ReceiveLocal(channel, eventName, payload, NetworkManager != null ? NetworkManager.LocalClientId : 0UL);
                return;
            }

            if (!IsSpawned)
            {
                Debug.LogWarning($"[MetaDyn Network Event Relay] Cannot send remote event from '{name}' because its NetworkObject is not spawned.");
                return;
            }

            SendServerRpc((byte)audience, channel, eventName, payload);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SendServerRpc(byte audienceValue, byte channel, string eventName, string payload, ServerRpcParams rpcParams = default)
        {
            var audience = (MetaDynRpcAudience)audienceValue;
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (audience == MetaDynRpcAudience.Everyone)
            {
                ReceiveClientRpc(channel, eventName, payload, senderClientId);
                return;
            }

            if (!TryBuildTargetParams(audience, senderClientId, out var clientRpcParams))
                return;

            ReceiveClientRpc(channel, eventName, payload, senderClientId, clientRpcParams);
        }

        [ClientRpc]
        private void ReceiveClientRpc(byte channel, string eventName, string payload, ulong senderClientId, ClientRpcParams clientRpcParams = default)
        {
            ReceiveLocal(channel, eventName, payload, senderClientId);
        }

        private void ReceiveLocal(byte channel, string eventName, string payload, ulong senderClientId)
        {
            LastChannel = channel;
            LastEventName = eventName ?? string.Empty;
            LastPayload = payload ?? string.Empty;
            LastSenderClientId = senderClientId;

            OnReceived?.Invoke();
            OnChannelReceived?.Invoke(LastChannel);
            OnPayloadReceived?.Invoke(LastPayload);
            OnEventNameReceived?.Invoke(LastEventName);
            OnMessageReceived?.Invoke(LastChannel, LastEventName, LastPayload);
        }

        private static bool TryBuildTargetParams(MetaDynRpcAudience audience, ulong senderClientId, out ClientRpcParams clientRpcParams)
        {
            clientRpcParams = default;

            if (NetworkManager.Singleton == null)
                return false;

            var targets = new List<ulong>();
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (audience == MetaDynRpcAudience.Others && clientId == senderClientId)
                    continue;

                targets.Add(clientId);
            }

            if (targets.Count == 0)
                return false;

            clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = targets
                }
            };

            return true;
        }

        private static byte ToChannel(int channel)
        {
            return (byte)Mathf.Clamp(channel, 0, byte.MaxValue);
        }
    }
}
