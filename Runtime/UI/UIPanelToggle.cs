using UnityEngine;
using UnityEngine.UI;

namespace MetaDyn
{
    public class UIPanelToggle : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject targetPanel;
        [SerializeField] private Button toggleButton;
        [SerializeField] private Image iconImage;

        [Header("State")]
        [SerializeField] private bool startVisible = true;

        [Header("Icon Opacity")]
        [Range(0f, 1f)]
        [SerializeField] private float visibleAlpha = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float hiddenAlpha = 0.1f;

        private bool isVisible;

        private void Reset()
        {
            toggleButton = GetComponent<Button>();
            iconImage = GetComponent<Image>();
        }

        private void Awake()
        {
            if (toggleButton == null)
                toggleButton = GetComponent<Button>();

            isVisible = startVisible;

            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(TogglePanel);
                toggleButton.onClick.AddListener(TogglePanel);
            }

            ApplyState();
        }

        public void TogglePanel()
        {
            isVisible = !isVisible;
            ApplyState();
        }

        public void ShowPanel()
        {
            isVisible = true;
            ApplyState();
        }

        public void HidePanel()
        {
            isVisible = false;
            ApplyState();
        }

        private void ApplyState()
        {
            if (targetPanel != null)
                targetPanel.SetActive(isVisible);

            if (iconImage != null)
            {
                Color color = iconImage.color;
                color.a = isVisible ? visibleAlpha : hiddenAlpha;
                iconImage.color = color;
            }
        }
    }
}