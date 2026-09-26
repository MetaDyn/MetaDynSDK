using Unity.Netcode.Components;
using UnityEngine;

namespace MetaDyn.Networking
{
    /// <summary>
    /// A client-authoritative version of NetworkAnimator.
    /// By returning false in OnIsServerAuthoritative, the owning client can synchronize animator state to other observers in both client-server and Distributed Authority topologies.
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
