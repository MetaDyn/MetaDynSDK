using UnityEngine;
using System.Collections;

namespace MetaDyn.AI
{
    /// <summary>
    /// The "Retina" of the AI. Manages a camera to capture visual snapshots
    /// of the environment for multimodal analysis.
    /// Includes optimizations for WebGL (resolution control, cooldowns).
    /// </summary>
    public class AIEye : MonoBehaviour
    {
        [Header("Vision Settings")]
        [Tooltip("Resolution of the visual snapshot. Lower = faster & cheaper.")]
        [SerializeField] private Vector2Int resolution = new Vector2Int(512, 512);

        [Tooltip("JPG Compression quality (1-100). Lower = smaller payload.")]
        [Range(1, 100)]
        [SerializeField] private int jpegQuality = 60;

        [Tooltip("Minimum time (seconds) between snapshots to prevent lag/spam.")]
        [SerializeField] private float blinkCooldown = 5.0f;

        [Header("Camera Configuration")]
        [Tooltip("The camera used for AI vision. If null, one will be created.")]
        public Camera eyeCamera;

        [Tooltip("Layers the AI can see.")]
        public LayerMask visualLayers = ~0; // See everything by default

        // Runtime state
        private RenderTexture _renderTexture;
        private Texture2D _outputTexture;
        private float _lastCaptureTime = -999f;

        private void Start()
        {
            InitializeEye();
        }

        private void InitializeEye()
        {
            // 1. Setup Camera
            if (eyeCamera == null)
            {
                GameObject camObj = new GameObject("Internal_AI_Eye");
                camObj.transform.SetParent(transform, false);
                camObj.transform.localPosition = Vector3.zero; // Aligned with head/eyes
                camObj.transform.localRotation = Quaternion.identity;
                
                eyeCamera = camObj.AddComponent<Camera>();
                eyeCamera.enabled = false; // We only render manually
                eyeCamera.fieldOfView = 60; // Human-ish FOV
                eyeCamera.nearClipPlane = 0.1f;
                eyeCamera.cullingMask = visualLayers;
            }
            else
            {
                // Ensure existing camera is disabled (we manual render)
                eyeCamera.enabled = false;
            }

            // 2. Setup Memory (RenderTexture)
            if (_renderTexture != null) _renderTexture.Release();
            
            _renderTexture = new RenderTexture(resolution.x, resolution.y, 16);
            _renderTexture.name = "AI_Retina_Texture";
            eyeCamera.targetTexture = _renderTexture;

            // 3. Setup CPU Buffer
            _outputTexture = new Texture2D(resolution.x, resolution.y, TextureFormat.RGB24, false);
        }

        /// <summary>
        /// Attempts to capture a visual snapshot and returns raw JPG bytes.
        /// </summary>
        public byte[] CaptureSnapshotBytes()
        {
            // 1. Cooldown Check
            if (Time.time < _lastCaptureTime + blinkCooldown)
            {
                Debug.Log("[MetaDyn.AI] Eye is blinking (cooldown). Cannot capture.");
                return null;
            }

            if (eyeCamera == null || _renderTexture == null)
            {
                Debug.LogError("[MetaDyn.AI] Eye not initialized properly.");
                return null;
            }

            _lastCaptureTime = Time.time;

            // 2. Render the View
            RenderTexture prevActive = RenderTexture.active;
            eyeCamera.Render();
            
            // 3. Read Pixels to CPU
            RenderTexture.active = _renderTexture;
            _outputTexture.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
            _outputTexture.Apply();
            
            RenderTexture.active = prevActive;

            // 4. Encode to JPG
            return _outputTexture.EncodeToJPG(jpegQuality);
        }

        /// <summary>
        /// Attempts to capture a visual snapshot.
        /// Returns the Base64 JPG string if successful, or null if on cooldown.
        /// </summary>
        public string CaptureSnapshot()
        {
            byte[] jpgBytes = CaptureSnapshotBytes();
            if (jpgBytes == null) return null;

            // 5. Convert to Base64
            string base64Image = System.Convert.ToBase64String(jpgBytes);
            
            Debug.Log($"[MetaDyn.AI] Captured visual snapshot. Payload size: {base64Image.Length / 1024} KB");
            
            return base64Image;
        }

        private void OnDestroy()
        {
            if (_renderTexture != null) _renderTexture.Release();
        }
    }
}
