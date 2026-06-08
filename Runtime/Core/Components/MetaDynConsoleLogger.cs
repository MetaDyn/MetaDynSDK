using UnityEngine;

namespace MetaDyn
{
    /// <summary>
    /// Inspector-friendly logger for wiring UnityEvents to the Unity console.
    /// </summary>
    public sealed class MetaDynConsoleLogger : MonoBehaviour
    {
        [Header("Defaults")]
        [Tooltip("Message logged by LogDefault, LogDefaultWarning, and LogDefaultError.")]
        [SerializeField] private string defaultMessage = "MetaDyn event fired.";

        [Tooltip("Optional prefix added to every message.")]
        [SerializeField] private string prefix = "[MetaDyn]";

        public void LogDefault()
        {
            Log(defaultMessage);
        }

        public void LogDefaultWarning()
        {
            LogWarning(defaultMessage);
        }

        public void LogDefaultError()
        {
            LogError(defaultMessage);
        }

        public void Log(string message)
        {
            Debug.Log(Format(message));
        }

        public void LogWarning(string message)
        {
            Debug.LogWarning(Format(message));
        }

        public void LogError(string message)
        {
            Debug.LogError(Format(message));
        }

        public void LogInt(int value)
        {
            Log(value.ToString());
        }

        public void LogFloat(float value)
        {
            Log(value.ToString("0.###"));
        }

        public void LogBool(bool value)
        {
            Log(value ? "true" : "false");
        }

        public void LogNetworkMessage(int channel, string eventName, string payload)
        {
            Log($"channel={channel}, event={eventName}, payload={payload}");
        }

        private string Format(string message)
        {
            message ??= string.Empty;

            if (string.IsNullOrWhiteSpace(prefix))
                return message;

            return $"{prefix} {message}";
        }
    }
}
