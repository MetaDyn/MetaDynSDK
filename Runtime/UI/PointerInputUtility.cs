using UnityEngine;
using UnityEngine.EventSystems;

namespace MetaDyn.UI
{
    /// <summary>
    /// Normalizes primary pointer checks across mouse and touch input.
    /// </summary>
    public static class PointerInputUtility
    {
        public static bool IsPrimaryPointerHeld()
        {
            if (TryGetActiveTouch(out _))
            {
                return true;
            }

            return Input.GetMouseButton(0);
        }

        public static bool IsPrimaryPointerPressedThisFrame()
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                return touch.phase == TouchPhase.Began;
            }

            return Input.GetMouseButtonDown(0);
        }

        public static bool TryGetPrimaryPointerPosition(out Vector2 screenPosition)
        {
            if (Input.touchCount > 0)
            {
                screenPosition = Input.GetTouch(0).position;
                return true;
            }

            screenPosition = Input.mousePosition;
            return true;
        }

        public static bool IsPrimaryPointerOverUI()
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            if (Input.touchCount > 0)
            {
                return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            }

            return EventSystem.current.IsPointerOverGameObject();
        }

        public static Vector2 GetPrimaryLookDelta()
        {
            if (TryGetActiveTouch(out Touch touch))
            {
                // touch.deltaPosition is in screen pixels. 
                // Return a scaled version so it doesn't feel like the camera is flying around.
                return touch.deltaPosition * 0.15f; 
            }

            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        }

        private static bool TryGetActiveTouch(out Touch activeTouch)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Canceled || touch.phase == TouchPhase.Ended)
                {
                    continue;
                }

                activeTouch = touch;
                return true;
            }

            activeTouch = default;
            return false;
        }
    }
}
