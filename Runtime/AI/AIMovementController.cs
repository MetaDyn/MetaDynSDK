using UnityEngine;
using UnityEngine.AI;

namespace MetaDyn.AI
{
    /// <summary>
    /// Controls autonomous movement of the AI Agent using Unity NavMesh.
    /// Handles walking to targets, following the player, and managing animations.
    /// Integrates with HeadLookController (disables during movement, re-enables when idle).
    /// </summary>
    public class AIMovementController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [Tooltip("Distance at which to stop before reaching the target")]
        public float stoppingDistance = 2.0f;

        [Header("References")]
        [Tooltip("The NavMeshAgent component (auto-found if null)")]
        public NavMeshAgent navMeshAgent;

        [Tooltip("The Animator component (auto-found if null)")]
        public Animator animator;

        [Tooltip("The HeadLookController to disable during movement (auto-found if null)")]
        public HeadLookController headLookController;

        [Header("State (Read-Only)")]
        [Tooltip("Current movement target position")]
        public Vector3? targetPosition = null;

        [Tooltip("Transform to follow continuously (for 'follow me' behavior)")]
        public Transform followTarget = null;

        [Tooltip("Is the agent currently walking?")]
        public bool isWalking = false;

        // Animator parameter name
        private const string ANIM_IS_WALK = "isWalk";

        private void Awake()
        {
            // Auto-find components if not assigned
            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>();
            }

            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (headLookController == null)
            {
                headLookController = GetComponent<HeadLookController>();
            }

            // Validate
            if (navMeshAgent == null)
            {
                Debug.LogError("[AIMovementController] No NavMeshAgent found! Please add one.", this);
            }

            if (animator == null)
            {
                Debug.LogError("[AIMovementController] No Animator found! Please assign one.", this);
            }

            // Configure NavMeshAgent
            if (navMeshAgent != null)
            {
                navMeshAgent.stoppingDistance = stoppingDistance;
                navMeshAgent.updateRotation = true; // NavMesh handles rotation
                navMeshAgent.updateUpAxis = false; // Keep upright
            }
        }

        private void Update()
        {
            if (navMeshAgent == null)
            {
                Debug.LogError("[AIMovementController] ❌ NavMeshAgent is NULL! Add NavMeshAgent component to this GameObject.");
                return;
            }

            // Determine target based on priority
            Vector3? desiredDestination = null;

            // Priority 1: Follow a moving target (e.g., "follow me")
            if (followTarget != null)
            {
                desiredDestination = followTarget.position;
            }
            // Priority 2: Walk to a specific position
            else if (targetPosition.HasValue)
            {
                desiredDestination = targetPosition.Value;
            }

            // Update NavMesh destination if we have a target
            if (desiredDestination.HasValue)
            {
                bool pathSet = navMeshAgent.SetDestination(desiredDestination.Value);
                if (!pathSet)
                {
                    Debug.LogWarning($"[AIMovementController] ❌ NavMesh failed to set destination to {desiredDestination.Value}");
                }
            }
            else
            {
                // No target - stop navigation
                if (navMeshAgent.hasPath)
                {
                    navMeshAgent.ResetPath();
                }
            }

            // Determine if we're actually moving based on NavMesh state
            bool shouldBeWalking = false;

            // Path is being calculated - keep walking
            if (navMeshAgent.pathPending)
            {
                shouldBeWalking = true;
            }
            // Path exists and is valid
            else if (navMeshAgent.hasPath)
            {
                // remainingDistance can be Infinity if path isn't ready, so check pathStatus too
                if (navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                {
                    if (navMeshAgent.remainingDistance > navMeshAgent.stoppingDistance)
                    {
                        shouldBeWalking = true;
                    }
                    else
                    {
                        // Reached destination
                        if (targetPosition.HasValue)
                        {
                            targetPosition = null; // Clear one-time target
                        }
                    }
                }
                else
                {
                    // Path exists but not complete yet - keep walking
                    shouldBeWalking = true;
                }
            }
            // Update animation state based on actual movement
            if (shouldBeWalking != isWalking)
            {
                SetWalkingState(shouldBeWalking);
            }
        }

        /// <summary>
        /// Move to a specific position (one-time command).
        /// </summary>
        public void WalkToPosition(Vector3 position)
        {
            targetPosition = position;
            followTarget = null; // Cancel any follow behavior
            // Animation is handled automatically by Update()

            Debug.Log($"[AIMovementController] ✅ WalkToPosition called: {position}");
        }

        /// <summary>
        /// Move to a specific Transform target (one-time command).
        /// </summary>
        public void WalkToTarget(Transform target)
        {
            if (target == null)
            {
                Debug.LogWarning("[AIMovementController] ❌ WalkToTarget called with NULL target!");
                return;
            }

            Debug.Log($"[AIMovementController] ✅ WalkToTarget called: {target.name} at {target.position}");
            WalkToPosition(target.position);
        }

        /// <summary>
        /// Continuously follow a target (e.g., "follow me").
        /// </summary>
        public void FollowTarget(Transform target)
        {
            followTarget = target;
            targetPosition = null;
            // Animation is handled automatically by Update()

            Debug.Log($"[AIMovementController] Now following: {target.name}");
        }

        /// <summary>
        /// Stop all movement and return to idle.
        /// </summary>
        public void StopMovement()
        {
            isWalking = false;
            targetPosition = null;
            followTarget = null;

            // Stop NavMesh navigation
            if (navMeshAgent != null && navMeshAgent.hasPath)
            {
                navMeshAgent.ResetPath();
            }

            // Update animator
            if (animator != null)
            {
                animator.SetBool(ANIM_IS_WALK, false);
            }

            // Re-enable head look
            if (headLookController != null)
            {
                headLookController.enabled = true;
            }

            Debug.Log("[AIMovementController] Stopped movement. Returning to idle.");
        }

        /// <summary>
        /// Internal: Set walking animation state without clearing targets.
        /// </summary>
        private void SetWalkingState(bool walking)
        {
            if (isWalking == walking) return; // No change needed

            isWalking = walking;

            // Update animator
            if (animator != null)
            {
                animator.SetBool(ANIM_IS_WALK, walking);
            }

            // Head look management
            if (headLookController != null)
            {
                headLookController.enabled = !walking; // Disable during walk, enable when idle
            }

            Debug.Log($"[AIMovementController] Walking state: {walking}");
        }

        // --- Public Utility Methods ---

        /// <summary>
        /// Check if the agent is currently moving.
        /// </summary>
        public bool IsMoving()
        {
            return isWalking;
        }

        /// <summary>
        /// Get the current distance to the target (if any).
        /// </summary>
        public float GetDistanceToTarget()
        {
            if (navMeshAgent != null && navMeshAgent.hasPath)
            {
                return navMeshAgent.remainingDistance;
            }

            return 0f;
        }

        // --- Debug Visualization ---
        private void OnDrawGizmosSelected()
        {
            if (!isWalking || navMeshAgent == null) return;

            Gizmos.color = Color.green;

            // Draw path
            if (navMeshAgent.hasPath)
            {
                var path = navMeshAgent.path;
                Vector3 previousCorner = transform.position;

                foreach (var corner in path.corners)
                {
                    Gizmos.DrawLine(previousCorner, corner);
                    Gizmos.DrawWireSphere(corner, 0.3f);
                    previousCorner = corner;
                }

                // Draw destination
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(navMeshAgent.destination, stoppingDistance);
            }
        }
    }
}
