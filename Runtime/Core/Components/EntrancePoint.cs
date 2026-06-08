using UnityEngine;

namespace MetaDyn
{
    /// <summary>
    /// Component to mark a valid spawn position and rotation for players.
    /// Add this to empty GameObjects in your scene.
    /// </summary>
    public class EntrancePoint : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            // Draws a Cyan sphere to show position
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            
            // Draws a Yellow line to show facing direction
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
        }
    }
}