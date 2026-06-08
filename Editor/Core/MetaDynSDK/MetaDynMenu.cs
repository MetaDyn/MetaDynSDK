using UnityEditor;

namespace MetaDyn
{
    /// <summary>
    /// Handles menu integration for MetaDyn SDK
    /// </summary>
    public static class MetaDynMenu
    {
        [MenuItem(MetaDynSDK.MENU_ROOT + "Dashboard")]
        public static void OpenDashboard()
        {
            MetaDynDashboard.ShowWindow();
        }
        
        [MenuItem(MetaDynSDK.MENU_ROOT + "Project Configuration")]
        public static void OpenProjectConfiguration()
        {
            MetaDynProjectConfig.ShowWindow();
        }

        [MenuItem(MetaDynSDK.MENU_ROOT + "SDK Sync Check")]
        public static void OpenSdkSyncCheck()
        {
            MetaDynSDKSyncCheckWindow.ShowWindow();
        }
    }
}
