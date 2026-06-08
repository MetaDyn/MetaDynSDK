using UnityEngine;

/// <summary>
/// Simple component that receives JavaScript SendMessage calls and forwards them to WebRTCManager.
/// This allows JavaScript to send messages to a named GameObject without renaming the player GameObject.
/// </summary>
public class WebRTCJSMessageForwarder : MonoBehaviour
{
    [HideInInspector]
    public WebRTCManager webRTCManager;

    /// <summary>
    /// Called by JavaScript when speaking state changes
    /// </summary>
    public void OnSpeakingStateChanged(string isSpeakingStr)
    {
        if (webRTCManager != null)
        {
            webRTCManager.OnSpeakingStateChanged(isSpeakingStr);
        }
    }

    /// <summary>
    /// Called by JavaScript when remote audio stream is ready
    /// </summary>
    public void OnRemoteAudioStreamReady(string peerId)
    {
        if (webRTCManager != null)
        {
            webRTCManager.OnRemoteAudioStreamReady(peerId);
        }
    }

    /// <summary>
    /// Called by JavaScript when receiving ICE candidate
    /// </summary>
    public void OnReceiveIceCandidateFromJS(string json)
    {
        if (webRTCManager != null)
        {
            webRTCManager.OnReceiveIceCandidateFromJS(json);
        }
    }

    /// <summary>
    /// Called by JavaScript when receiving SDP description
    /// </summary>
    public void OnReceiveDescriptionFromJS(string json)
    {
        if (webRTCManager != null)
        {
            webRTCManager.OnReceiveDescriptionFromJS(json);
        }
    }
}
