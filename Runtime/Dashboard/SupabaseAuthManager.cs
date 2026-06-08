using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Text;

namespace MetaDyn.Dashboard
{
    /// <summary>
    /// Manages Supabase authentication for Unity client.
    /// Handles login, signup, and session management.
    /// </summary>
    public class SupabaseAuthManager : MonoBehaviour
    {
        public static SupabaseAuthManager Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private SupabaseConfig config;
        public SupabaseConfig Config => config;

        [Header("Current Session")]
        public SupabaseSession CurrentSession { get; private set; }
        public bool IsAuthenticated => CurrentSession != null && !string.IsNullOrEmpty(CurrentSession.accessToken);

        // Events
        public event Action<SupabaseUser> OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action OnLogout;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                if (transform.parent != null) transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Login with email and password
        /// </summary>
        public void Login(string email, string password, Action<SupabaseUser> onSuccess, Action<string> onError)
        {
            if (config == null)
            {
                onError?.Invoke("Supabase config not assigned!");
                Debug.LogError("[SupabaseAuth] Config is null!");
                return;
            }

            StartCoroutine(LoginCoroutine(email, password, onSuccess, onError));
        }

        /// <summary>
        /// Signup with email and password
        /// </summary>
        public void Signup(string email, string password, Action<SupabaseUser> onSuccess, Action<string> onError)
        {
            if (config == null)
            {
                onError?.Invoke("Supabase config not assigned!");
                Debug.LogError("[SupabaseAuth] Config is null!");
                return;
            }

            StartCoroutine(SignupCoroutine(email, password, onSuccess, onError));
        }

        /// <summary>
        /// Logout current user
        /// </summary>
        public void Logout()
        {
            CurrentSession = null;

            // Clear web token if available
            if (WebAuthBridge.Instance != null)
            {
                WebAuthBridge.Instance.ClearToken();
            }

            OnLogout?.Invoke();
            Debug.Log("[MetaDyn AUTH] User logged out.");
        }

        /// <summary>
        /// Initialize authentication from web token (web-first flow).
        /// For WebGL builds, checks for token from dashboard redirect.
        /// If no token, redirects to dashboard login.
        /// </summary>
        public void InitializeFromWebToken(Action<SupabaseUser> onSuccess, Action<string> onError)
        {
            if (WebAuthBridge.Instance == null)
            {
                onError?.Invoke("WebAuthBridge not found");
                return;
            }

            if (!WebAuthBridge.Instance.EnableWebAuth)
            {
                onError?.Invoke("Web auth disabled");
                return;
            }

            if (!WebAuthBridge.Instance.IsWebGL)
            {
                onError?.Invoke("Not in WebGL");
                return;
            }

            string token = WebAuthBridge.Instance.GetToken();

            if (string.IsNullOrEmpty(token))
            {
                WebAuthBridge.Instance.RedirectToLogin();
                return;
            }

            Debug.Log("[MetaDyn AUTH] Validating web token...");
            ValidateToken(token, onSuccess, onError);
        }

        /// <summary>
        /// Validate a token with Supabase and get user info.
        /// Used for web-first auth flow.
        /// </summary>
        public void ValidateToken(string token, Action<SupabaseUser> onSuccess, Action<string> onError)
        {
            if (config == null)
            {
                onError?.Invoke("Supabase config not assigned!");
                Debug.LogError("[MetaDyn AUTH] Config is null!");
                return;
            }

            StartCoroutine(ValidateTokenCoroutine(token, onSuccess, onError));
        }

        private IEnumerator ValidateTokenCoroutine(string token, Action<SupabaseUser> onSuccess, Action<string> onError)
        {
            // Use Supabase's /auth/v1/user endpoint to validate token and get user info
            string url = $"{config.SupabaseUrl}/auth/v1/user";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", config.AnonKey);
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;

                    try
                    {
                        SupabaseUser user = JsonUtility.FromJson<SupabaseUser>(responseText);

                        // Create session with validated token
                        CurrentSession = new SupabaseSession
                        {
                            accessToken = token,
                            refreshToken = null, // Web flow doesn't provide refresh token directly
                            expiresIn = 3600, // Default 1 hour
                            user = user
                        };

                        Debug.Log($"[MetaDyn AUTH] Token validated for {user.email}");
                        OnLoginSuccess?.Invoke(user);
                        onSuccess?.Invoke(user);
                    }
                    catch (Exception e)
                    {
                        string errorMsg = $"Failed to parse user response: {e.Message}";
                        Debug.LogError($"[MetaDyn AUTH] {errorMsg}");

                        // Token is invalid, clear it and redirect to login
                        if (WebAuthBridge.Instance != null)
                        {
                            WebAuthBridge.Instance.ClearToken();
                            WebAuthBridge.Instance.RedirectToLogin();
                        }

                        OnLoginFailed?.Invoke(errorMsg);
                        onError?.Invoke(errorMsg);
                    }
                }
                else
                {
                    string errorMsg = $"Token validation failed: {request.error}";
                    Debug.LogError($"[MetaDyn AUTH] {errorMsg}");

                    // Token is invalid, clear it and redirect to login
                    if (WebAuthBridge.Instance != null)
                    {
                        WebAuthBridge.Instance.ClearToken();
                        WebAuthBridge.Instance.RedirectToLogin();
                    }

                    OnLoginFailed?.Invoke(errorMsg);
                    onError?.Invoke(errorMsg);
                }
            }
        }

        private IEnumerator LoginCoroutine(string email, string password, Action<SupabaseUser> onSuccess, Action<string> onError)
        {
            string url = $"{config.SupabaseUrl}/auth/v1/token?grant_type=password";

            string jsonData = JsonUtility.ToJson(new SupabaseLoginRequest { email = email, password = password });
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", config.AnonKey);

                Debug.Log($"[MetaDyn AUTH] Attempting login: {email}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;

                    try
                    {
                        SupabaseAuthResponse response = JsonUtility.FromJson<SupabaseAuthResponse>(responseText);
                        CurrentSession = new SupabaseSession
                        {
                            accessToken = response.access_token,
                            refreshToken = response.refresh_token,
                            expiresIn = response.expires_in,
                            user = response.user
                        };

                        Debug.Log($"[MetaDyn AUTH] Login successful: {response.user.id}");
                        OnLoginSuccess?.Invoke(response.user);
                        onSuccess?.Invoke(response.user);
                    }
                    catch (Exception e)
                    {
                        string errorMsg = $"Failed to parse login response: {e.Message}";
                        Debug.LogError($"[MetaDyn AUTH] {errorMsg}");
                        OnLoginFailed?.Invoke(errorMsg);
                        onError?.Invoke(errorMsg);
                    }
                }
                else
                {
                    string errorMsg = $"Login failed: {request.error}";
                    Debug.LogError($"[MetaDyn AUTH] {errorMsg}");
                    OnLoginFailed?.Invoke(errorMsg);
                    onError?.Invoke(errorMsg);
                }
            }
        }

        private IEnumerator SignupCoroutine(string email, string password, Action<SupabaseUser> onSuccess, Action<string> onError)
        {
            string url = $"{config.SupabaseUrl}/auth/v1/signup";

            string jsonData = JsonUtility.ToJson(new SupabaseLoginRequest { email = email, password = password });
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", config.AnonKey);

                Debug.Log($"[SupabaseAuth] Attempting signup for: {email}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"[SupabaseAuth] Signup response: {responseText}");

                    try
                    {
                        SupabaseAuthResponse response = JsonUtility.FromJson<SupabaseAuthResponse>(responseText);
                        CurrentSession = new SupabaseSession
                        {
                            accessToken = response.access_token,
                            refreshToken = response.refresh_token,
                            expiresIn = response.expires_in,
                            user = response.user
                        };

                        Debug.Log($"[SupabaseAuth] Signup successful! User ID: {response.user.id}");
                        OnLoginSuccess?.Invoke(response.user);
                        onSuccess?.Invoke(response.user);
                    }
                    catch (Exception e)
                    {
                        string errorMsg = $"Failed to parse signup response: {e.Message}";
                        Debug.LogError($"[SupabaseAuth] {errorMsg}");
                        OnLoginFailed?.Invoke(errorMsg);
                        onError?.Invoke(errorMsg);
                    }
                }
                else
                {
                    string errorMsg = $"Signup failed: {request.error} - {request.downloadHandler.text}";
                    Debug.LogError($"[SupabaseAuth] {errorMsg}");
                    OnLoginFailed?.Invoke(errorMsg);
                    onError?.Invoke(errorMsg);
                }
            }
        }

        /// <summary>
        /// Get authorization header for API calls
        /// </summary>
        public string GetAuthHeader()
        {
            if (!IsAuthenticated)
                return null;

            return $"Bearer {CurrentSession.accessToken}";
        }

        /// <summary>
        /// Fetch user profile from profiles table
        /// </summary>
        public void FetchProfile(string userId, Action<SupabaseProfile> onSuccess, Action<string> onError)
        {
            StartCoroutine(FetchProfileCoroutine(userId, onSuccess, onError));
        }

        /// <summary>
        /// Update avatar_index in user's profile
        /// </summary>
        public void UpdateAvatarIndex(int avatarIndex, Action onSuccess, Action<string> onError)
        {
            if (!IsAuthenticated || CurrentSession.user == null)
            {
                onError?.Invoke("Not authenticated");
                return;
            }

            StartCoroutine(UpdateAvatarIndexCoroutine(CurrentSession.user.id, avatarIndex, onSuccess, onError));
        }

        /// <summary>
        /// Download an image from a URL and return as Texture2D.
        /// Use this to load profile avatar images from Supabase storage.
        /// </summary>
        public void LoadImageFromUrl(string url, Action<Texture2D> onSuccess, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(url))
            {
                onError?.Invoke("URL is empty");
                return;
            }

            StartCoroutine(LoadImageCoroutine(url, onSuccess, onError));
        }

        private IEnumerator LoadImageCoroutine(string url, Action<Texture2D> onSuccess, Action<string> onError)
        {
            Debug.Log($"[SupabaseAuth] Loading image from: {url}");
            
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                // Include auth headers so private bucket images (like Space thumbnails) load in WebGL
                if (config != null)
                {
                    request.SetRequestHeader("apikey", config.AnonKey);
                    request.SetRequestHeader("Authorization", GetAuthHeader());
                }

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    Debug.Log($"[SupabaseAuth] Image loaded: {texture.width}x{texture.height}");
                    onSuccess?.Invoke(texture);
                }
                else
                {
                    string errorMsg = $"Failed to load image: {request.error}";
                    Debug.LogError($"[SupabaseAuth] {errorMsg}");
                    onError?.Invoke(errorMsg);
                }
            }
        }

        private IEnumerator UpdateAvatarIndexCoroutine(string userId, int avatarIndex, Action onSuccess, Action<string> onError)
        {
            string url = $"{config.SupabaseUrl}/rest/v1/profiles?id=eq.{userId}";

            string jsonData = $"{{\"avatar_index\":{avatarIndex}}}";
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest request = new UnityWebRequest(url, "PATCH"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", config.AnonKey);
                request.SetRequestHeader("Authorization", GetAuthHeader());
                request.SetRequestHeader("Prefer", "return=minimal");

                Debug.Log($"[SupabaseAuth] Updating avatar_index to {avatarIndex} for user {userId}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    // Update local profile cache
                    if (CurrentSession?.user?.profile != null)
                    {
                        CurrentSession.user.profile.avatar_index = avatarIndex;
                    }

                    Debug.Log($"[SupabaseAuth] Avatar index updated to {avatarIndex}");
                    onSuccess?.Invoke();
                }
                else
                {
                    string errorMsg = $"Failed to update avatar: {request.error} - {request.downloadHandler.text}";
                    Debug.LogError($"[SupabaseAuth] {errorMsg}");
                    onError?.Invoke(errorMsg);
                }
            }
        }

        private IEnumerator FetchProfileCoroutine(string userId, Action<SupabaseProfile> onSuccess, Action<string> onError)
        {
            string url = $"{config.SupabaseUrl}/rest/v1/profiles?id=eq.{userId}&select=*";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", config.AnonKey);
                request.SetRequestHeader("Authorization", GetAuthHeader());

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"[SupabaseAuth] Profile response: {responseText}");

                    try
                    {
                        // Response is an array, get first element
                        if (responseText.StartsWith("["))
                        {
                            responseText = responseText.Substring(1, responseText.Length - 2); // Remove [ ]
                        }

                        SupabaseProfile profile = JsonUtility.FromJson<SupabaseProfile>(responseText);

                        // Store in current session
                        if (CurrentSession != null && CurrentSession.user != null)
                        {
                            CurrentSession.user.profile = profile;
                        }

                        Debug.Log($"[SupabaseAuth] Profile fetched! Name: {profile.name}");
                        onSuccess?.Invoke(profile);
                    }
                    catch (Exception e)
                    {
                        string errorMsg = $"Failed to parse profile: {e.Message}";
                        Debug.LogError($"[SupabaseAuth] {errorMsg}");
                        onError?.Invoke(errorMsg);
                    }
                }
                else
                {
                    string errorMsg = $"Profile fetch failed: {request.error}";
                    Debug.LogError($"[SupabaseAuth] {errorMsg}");
                    onError?.Invoke(errorMsg);
                }
            }
        }
    }

    // Data structures for Supabase auth
    [Serializable]
    public class SupabaseLoginRequest
    {
        public string email;
        public string password;
    }

    [Serializable]
    public class SupabaseAuthResponse
    {
        public string access_token;
        public string token_type;
        public int expires_in;
        public string refresh_token;
        public SupabaseUser user;
    }

    [Serializable]
    public class SupabaseUser
    {
        public string id;
        public string aud;
        public string role;
        public string email;
        public string email_confirmed_at;
        public string phone;
        public string confirmed_at;
        public string last_sign_in_at;
        public string created_at;
        public string updated_at;

        // Profile data (fetched separately)
        public SupabaseProfile profile;
    }

    [Serializable]
    public class SupabaseProfile
    {
        public string id;          // User's unique UID (matches auth.users.id)
        public string name;        // Display name
        public string avatar_url;  // Profile picture URL
        public int avatar_index = -1;  // Preset avatar choice (-1 = not chosen, show picker)
        // Note: email is NOT in profiles table - get it from SupabaseUser.email instead

        /// <summary>
        /// Returns true if user has not yet chosen an avatar
        /// </summary>
        public bool NeedsAvatarSelection => avatar_index < 0;
    }

    [Serializable]
    public class SupabaseSession
    {
        public string accessToken;
        public string refreshToken;
        public int expiresIn;
        public SupabaseUser user;
    }
}
