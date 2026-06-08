using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace MetaDyn.Chat
{
    /// <summary>
    /// Represents a single chat message entry in the UI.
    /// Displays: Timestamp, Sender Name, Message Content
    /// </summary>
    public class ChatMessageEntry : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text timestampText;
        [SerializeField] private TMP_Text senderText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Image backgroundImage;

        [Header("Layout Settings")]
        [SerializeField] private bool useAutoHeight = true;
        [SerializeField] private float minHeight = 40f;
        [SerializeField] private float maxHeight = 200f;

        private RectTransform _rectTransform;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            
            // Ensure we have a LayoutElement for the VerticalLayoutGroup to read
            var layoutElement = GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = gameObject.AddComponent<LayoutElement>();
            }
            layoutElement.minHeight = minHeight;
            layoutElement.preferredHeight = -1; // Let text determine height
        }

        /// <summary>
        /// Initialize this message entry with content
        /// </summary>
        public void Initialize(string sender, string message, string timestamp, Color senderColor)
        {
            bool isSystem = string.IsNullOrEmpty(sender);

            // Hide separate components as we are now using a single consolidated text block
            // for true inline wrapping across lines.
            if (timestampText != null) timestampText.gameObject.SetActive(false);
            if (senderText != null) senderText.gameObject.SetActive(false);

            if (messageText != null)
            {
                if (isSystem)
                {
                    // Clean system message (no timestamp as requested)
                    messageText.text = message;
                    messageText.color = senderColor;
                }
                else
                {
                    // Consolidated player message: [Timestamp] Name: Message
                    string colorHex = ColorUtility.ToHtmlStringRGB(senderColor);
                    string timestampPart = $"<color=#888888>[{timestamp}]</color> ";
                    string senderPart = $"<color=#{colorHex}><b>{sender}:</b></color> ";
                    
                    messageText.text = $"{timestampPart}{senderPart}{message}";
                    messageText.color = Color.white;
                }
            }
        }

        /// <summary>
        /// Set background color (useful for alternating rows or highlighting)
/// </summary>
        public void SetBackgroundColor(Color color)
        {
            if (backgroundImage != null)
            {
                backgroundImage.color = color;
            }
        }

        /// <summary>
        /// Set background alpha (for transparency effects)
        /// </summary>
        public void SetBackgroundAlpha(float alpha)
        {
            if (backgroundImage != null)
            {
                Color color = backgroundImage.color;
                color.a = alpha;
                backgroundImage.color = color;
            }
        }
    }
}
