using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MetaDyn.UserList
{
    public class UserListEntry : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Button micIconButton;
        [SerializeField] private Button menuButton;
        [SerializeField] private GameObject contextMenu;

        [Header("Context Menu Buttons")]
        [SerializeField] private Button muteButton;
        [SerializeField] private Button blockButton;
        [SerializeField] private Button kickButton;
        [SerializeField] private Button banButton;

        [Header("Mic Icons")]
        [SerializeField] private Sprite micMutedSprite;
        [SerializeField] private Sprite micUnmutedSprite;

        [Header("Colors")]
[SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color localPlayerColor = new Color(0.2f, 0.8f, 1f);
        [SerializeField] private Color adminColor = new Color(1f, 0.84f, 0f);
        [SerializeField] private Color blockedColor = new Color(0.5f, 0.5f, 0.5f);

        private MetaDynUGSUserData _userData;
        private bool _isLocalPlayer;
        private bool _isBlocked;

        private void Awake()
        {
            if (micIconButton != null)
                micIconButton.onClick.AddListener(OnMicIconClicked);

            if (menuButton != null)
                menuButton.onClick.AddListener(ToggleContextMenu);

            if (muteButton != null)
                muteButton.onClick.AddListener(OnMuteClicked);

            if (blockButton != null)
                blockButton.onClick.AddListener(OnBlockClicked);

            if (kickButton != null)
                kickButton.onClick.AddListener(OnKickClicked);

            if (banButton != null)
                banButton.onClick.AddListener(OnBanClicked);

            if (contextMenu != null)
                contextMenu.SetActive(false);
        }

        public void Initialize(MetaDynUGSUserData userData, bool isLocalPlayer)
        {
            _userData = userData;
            _isLocalPlayer = isLocalPlayer;
            _isBlocked = MetaDynUGSUserListManager.Instance != null
                && MetaDynUGSUserListManager.Instance.IsUserBlockedLocally(userData.ClientId);

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (nameText != null)
            {
                string displayName = _userData.PlayerName.ToString();

                if (_isLocalPlayer)
                    displayName += " (You)";

                if (_userData.IsAdmin)
                    displayName = "* " + displayName;

                nameText.text = displayName;

                if (_isBlocked)
                    nameText.color = blockedColor;
                else if (_userData.IsAdmin)
                    nameText.color = adminColor;
                else if (_isLocalPlayer)
                    nameText.color = localPlayerColor;
                else
                    nameText.color = normalColor;
            }

            if (micIconButton != null)
            {
                var micImage = micIconButton.GetComponent<Image>();
                if (micImage != null)
                {
                    micImage.sprite = _userData.IsMuted ? micMutedSprite : micUnmutedSprite;
                    micImage.color = _isBlocked ? blockedColor : Color.white;
                }

                micIconButton.interactable = _isLocalPlayer || IsLocalPlayerAdmin();
            }

            UpdateButtonVisibility();
        }

        private void UpdateButtonVisibility()
        {
            bool localPlayerIsAdmin = IsLocalPlayerAdmin();

            if (menuButton != null)
                menuButton.gameObject.SetActive(true);

            if (muteButton != null)
            {
                muteButton.gameObject.SetActive(_isLocalPlayer || localPlayerIsAdmin);
                var text = muteButton.GetComponentInChildren<TMP_Text>();
                if (text != null)
                    text.text = _userData.IsMuted ? "Unmute" : "Mute";
            }

            if (blockButton != null)
            {
                blockButton.gameObject.SetActive(!_isLocalPlayer);
                var text = blockButton.GetComponentInChildren<TMP_Text>();
                if (text != null)
                    text.text = _isBlocked ? "Unblock" : "Block";
            }

            if (kickButton != null)
                kickButton.gameObject.SetActive(!_isLocalPlayer && localPlayerIsAdmin);

            if (banButton != null)
                banButton.gameObject.SetActive(!_isLocalPlayer && localPlayerIsAdmin);
        }

        private bool IsLocalPlayerAdmin()
        {
            var manager = MetaDynUGSUserListManager.Instance;
            var networkManager = NetworkManager.Singleton;
            if (manager == null || networkManager == null)
                return false;

            return manager.TryGetUser(networkManager.LocalClientId, out var localUserData) && localUserData.IsAdmin;
        }

        private void OnMicIconClicked()
        {
            OnMuteClicked();
        }

        private void ToggleContextMenu()
        {
            if (contextMenu != null)
                contextMenu.SetActive(!contextMenu.activeSelf);
        }

        private void OnMuteClicked()
        {
            var manager = MetaDynUGSUserListManager.Instance;
            var networkManager = NetworkManager.Singleton;
            if (manager == null || networkManager == null)
                return;

            manager.SetPlayerMuted(_userData.ClientId, !_userData.IsMuted, networkManager.LocalClientId);
            HideContextMenu();
        }

        private void OnBlockClicked()
        {
            var manager = MetaDynUGSUserListManager.Instance;
            if (manager == null)
                return;

            if (_isBlocked)
            {
                manager.UnblockUser(_userData.ClientId);
                _isBlocked = false;
            }
            else
            {
                manager.BlockUser(_userData.ClientId);
                _isBlocked = true;
            }

            UpdateDisplay();
            HideContextMenu();
        }

        private void OnKickClicked()
        {
            Debug.LogWarning("[UserListEntry] Kick is not implemented for the UGS user list yet.");
            HideContextMenu();
        }

        private void OnBanClicked()
        {
            Debug.LogWarning("[UserListEntry] Ban is not implemented for the UGS user list yet.");
            HideContextMenu();
        }

        private void HideContextMenu()
        {
            if (contextMenu != null)
                contextMenu.SetActive(false);
        }

        private void Update()
        {
            if (contextMenu == null || !contextMenu.activeSelf || !Input.GetMouseButtonDown(0))
                return;

            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    contextMenu.GetComponent<RectTransform>(),
                    Input.mousePosition))
            {
                HideContextMenu();
            }
        }
    }
}
