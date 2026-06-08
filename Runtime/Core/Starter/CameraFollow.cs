using UnityEngine;
using UnityEngine.EventSystems;

namespace MetaDyn
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        public bool isTargetMoving = false;
        private Transform _lastTarget;
        public PlayerInput inputSource;
        
        [Header("Position")]
        public Vector3 offset = new Vector3(0, 1.6f, -4.5f);
        public float followSpeed = 10f;
        
        [Header("Rotation")]
        public Vector3 lookAtOffset = new Vector3(0, 1.5f, 0);
        public float rotationSpeed = 5f;

        [Header("First-Person")]
        public Vector3 firstPersonOffset = new Vector3(0f, 1.6f, 0f);
        public float firstPersonBlendTime = 0.12f;

        [Header("Top-Down")]
        public float topDownHeight = 12f;
        public float topDownPitch = 90f;
        public float topDownBlendTime = 0.18f;

        [Header("Collision")]
        public LayerMask collisionLayers = -1;
        public float collisionBuffer = 0.2f;
        
        [Header("Mouse Control")]
        public float mouseSensitivity = 2f;
        public bool invertY = false;
        
        private float currentYaw = 0f;
        private float currentPitch = 0f;
        private float currentDistance;
        private bool IsDragging = false;
        private float _firstPersonBlend;
        private float _firstPersonBlendVelocity;
        private float _topDownBlend;
        private float _topDownBlendVelocity;
        private RaycastHit[] _cameraCollisionHits = new RaycastHit[12];
        
        private void Start()
        {
            currentDistance = offset.magnitude;
            currentPitch = 20f;
        }

        public void InitializeCameraRotation(float targetYaw)
        {
            currentYaw = targetYaw;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            if (inputSource != null)
            {
                currentDistance = inputSource.CurrentInput.CameraZoom;
            }

            if (target != _lastTarget)
            {
                _lastTarget = target;
                if (currentDistance <= 0) currentDistance = offset.magnitude;

                Quaternion snapRotation = Quaternion.Euler(currentPitch, currentYaw, 0);
                Vector3 snapTargetPoint = target.position + lookAtOffset;
                Vector3 snapPosition = snapTargetPoint + snapRotation * new Vector3(0, 0, -currentDistance);
                transform.position = snapPosition;
                transform.LookAt(snapTargetPoint);
            }

            bool isOverUI = MetaDyn.UI.PointerInputUtility.IsPrimaryPointerOverUI();
            bool isPointerHeld = MetaDyn.UI.PointerInputUtility.IsPrimaryPointerHeld();

            if (isPointerHeld && !isOverUI)
            {
                if (!IsDragging)
                {
                    IsDragging = true;
                    #if !UNITY_ANDROID && !UNITY_IOS && !UNITY_WEBGL
                    if (Cursor.lockState != CursorLockMode.Locked)
                    {
                        Cursor.lockState = CursorLockMode.Locked;
                        Cursor.visible = false;
                    }
                    #endif
                }
                
                var lookDelta = MetaDyn.UI.PointerInputUtility.GetPrimaryLookDelta();
                if (Input.touchCount > 0) lookDelta *= 0.15f;

                float mouseX = lookDelta.x * mouseSensitivity;
                float mouseY = lookDelta.y * mouseSensitivity * (invertY ? 1f : -1f);
                
                currentYaw += mouseX;
                currentPitch += mouseY;
                currentPitch = Mathf.Clamp(currentPitch, -30f, 70f);
            }
            else
            {
                if (IsDragging)
                {
                    IsDragging = false;
                    #if !UNITY_ANDROID && !UNITY_IOS && !UNITY_WEBGL
                    if (Cursor.lockState != CursorLockMode.None)
                    {
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                    }
                    #endif
                }
                
                if (isTargetMoving)
                {
                    float targetYaw = target.eulerAngles.y;
                    currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, rotationSpeed * Time.deltaTime);
                }
            }
            
            bool isFirstPerson = inputSource != null && inputSource.IsFirstPersonZoom;
            bool isTopDown = inputSource != null && inputSource.IsTopDownZoom;
            if (isFirstPerson) isTopDown = false;

            float targetBlend = isFirstPerson ? 1f : 0f;
            _firstPersonBlend = Mathf.SmoothDamp(_firstPersonBlend, targetBlend, ref _firstPersonBlendVelocity, firstPersonBlendTime);

            float targetTopDownBlend = isTopDown ? 1f : 0f;
            _topDownBlend = Mathf.SmoothDamp(_topDownBlend, targetTopDownBlend, ref _topDownBlendVelocity, topDownBlendTime);

            Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
            Vector3 targetPoint = target.position + lookAtOffset;
            Vector3 desiredLocalPos = new Vector3(0, 0, -currentDistance);
            Vector3 desiredPosition = targetPoint + rotation * desiredLocalPos;
            
            if (_firstPersonBlend < 1f && _topDownBlend < 1f)
            {
                Vector3 direction = desiredPosition - targetPoint;
                float distance = direction.magnitude;
                if (TryGetCameraCollision(targetPoint, direction.normalized, distance, out RaycastHit hit))
                {
                    desiredPosition = hit.point - direction.normalized * collisionBuffer;
                }
            }

            Vector3 firstPersonPosition = target.position + firstPersonOffset;
            Quaternion firstPersonRotation = rotation;
            Quaternion thirdPersonRotation = Quaternion.LookRotation(targetPoint - desiredPosition, Vector3.up);

            Vector3 blendedPosition = Vector3.Lerp(desiredPosition, firstPersonPosition, _firstPersonBlend);
            Quaternion blendedRotation = Quaternion.Slerp(thirdPersonRotation, firstPersonRotation, _firstPersonBlend);

            Vector3 topDownPosition = target.position + Vector3.up * topDownHeight;
            Quaternion topDownRotation = Quaternion.Euler(topDownPitch, currentYaw, 0);

            blendedPosition = Vector3.Lerp(blendedPosition, topDownPosition, _topDownBlend);
            blendedRotation = Quaternion.Slerp(blendedRotation, topDownRotation, _topDownBlend);

            transform.position = Vector3.Lerp(transform.position, blendedPosition, followSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, blendedRotation, rotationSpeed * Time.deltaTime);
        }

        private bool TryGetCameraCollision(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearestHit)
        {
            nearestHit = default;
            int hitCount = Physics.RaycastNonAlloc(origin, direction, _cameraCollisionHits, distance, collisionLayers, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hitCount; i++)
            {
                var hit = _cameraCollisionHits[i];
                if (hit.collider == null) continue;
                if (target != null && hit.collider.transform.IsChildOf(target)) continue;

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestHit = hit;
                    found = true;
                }
            }
            return found;
        }
    }
}