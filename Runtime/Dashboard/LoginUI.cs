using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace MetaDyn.Dashboard
{
    /// <summary>
    /// Simple login/signup UI for Supabase authentication.
    /// Attach to a Canvas with input fields and buttons.
    /// Integrates with UIGameMenu to auto-spawn on successful login.
    /// Test in Editor with email/password, WebGL builds will use web auth if enabled in WebAuthBridge.
    /// </summary>
    public class LoginUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject loginPanel;
        [SerializeField] private TMP_InputField emailInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button signupButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text errorText;

        [Header("Profile Display")]
        [SerializeField] private RawImage profileAvatarImage;
        [SerializeField] private Texture2D defaultAvatarTexture;

        [Header("Integration")]
        [SerializeField] private Starter.UIGameMenu uiGameMenu;
        [SerializeField] private GameObject uiGameMenuPanel;
        [SerializeField] private bool autoSpawnOnLogin = true;

        [Header("Settings")]
        [SerializeField] private bool hideOnLogin = true;

        private bool _authFlowCompleted;

        public static bool ShouldHandleAuthenticationInBuild()
        {
            var loginUI = FindFirstObjectByType<LoginUI>(FindObjectsInactive.Include);
            return loginUI != null && loginUI.isActiveAndEnabled;
        }

        public static bool ShouldKeepCursorUnlocked()
        {
            var loginUI = FindFirstObjectByType<LoginUI>(FindObjectsInactive.Include);
            if (loginUI == null || !loginUI.isActiveAndEnabled)
            {
                return false;
            }

            return loginUI.loginPanel != null && loginUI.loginPanel.activeInHierarchy;
        }

        private void Start()
        {
            // Setup button listeners
            if (loginButton != null)
                loginButton.onClick.AddListener(OnLoginClicked);

            if (signupButton != null)
                signupButton.onClick.AddListener(OnSignupClicked);

            // Subscribe to auth events
            if (SupabaseAuthManager.Instance != null)
            {
                SupabaseAuthManager.Instance.OnLoginSuccess += OnLoginSuccess;
                SupabaseAuthManager.Instance.OnLoginFailed += OnLoginFailed;
            }
            else
            {
                Debug.LogError("[LoginUI] SupabaseAuthManager.Instance is null! Make sure it exists in the scene.");
            }

            // Clear status
            SetStatus("");
            SetError("");

            SetGameplayUIVisible(false);

            // Check if already authenticated
            if (SupabaseAuthManager.Instance != null && SupabaseAuthManager.Instance.IsAuthenticated)
            {
                OnLoginSuccess(SupabaseAuthManager.Instance.CurrentSession.user);
                return;
            }

            // Try web-first auth for WebGL builds
            TryWebFirstAuth();
        }

        /// <summary>
        /// Show login panel for manual login (Editor/local testing).
        /// Web auth for WebGL is handled by UIGameMenu directly.
        /// </summary>
        private void TryWebFirstAuth()
        {
            // Check if authentication is required at all
            if (WebAuthBridge.Instance != null && !WebAuthBridge.Instance.RequireAuthentication)
            {
                // Auth disabled - hide login panel, let user play as guest
                if (loginPanel != null)
                    loginPanel.SetActive(false);
                SetGameplayUIVisible(true);
                Debug.Log("[LoginUI] Authentication not required, guest mode");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (ShouldHandleAuthenticationInBuild())
            {
                if (loginPanel != null)
                    loginPanel.SetActive(true);
                SetGameplayUIVisible(false);
                Debug.Log("[LoginUI] Manual LoginUI auth is active for this WebGL build");
                return;
            }
#endif
            // Editor or web auth disabled - show login panel for manual login
            if (loginPanel != null)
                loginPanel.SetActive(true);

            SetGameplayUIVisible(false);
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (SupabaseAuthManager.Instance != null)
            {
                SupabaseAuthManager.Instance.OnLoginSuccess -= OnLoginSuccess;
                SupabaseAuthManager.Instance.OnLoginFailed -= OnLoginFailed;
            }
        }

        private void OnLoginClicked()
        {
            string email = emailInput != null ? emailInput.text : "";
            string password = passwordInput != null ? passwordInput.text : "";

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                SetError("Please enter email and password");
                return;
            }

            SetStatus("Logging in...");
            SetError("");
            SetButtonsInteractable(false);

            SupabaseAuthManager.Instance.Login(email, password, null, OnLoginFailed);
        }

        private void OnSignupClicked()
        {
            string email = emailInput != null ? emailInput.text : "";
            string password = passwordInput != null ? passwordInput.text : "";

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                SetError("Please enter email and password");
                return;
            }

            if (password.Length < 6)
            {
                SetError("Password must be at least 6 characters");
                return;
            }

            SetStatus("Creating account...");
            SetError("");
            SetButtonsInteractable(false);

            SupabaseAuthManager.Instance.Signup(email, password, null, OnLoginFailed);
        }

        private void OnLoginSuccess(SupabaseUser user)
        {
            if (_authFlowCompleted)
            {
                return;
            }

            SetStatus("Fetching profile...");
            SetError("");
            SetButtonsInteractable(false);

            Debug.Log($"[LoginUI] Login successful! User: {user.email}");

            // Fetch profile to get display name
            SupabaseAuthManager.Instance.FetchProfile(user.id, OnProfileFetched, OnProfileFetchFailed);
        }

        private void OnProfileFetched(SupabaseProfile profile)
        {
            _authFlowCompleted = true;

            Debug.Log($"[LoginUI] Profile loaded! Name: {profile.name}, Avatar Index: {profile.avatar_index}, Avatar URL: {profile.avatar_url ?? "none"}");

            // Load profile avatar image
            LoadProfileAvatarImage(profile);

            // Keep manual login in sync with the same shared user UI that web auth updates.
            if (uiGameMenu != null)
            {
                uiGameMenu.ApplyAuthenticatedProfile(profile);
            }

            // Auto-spawn if enabled and UIGameMenu is assigned
            if (autoSpawnOnLogin && uiGameMenu != null)
            {
                // Check if user has selected an avatar
                bool hasAvatar = uiGameMenu.LoadAvatarFromProfile(profile);

                if (hasAvatar)
                {
                    CompleteSuccessfulLogin(profile.name, true);
                }
                else
                {
                    CompleteSuccessfulLogin(profile.name, false);
                    uiGameMenu.ShowAvatarPickerThenSpawn();
                }
            }
            else
            {
                CompleteSuccessfulLogin(profile.name, false);
            }
        }

        private IEnumerator DelayedStartGame()
        {
            // Wait for one frame to ensure UI and state are settled
            yield return null;
            if (uiGameMenu != null)
                uiGameMenu.StartGame();
        }

        private void OnProfileFetchFailed(string error)
        {
            Debug.LogError($"[LoginUI] Profile fetch failed: {error}");

            if (SupabaseAuthManager.Instance != null && SupabaseAuthManager.Instance.IsAuthenticated)
            {
                CompleteLoginWithoutProfile();
                return;
            }

            SetStatus("");
            SetError($"Failed to load profile: {error}");
            SetButtonsInteractable(true);
        }

        private void OnLoginFailed(string error)
        {
            _authFlowCompleted = false;
            SetStatus("");
            SetError($"Error: {error}");
            SetButtonsInteractable(true);
            SetGameplayUIVisible(false);

            Debug.LogError($"[LoginUI] Login failed: {error}");
        }

        private void HidePanel()
        {
            if (loginPanel != null)
                loginPanel.SetActive(false);
        }

        private void SetGameplayUIVisible(bool isVisible)
        {
            if (uiGameMenuPanel != null)
            {
                uiGameMenuPanel.SetActive(isVisible);
            }
        }

        private void CompleteLoginWithoutProfile()
        {
            _authFlowCompleted = true;

            var currentUser = SupabaseAuthManager.Instance.CurrentSession != null
                ? SupabaseAuthManager.Instance.CurrentSession.user
                : null;

            string fallbackName = !string.IsNullOrEmpty(currentUser?.email)
                ? currentUser.email
                : PlayerPrefs.GetString("PlayerName", "Player");

            Debug.LogWarning("[LoginUI] Continuing without profile data");

            if (uiGameMenu != null && uiGameMenu.NicknameText != null)
            {
                uiGameMenu.NicknameText.text = fallbackName;
            }

            CompleteSuccessfulLogin(fallbackName, autoSpawnOnLogin && uiGameMenu != null);
        }

        private void CompleteSuccessfulLogin(string displayName, bool autoStartGame)
        {
            SetError("");
            SetButtonsInteractable(true);

            if (autoStartGame)
            {
                SetStatus("Connecting to world...");
                SetGameplayUIVisible(false);
            }
            else
            {
                SetStatus($"Welcome, {displayName}!");
                SetGameplayUIVisible(true);
            }

            if (hideOnLogin)
            {
                HidePanel();
            }

            if (autoStartGame && uiGameMenu != null)
            {
                uiGameMenu.StartGame();
            }
        }

        /// <summary>
        /// Load the user's profile avatar image from Supabase storage bucket
        /// </summary>
        private void LoadProfileAvatarImage(SupabaseProfile profile)
        {
            if (profileAvatarImage == null)
            {
                return; // No RawImage assigned
            }

            // Show default texture while loading
            if (defaultAvatarTexture != null)
            {
                profileAvatarImage.texture = defaultAvatarTexture;
                profileAvatarImage.enabled = true;
            }

            // Check if profile has an avatar URL
            if (profile == null || string.IsNullOrEmpty(profile.avatar_url))
            {
                Debug.Log("[LoginUI] No avatar_url in profile");
                return;
            }

            // Load the avatar image from URL
            if (SupabaseAuthManager.Instance != null)
            {
                SupabaseAuthManager.Instance.LoadImageFromUrl(
                    profile.avatar_url,
                    onSuccess: (texture) => {
                        if (profileAvatarImage != null)
                        {
                            profileAvatarImage.texture = texture;
                            profileAvatarImage.enabled = true;
                            Debug.Log($"[LoginUI] Profile avatar loaded: {texture.width}x{texture.height}");
                        }
                    },
                    onError: (error) => {
                        Debug.LogWarning($"[LoginUI] Failed to load profile avatar: {error}");
                    }
                );
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void SetError(string message)
        {
            if (errorText != null)
            {
                errorText.text = message;
                errorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (loginButton != null)
                loginButton.interactable = interactable;

            if (signupButton != null)
                signupButton.interactable = interactable;
        }

        // Public methods for testing
        public void Show()
        {
            if (loginPanel != null)
                loginPanel.SetActive(true);
        }

        public void Hide()
        {
            if (loginPanel != null)
                loginPanel.SetActive(false);
        }
    }
}
