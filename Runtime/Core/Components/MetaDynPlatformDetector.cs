using UnityEngine;
using UnityEngine.Events;

namespace MetaDyn
{
    /// <summary>
    /// Detects the current execution platform and triggers specific UnityEvents.
    /// Supports WebGL, Mobile (Android/iOS), and XR (VR/AR) detection.
    /// </summary>
    [AddComponentMenu("MetaDyn/Components/Platform Detector")]
    public class MetaDynPlatformDetector : MonoBehaviour
    {
        [Header("Detection Settings")]
        [Tooltip("If true, detection will run automatically in Awake.")]
        public bool triggerOnAwake = true;

        [Header("Platform Events")]
        [Tooltip("Triggered when the platform is detected as WebGL.")]
        public UnityEvent OnWebDetected;

        [Tooltip("Triggered when the platform is detected as a mobile device (Android/iOS).")]
        public UnityEvent OnMobileDetected;

        [Tooltip("Triggered when an XR device is detected and active.")]
        public UnityEvent OnXRDetected;

        [Tooltip("Triggered when the platform is detected as Desktop (and not Web).")]
        public UnityEvent OnDesktopDetected;

        [Tooltip("Triggered when the platform is NOT a mobile device (covers PC Web and Native Desktop).")]
        public UnityEvent OnNonMobileDetected;

        private void Awake()
        {
            if (triggerOnAwake)
            {
                CheckPlatform();
            }
        }

        /// <summary>
        /// Manually trigger the platform check and fire the associated events.
        /// </summary>
        public void CheckPlatform()
        {
            bool isMobile = Application.isMobilePlatform;

            // Mobile vs Non-Mobile (Primary Branch)
            if (isMobile)
            {
                Debug.Log("[PlatformDetector] Mobile platform detected.");
                OnMobileDetected?.Invoke();
            }
            else
            {
                Debug.Log("[PlatformDetector] Non-mobile platform detected.");
                OnNonMobileDetected?.Invoke();
            }

            // Specific Sub-Platform Detection
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                Debug.Log("[PlatformDetector] WebGL context.");
                OnWebDetected?.Invoke();
            }
            
            // XR Detection (Unity 6 / XR Plug-in Management)
            if (UnityEngine.XR.XRSettings.isDeviceActive)
            {
                Debug.Log("[PlatformDetector] XR Device active.");
                OnXRDetected?.Invoke();
            }

            // Native Desktop (Standalone builds only)
            if (!isMobile && Application.platform != RuntimePlatform.WebGLPlayer)
            {
                Debug.Log("[PlatformDetector] Native Desktop detected.");
                OnDesktopDetected?.Invoke();
            }
        }
    }
}
