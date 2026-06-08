using UnityEngine;
using UnityEngine.EventSystems;

namespace MetaDyn
{
    /// <summary>
    /// A virtual button for mobile controls (Jump, Sprint, etc).
    /// </summary>
    public class MetaDynVirtualButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public enum ButtonType
        {
            Jump,
            Sprint
        }

        public ButtonType type;

        [Header("Feedback")]
        public UnityEngine.UI.Image targetImage;
        public Color pressedColor = new Color(0.7f, 0.7f, 0.7f, 1.0f);
        private Color _normalColor = Color.white;

        private void Start()
        {
            if (targetImage == null) targetImage = GetComponent<UnityEngine.UI.Image>();
            if (targetImage != null) _normalColor = targetImage.color;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            SetState(true);
            if (targetImage != null) targetImage.color = pressedColor;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            SetState(false);
            if (targetImage != null) targetImage.color = _normalColor;
        }

        private void SetState(bool pressed)
        {
            switch (type)
            {
                case ButtonType.Jump:
                    InputManager.VirtualJumpInput = pressed;
                    break;
                case ButtonType.Sprint:
                    InputManager.VirtualSprintInput = pressed;
                    break;
            }
        }

        private void OnDisable()
        {
            SetState(false);
        }
    }
}
