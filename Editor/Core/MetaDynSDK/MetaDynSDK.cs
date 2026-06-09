namespace MetaDyn
{
    /// <summary>
    /// Core MetaDyn SDK class containing version information and SDK constants
    /// </summary>
    public static class MetaDynSDK
    {
        public const string SDK_NAME = "MetaDyn SDK";
        public const string SDK_VERSION = "1.3.2";
        public const string SUPPORTED_NGO_VERSION = "2.1.0";
        public const string SUPPORTED_VIVOX_VERSION = "16.4.2";
        public const string MENU_ROOT = "Tools/MetaDyn/";

        /// <summary>
        /// Detects if this project is the Master SDK development environment.
        /// </summary>
        public static bool IsMasterSDK
        {
            get
            {
                string projectRoot = System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                return System.IO.File.Exists(System.IO.Path.Combine(projectRoot, ".metadyn-master"));
            }
        }
        
        /// <summary>
        /// Gets the full SDK display name with version
        /// </summary>
public static string GetFullSDKName()
        {
            return $"{SDK_NAME} v{SDK_VERSION}";
        }
    }
}
