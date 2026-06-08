using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace MetaDyn
{
    public class NameTag : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private RawImage speakingIndicator;

        [Header("Settings")]
        [Tooltip("Adjusts the position relative to the player root. X=0, Y=2.2 is usually good for head height.")]
        public Vector3 PositionOffset = new Vector3(0f, 2.2f, 0f);

        private Transform _camTransform;
        private CanvasGroup _speakingCanvasGroup;

        private void Start()
        {
            if (Camera.main != null)
                _camTransform = Camera.main.transform;

            // Setup speaking indicator
            if (speakingIndicator != null)
            {
                _speakingCanvasGroup = speakingIndicator.GetComponent<CanvasGroup>();
                if (_speakingCanvasGroup == null)
                    _speakingCanvasGroup = speakingIndicator.gameObject.AddComponent<CanvasGroup>();

                SetSpeaking(false); // Start hidden
            }
        }

        private void LateUpdate()
        {
            // 1. Force the position to the offset relative to the parent
            transform.localPosition = PositionOffset;

            // 2. Billboard Logic (Face Camera)
            if (_camTransform == null)
            {
                if (Camera.main != null) _camTransform = Camera.main.transform;
                else return;
            }

            // Match camera rotation exactly so the text faces the screen.
            transform.rotation = _camTransform.rotation;
        }

        // Helper method to update text safely
        public void SetName(string name)
        {
            if (nameText != null)
            {
                nameText.text = name;
            }
        }

        // Show/hide speaking indicator
        public void SetSpeaking(bool isSpeaking)
        {
            if (_speakingCanvasGroup != null)
            {
                _speakingCanvasGroup.alpha = isSpeaking ? 1f : 0f;
            }
        }
    }
}