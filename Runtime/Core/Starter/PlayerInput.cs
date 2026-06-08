using UnityEngine;
using UnityEngine.EventSystems;
using MetaDyn;

namespace MetaDyn
{
    public struct GameplayInput
    {
        public Vector2 LookRotation;
        public Vector2 MoveDirection;
        public bool Jump;
        public bool Sprint;
        public float CameraZoom;
    }

    public sealed class PlayerInput : MonoBehaviour
    {
        public GameplayInput CurrentInput => _input;
        private GameplayInput _input;

        public bool IsDragging { get; private set; }

        [Header("Mouse Settings")]
        public bool InvertY = false;
        public float Sensitivity = 1.0f;

        [Header("Camera Zoom Settings")]
        public float minZoomDistance = 0.5f;
        public float maxZoomDistance = 10f;
        public float defaultZoomDistance = 3f;
        public float zoomSpeed = 2f;
        public float zoomSmoothTime = 0.1f;
        public float firstPersonThreshold = 0.6f;
        public float topDownThreshold = 9.5f;

        private float targetZoomDistance;
        private float currentZoomVelocity;

        public bool IsFirstPersonZoom => _input.CameraZoom <= firstPersonThreshold;
        public bool IsTopDownZoom => _input.CameraZoom >= topDownThreshold;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            if (firstPersonThreshold < minZoomDistance) firstPersonThreshold = minZoomDistance;
            if (topDownThreshold > maxZoomDistance) topDownThreshold = maxZoomDistance;
            if (topDownThreshold < firstPersonThreshold) topDownThreshold = firstPersonThreshold;

            targetZoomDistance = defaultZoomDistance;
            _input.CameraZoom = defaultZoomDistance;
        }

        public void GetGameplayInput(out GameplayInput input) { input = _input; }
        public GameplayInput GetGameplayInput() { return _input; }

        public void ResetInput()
        {
            _input.MoveDirection = default;
            _input.Jump = false;
            _input.Sprint = false;
        }

        private void Update()
        {
            bool isDraggingInput = Input.GetMouseButton(0) || Input.touchCount > 0;
            bool isOverUI = MetaDyn.UI.PointerInputUtility.IsPrimaryPointerOverUI();

            if (isDraggingInput && !isOverUI)
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

                float yMult = InvertY ? 1f : -1f;
                var lookDelta = MetaDyn.UI.PointerInputUtility.GetPrimaryLookDelta();
                if (Input.touchCount > 0) lookDelta *= 0.1f;

                var lookRotationDelta = new Vector2(lookDelta.y * yMult * Sensitivity, lookDelta.x * Sensitivity);
                _input.LookRotation = ClampLookRotation(_input.LookRotation + lookRotationDelta);
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
            }

            if (!isOverUI)
            {
                float scrollInput = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scrollInput) > 0.01f)
                {
                    targetZoomDistance -= scrollInput * zoomSpeed;
                    targetZoomDistance = Mathf.Clamp(targetZoomDistance, minZoomDistance, maxZoomDistance);
                }
            }

            _input.CameraZoom = Mathf.SmoothDamp(_input.CameraZoom, targetZoomDistance, ref currentZoomVelocity, zoomSmoothTime);

            if (InputManager.IsInputLocked)
            {
                _input.MoveDirection = Vector2.zero;
                _input.Jump = false;
                _input.Sprint = false;
            }
            else
            {
                _input.MoveDirection = InputManager.GetMoveInput();
                _input.Jump |= InputManager.GetJumpInput();
                _input.Sprint |= InputManager.GetSprintInput();
            }

            if (Camera.main != null)
            {
                Vector2 currentLook = _input.LookRotation;
                currentLook.y = Camera.main.transform.eulerAngles.y;
                _input.LookRotation = currentLook;
            }
        }

        private Vector2 ClampLookRotation(Vector2 lookRotation)
        {
            lookRotation.x = Mathf.Clamp(lookRotation.x, -30f, 70f);
            return lookRotation;
        }

        public void SetLookRotationY(float newY)
        {
            _input.LookRotation = new Vector2(_input.LookRotation.x, newY);
        }
    }
}