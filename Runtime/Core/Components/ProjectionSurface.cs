using UnityEngine;
using System.Runtime.InteropServices;

namespace MetaDyn
{
    /// <summary>
    /// SDK Component: Projects screenshare or web content onto a 3D surface.
    /// Supports two modes: Screenshare (getDisplayMedia) and Web Content (iframe).
    ///
    /// Screenshare: User selects screen/window/tab to share via browser permission.
    /// Web Content: Displays URL in hidden iframe (works for sites that allow embedding).
    ///
    /// Note: Some sites block iframe embedding (X-Frame-Options). Use Screenshare for those.
    /// Perfect for presentations, collaborative workspaces, and virtual events.
    /// </summary>
    public class ProjectionSurface : MonoBehaviour
    {
        public enum ProjectionMode
        {
            Screenshare,    // Capture and display user's screen
            WebContent      // Display iframe/video from URL
        }

        [Header("Projection Settings")]
        [Tooltip("Mode: Screenshare captures user's screen, WebContent displays a URL")]
        public ProjectionMode mode = ProjectionMode.Screenshare;

        [Tooltip("The renderer whose material will receive the projection texture")]
        public Renderer targetRenderer;

        [Tooltip("Material index to apply texture to (default: 0)")]
        public int materialIndex = 0;

        [Header("Web Content Settings")]
        [Tooltip("URL to display when in WebContent mode. Uses iframe (works with most sites). Sites blocking embedding will show error message.")]
        public string contentURL = "https://www.example.com";

        [Tooltip("Width of the web content capture (pixels)")]
        public int contentWidth = 1920;

        [Tooltip("Height of the web content capture (pixels)")]
        public int contentHeight = 1080;

        [Header("Screenshare Settings")]
        [Tooltip("Auto-start screenshare on Awake (WebGL only)")]
        public bool autoStartScreenshare = false;

        [Tooltip("Enable audio capture and playback from screenshare")]
        public bool enableAudio = true;

        [Range(0f, 1f)]
        [Tooltip("Audio volume (0 = mute, 1 = full volume)")]
        public float audioVolume = 1.0f;

        [Header("Display Options")]
        [Tooltip("Flip the projection horizontally (mirror)")]
        public bool flipHorizontal = false;

        [Tooltip("Flip the projection vertically")]
        public bool flipVertical = true;

        [Header("Interaction")]
        [Tooltip("Hotkey to start/stop projection (default: P)")]
        public KeyCode toggleKey = KeyCode.P;

        [Tooltip("Allow click-to-start projection")]
        public bool clickToStart = true;

        // Public state
        public bool IsProjecting { get; private set; }
        public ProjectionMode CurrentMode => mode;

        // Internal state
        private Texture2D _projectionTexture;
        private Material _targetMaterial;
        private string _surfaceId;
        private bool _initialized = false;
        private float _lastAudioVolume;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ProjectionSurface_StartScreenshare(string surfaceId, int width, int height, bool enableAudio, float volume);

        [DllImport("__Internal")]
        private static extern void ProjectionSurface_StopScreenshare(string surfaceId);

        [DllImport("__Internal")]
        private static extern void ProjectionSurface_StartWebContent(string surfaceId, string url, int width, int height);

        [DllImport("__Internal")]
        private static extern void ProjectionSurface_StopWebContent(string surfaceId);

        [DllImport("__Internal")]
        private static extern void ProjectionSurface_UpdateTexture(string surfaceId, int textureId, int width, int height, bool flipH, bool flipV);

        [DllImport("__Internal")]
        private static extern void ProjectionSurface_SetAudioVolume(string surfaceId, float volume);
#endif

        private void Awake()
        {
            _surfaceId = $"ProjectionSurface_{GetInstanceID()}";
            _lastAudioVolume = audioVolume;
            Initialize();

            if (autoStartScreenshare && mode == ProjectionMode.Screenshare)
            {
                StartProjection();
            }
        }

        private void Initialize()
        {
            if (_initialized) return;

            // Validate renderer
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
                if (targetRenderer == null)
                {
                    Debug.LogError($"[ProjectionSurface] No Renderer found on {gameObject.name}. Please assign targetRenderer.");
                    return;
                }
            }

            // Get material
            if (targetRenderer.materials.Length > materialIndex)
            {
                _targetMaterial = targetRenderer.materials[materialIndex];
            }
            else
            {
                Debug.LogError($"[ProjectionSurface] Material index {materialIndex} out of range on {gameObject.name}");
                return;
            }

            // Create texture
            _projectionTexture = new Texture2D(contentWidth, contentHeight, TextureFormat.RGBA32, false);
            _projectionTexture.filterMode = FilterMode.Bilinear;
            _projectionTexture.wrapMode = TextureWrapMode.Clamp;

            _initialized = true;
        }

        private void Update()
        {
            // Hotkey toggle
            if (Input.GetKeyDown(toggleKey))
            {
                ToggleProjection();
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // Update texture from browser - JavaScript directly writes to GPU texture
            if (IsProjecting && _projectionTexture != null)
            {
                ProjectionSurface_UpdateTexture(_surfaceId, _projectionTexture.GetNativeTexturePtr().ToInt32(), contentWidth, contentHeight, flipHorizontal, flipVertical);

                // Update audio volume if changed
                if (mode == ProjectionMode.Screenshare && Mathf.Abs(_lastAudioVolume - audioVolume) > 0.01f)
                {
                    ProjectionSurface_SetAudioVolume(_surfaceId, audioVolume);
                    _lastAudioVolume = audioVolume;
                }
            }
#endif
        }

        private void OnMouseDown()
        {
            if (clickToStart && !IsProjecting)
            {
                StartProjection();
            }
        }

        /// <summary>
        /// Start projection based on current mode
        /// </summary>
        public void StartProjection()
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (IsProjecting)
            {
                Debug.LogWarning($"[ProjectionSurface] Already projecting on {gameObject.name}");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (mode == ProjectionMode.Screenshare)
            {
                Debug.Log($"[ProjectionSurface] Starting screenshare on {gameObject.name}");
                ProjectionSurface_StartScreenshare(_surfaceId, contentWidth, contentHeight, enableAudio, audioVolume);
            }
            else
            {
                Debug.Log($"[ProjectionSurface] Starting web content: {contentURL} on {gameObject.name}");
                ProjectionSurface_StartWebContent(_surfaceId, contentURL, contentWidth, contentHeight);
            }

            // Apply texture to material
            if (_targetMaterial != null && _projectionTexture != null)
            {
                _targetMaterial.mainTexture = _projectionTexture;
            }

            IsProjecting = true;
#else
            Debug.LogWarning("[ProjectionSurface] Projection only works in WebGL builds");
#endif
        }

        /// <summary>
        /// Stop current projection
        /// </summary>
        public void StopProjection()
        {
            if (!IsProjecting) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (mode == ProjectionMode.Screenshare)
            {
                ProjectionSurface_StopScreenshare(_surfaceId);
            }
            else
            {
                ProjectionSurface_StopWebContent(_surfaceId);
            }

            Debug.Log($"[ProjectionSurface] Stopped projection on {gameObject.name}");
#endif

            IsProjecting = false;
        }

        /// <summary>
        /// Toggle projection on/off
        /// </summary>
        public void ToggleProjection()
        {
            if (IsProjecting)
            {
                StopProjection();
            }
            else
            {
                StartProjection();
            }
        }

        /// <summary>
        /// Change the URL for web content mode
        /// </summary>
        public void SetContentURL(string url)
        {
            contentURL = url;

            // Restart if already projecting
            if (IsProjecting && mode == ProjectionMode.WebContent)
            {
                StopProjection();
                StartProjection();
            }
        }

        /// <summary>
        /// Switch projection mode
        /// </summary>
        public void SetMode(ProjectionMode newMode)
        {
            if (mode == newMode) return;

            bool wasProjecting = IsProjecting;
            if (wasProjecting)
            {
                StopProjection();
            }

            mode = newMode;

            if (wasProjecting)
            {
                StartProjection();
            }
        }

        private void OnDestroy()
        {
            StopProjection();

            if (_projectionTexture != null)
            {
                Destroy(_projectionTexture);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            if (targetRenderer != null)
            {
                Gizmos.color = IsProjecting ? Color.green : Color.yellow;
                Gizmos.DrawWireCube(targetRenderer.bounds.center, targetRenderer.bounds.size);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (targetRenderer == null) return;

            // Draw projection info
            Gizmos.color = mode == ProjectionMode.Screenshare ? Color.cyan : Color.magenta;
            Gizmos.DrawWireCube(targetRenderer.bounds.center, targetRenderer.bounds.size * 1.05f);

            // Draw icon
            Vector3 iconPos = targetRenderer.bounds.center + Vector3.up * (targetRenderer.bounds.extents.y + 0.5f);
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(iconPos, 0.2f);
        }
#endif
    }
}
