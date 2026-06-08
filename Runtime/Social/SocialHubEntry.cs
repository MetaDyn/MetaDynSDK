using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MetaDyn.Dashboard;

namespace MetaDyn.Social
{
    /// <summary>
    /// Component for a single entry in the Social Hub list (Friend or Community).
    /// </summary>
    public class SocialHubEntry : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private RawImage avatarImage;
        [SerializeField] private Button chatButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button favoriteButton;
        [SerializeField] private Image favoriteIcon;

        public event Action OnChatClicked;
        public event Action OnJoinClicked;
        public event Action OnFavoriteClicked;

        private string _friendId;

        private void Awake()
        {
            chatButton?.onClick.AddListener(() => OnChatClicked?.Invoke());
            joinButton?.onClick.AddListener(() => OnJoinClicked?.Invoke());
            favoriteButton?.onClick.AddListener(() => 
            {
                if (MetaDynSocialManager.Instance != null && !string.IsNullOrEmpty(_friendId))
                {
                    MetaDynSocialManager.Instance.ToggleFavorite(_friendId);
                    OnFavoriteClicked?.Invoke();
                }
            });
        }

        public void Setup(string name, string status, string avatarUrl, string friendId = "", bool isFavorite = false)
        {
            _friendId = friendId;
            if (nameText != null) nameText.text = name;
            if (statusText != null) statusText.text = status;

            if (favoriteIcon != null)
            {
                favoriteIcon.color = isFavorite ? Color.yellow : Color.gray;
            }

            // Load avatar if url is provided
if (!string.IsNullOrEmpty(avatarUrl) && avatarImage != null && SupabaseAuthManager.Instance != null)
            {
                SupabaseAuthManager.Instance.LoadImageFromUrl(avatarUrl, (tex) =>
                {
                    if (this != null && avatarImage != null)
                    {
                        avatarImage.texture = tex;
                    }
                });
            }
        }
    }
}
