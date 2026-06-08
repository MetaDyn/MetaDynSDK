using System.Runtime.InteropServices;
using UnityEngine;

namespace MetaDyn.Dashboard
{
    /// <summary>
    /// Bridge between Unity and browser JavaScript for web-first authentication.
    /// Handles token retrieval from cookies/localStorage and redirects to dashboard login.
    /// </summary>
    public class WebAuthBridge : MonoBehaviour
    {
        public static WebAuthBridge Instance { get; private set; }

        [Header("Web Auth Settings")]
        [Tooltip("Require authentication to play. When disabled, users can play without logging in (guest mode).")]
        [SerializeField] private bool requireAuthentication = true;

        [Tooltip("Enable web-first authentication. When enabled, Unity will check for auth token from browser and redirect to dashboard if not authenticated.")]
        [SerializeField] private bool enableWebAuth = true;

        [Tooltip("Dashboard URL for login redirects (without trailing slash)")]
        [SerializeField] private string dashboardUrl = "https://dashboard.metadyn.xyz";

        // Cached token to avoid repeated jslib calls
        private string _cachedToken;
        private bool _tokenChecked;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern string AuthBridge_GetTokenFromCookie();
        [DllImport("__Internal")] private static extern string AuthBridge_GetTokenFromLocalStorage();
        [DllImport("__Internal")] private static extern bool AuthBridge_IsWebGL();
        [DllImport("__Internal")] private static extern void AuthBridge_RedirectToLogin(string dashboardUrl);
        [DllImport("__Internal")] private static extern void AuthBridge_RedirectToDashboard(string dashboardUrl, string path);
        [DllImport("__Internal")] private static extern void AuthBridge_ClearTokenCookie();
        [DllImport("__Internal")] private static extern string AuthBridge_GetCurrentURL();
        [DllImport("__Internal")] private static extern void AuthBridge_OpenURLSameTab(string url);
#endif

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Whether authentication is required to play
        /// </summary>
        public bool RequireAuthentication => requireAuthentication;

        /// <summary>
        /// Whether web authentication is enabled
        /// </summary>
        public bool EnableWebAuth => enableWebAuth;

        /// <summary>
        /// The dashboard URL for redirects
        /// </summary>
        public string DashboardUrl => dashboardUrl;

        /// <summary>
        /// Check if we're running in WebGL browser context
        /// </summary>
        public bool IsWebGL
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Get authentication token from browser (cookie or localStorage)
        /// Returns null if no token found
        /// </summary>
        public string GetToken()
        {
            if (!enableWebAuth)
            {
                Debug.Log("[WebAuthBridge] Web auth disabled, skipping token check");
                return null;
            }

            // Return cached token if already checked
            if (_tokenChecked && !string.IsNullOrEmpty(_cachedToken))
            {
                return _cachedToken;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // Try cookie first (shared across subdomains via domain=.metadyn.xyz)
            _cachedToken = AuthBridge_GetTokenFromCookie();

            // Fallback to localStorage (same-origin only)
            if (string.IsNullOrEmpty(_cachedToken))
            {
                _cachedToken = AuthBridge_GetTokenFromLocalStorage();
            }

            if (!string.IsNullOrEmpty(_cachedToken))
            {
                Debug.Log("[WebAuthBridge] Token retrieved from browser");
            }
            else
            {
                Debug.Log("[WebAuthBridge] No token found in cookie or localStorage");
            }
#else
            Debug.Log("[WebAuthBridge] Not running in WebGL, token check skipped");
#endif

            _tokenChecked = true;
            return _cachedToken;
        }

        /// <summary>
        /// Check if a token is available
        /// </summary>
        public bool HasToken()
        {
            return !string.IsNullOrEmpty(GetToken());
        }

        /// <summary>
        /// Set token (used after validation to update cached value)
        /// </summary>
        public void SetToken(string token)
        {
            _cachedToken = token;
            _tokenChecked = true;
        }

        /// <summary>
        /// Clear cached token (on logout or invalid token)
        /// </summary>
        public void ClearToken()
        {
            _cachedToken = null;
            _tokenChecked = false;

#if UNITY_WEBGL && !UNITY_EDITOR
            AuthBridge_ClearTokenCookie();
            Debug.Log("[WebAuthBridge] Token cleared from browser");
#endif
        }

        /// <summary>
        /// Redirect to dashboard login page with return URL
        /// </summary>
        public void RedirectToLogin()
        {
            if (!enableWebAuth)
            {
                Debug.LogWarning("[WebAuthBridge] Web auth disabled, not redirecting");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log($"[WebAuthBridge] Redirecting to {dashboardUrl}/login...");
            AuthBridge_RedirectToLogin(dashboardUrl);
#else
            Debug.Log($"[WebAuthBridge] Would redirect to {dashboardUrl}/login (not in WebGL)");
#endif
        }

        /// <summary>
        /// Redirect to a specific dashboard path (e.g., "/profile", "/spaces")
        /// </summary>
        public void RedirectToDashboard(string path = "/")
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log($"[WebAuthBridge] Redirecting to {dashboardUrl}{path}");
            AuthBridge_RedirectToDashboard(dashboardUrl, path);
#else
            Debug.Log($"[WebAuthBridge] Would redirect to {dashboardUrl}{path} (not in WebGL)");
#endif
        }

        /// <summary>
        /// Get current page URL (for debugging)
        /// </summary>
        public string GetCurrentURL()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return AuthBridge_GetCurrentURL();
#else
            return "editor://localhost";
#endif
        }

        /// <summary>
        /// Open a URL in the same browser tab (WebGL only)
        /// </summary>
        public void OpenURL(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            AuthBridge_OpenURLSameTab(url);
#else
            Debug.Log($"[WebAuthBridge] Opening URL in browser: {url}");
            Application.OpenURL(url);
#endif
        }
    }
}
