using UnityEngine;

namespace MetaDyn
{
    /// <summary>
    /// Automatically enables or disables the mobile HUD based on the platform.
    /// Best used when parented to a HUD that is already session-aware (like MainUI).
    /// </summary>
    public class MobileControlsToggle : MonoBehaviour
    {
        public GameObject mobileHUD;
        public bool forceShowInEditor = false;

        private void Awake()
        {
            if (mobileHUD == null) mobileHUD = gameObject;

            bool isMobile = Application.isMobilePlatform;
            
            #if UNITY_EDITOR
            if (forceShowInEditor) isMobile = true;
            #endif

            mobileHUD.SetActive(isMobile);
        }
    }
}
