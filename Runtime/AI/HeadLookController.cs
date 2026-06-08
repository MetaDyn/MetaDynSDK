using UnityEngine;

namespace MetaDyn.AI
{
    /// <summary>
    /// Controls the avatar's head and eye movement using Unity's Inverse Kinematics (IK).
    /// Also handles body rotation to face the target (replaces FacePlayer.cs).
    /// Provides smooth transitions between looking at the user, objects, or returning to neutral.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class HeadLookController : MonoBehaviour
    {
        [Header("Body Rotation")]
        [Tooltip("Enable body rotation to face the look target")]
        public bool enableBodyRotation = true;

        [Tooltip("How fast the body rotates to face the target")]
        public float bodyRotationSpeed = 3.0f;

        [Header("Head IK Settings")]
        [Tooltip("How fast the head turns to look at a target")]
        public float lookSpeed = 2.0f;

        [Tooltip("Weight of the head look (0=Disabled, 1=Full Lock)")]
        [Range(0, 1)] public float lookWeight = 1.0f;

        [Header("IK Weights")]
        [Range(0, 1)] public float bodyWeight = 0.2f;
        [Range(0, 1)] public float headWeight = 0.9f;
        [Range(0, 1)] public float eyesWeight = 1.0f;
        [Range(0, 1)] public float clampWeight = 0.5f;

        [Header("State")]
        [Tooltip("Current object being looked at. If null, looks forward.")]
        public Transform currentLookTarget;

        [Tooltip("Offset for the eyes (usually roughly 1.6m up)")]
        public Vector3 eyeOffset = new Vector3(0, 1.6f, 0);

        private Animator _animator;
        private Vector3 _currentLookPos;
        private float _currentWeight = 0f;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _currentLookPos = transform.position + transform.forward + eyeOffset;
        }

        private void Update()
        {
            // Body rotation to face target (replaces FacePlayer.cs)
            if (enableBodyRotation && currentLookTarget != null)
            {
                Vector3 direction = currentLookTarget.position - transform.position;
                direction.y = 0; // Keep upright, only rotate on Y axis

                if (direction.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        bodyRotationSpeed * Time.deltaTime
                    );
                }
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null) return;

            // 1. Determine Target Position
            Vector3 targetPos;
            float targetWeight;

            if (currentLookTarget != null)
            {
                // Look at the target (specifically their eyes if it's a player, or center if object)
                targetPos = currentLookTarget.position;
                
                // If looking at a player, try to look at their head height (approx +1.6m)
                // This is a simple heuristic; can be refined with specific bone targets later
                if (currentLookTarget.CompareTag("Player"))
                {
                    targetPos += Vector3.up * 1.5f; 
                }

                targetWeight = lookWeight;
            }
            else
            {
                // Look forward (Neutral)
                targetPos = transform.position + transform.forward * 5.0f + eyeOffset;
                targetWeight = 0f; // Fade out IK when neutral
            }

            // 2. Smoothly Interpolate Position & Weight
            _currentLookPos = Vector3.Lerp(_currentLookPos, targetPos, Time.deltaTime * lookSpeed);
            _currentWeight = Mathf.Lerp(_currentWeight, targetWeight, Time.deltaTime * lookSpeed);

            // 3. Apply IK
            _animator.SetLookAtWeight(_currentWeight, bodyWeight, headWeight, eyesWeight, clampWeight);
            _animator.SetLookAtPosition(_currentLookPos);
        }

        /// <summary>
        /// Set a new object to look at. Pass null to look neutral/forward.
        /// </summary>
        public void SetLookTarget(Transform target)
        {
            currentLookTarget = target;
        }

        /// <summary>
        /// Momentarily look at a target for a duration, then return to previous target.
        /// </summary>
        public void GlanceAt(Transform target, float duration)
        {
            StartCoroutine(GlanceRoutine(target, duration));
        }

        private System.Collections.IEnumerator GlanceRoutine(Transform target, float duration)
        {
            Transform previousTarget = currentLookTarget;
            currentLookTarget = target;
            yield return new WaitForSeconds(duration);
            currentLookTarget = previousTarget;
        }
    }
}
