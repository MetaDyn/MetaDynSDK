using Unity.Netcode.Components;
using UnityEngine;

namespace MetaDyn.Networking
{
    /// <summary>
    /// A client-authoritative version of NetworkAnimator.
    /// By returning false in OnIsServerAuthoritative, the owner client can synchronize animator state to the server and other clients.
    /// </summary>
    [DisallowMultipleComponent]
    public class OwnerNetworkAnimator : NetworkAnimator
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}