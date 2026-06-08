using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MetaDyn
{
    /// <summary>
    /// A modern virtual joystick for mobile movement controls.
    /// Injects values into InputManager.VirtualMoveInput.
    /// </summary>
    public class MetaDynVirtualJoystick : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("References")]
        public RectTransform container;
        public RectTransform handle;

        [Header("Settings")]
        public float movementRange = 100f;
        public bool hideOnRelease = false;

        [Header("Feedback")]
        public UnityEngine.UI.Image backgroundImage;
        public Color activeColor = new Color(1f, 1f, 1f, 1.0f);
        private Color _normalColor;

        private Vector2 _input = Vector2.zero;
        private Vector3 _startPos;

        private void Awake()
{
            if (container == null) container = GetComponent<RectTransform>();
            _startPos = handle.anchoredPosition;
            
            if (backgroundImage == null) backgroundImage = container.GetComponent<UnityEngine.UI.Image>();
            if (backgroundImage != null) _normalColor = backgroundImage.color;

            if (hideOnRelease) container.gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (backgroundImage != null) backgroundImage.color = activeColor;

            if (hideOnRelease)
            {
                container.gameObject.SetActive(true);
                container.position = eventData.position;
            }
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 pos;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(container, eventData.position, eventData.pressEventCamera, out pos))
            {
                // Clamp position
                pos = Vector2.ClampMagnitude(pos, movementRange);
                handle.anchoredPosition = pos;

                // Normalize input
                // Ensure we are sending the correct direction vector
                _input = pos / movementRange;
                InputManager.VirtualMoveInput = _input;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (backgroundImage != null) backgroundImage.color = _normalColor;

            _input = Vector2.zero;
            InputManager.VirtualMoveInput = Vector2.zero;
            handle.anchoredPosition = _startPos;

            if (hideOnRelease) container.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            InputManager.VirtualMoveInput = Vector2.zero;
        }
    }
}
