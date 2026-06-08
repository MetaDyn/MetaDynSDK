using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MetaDyn
{
    public class UmamiAnalytics : MonoBehaviour
    {
        [Header("MetaDyn Umami Configuration")]
        [SerializeField] private string umamiEndpoint = "https://analytics.metadyn.xyz/api/send";
        [SerializeField] private string websiteId = "";
        [SerializeField] private string deploymentName = "";
        [SerializeField] private bool logFailures = true;

        private string sessionId;
        private float sessionStartTime;

        public static UmamiAnalytics Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeSession();
        }

        private void InitializeSession()
        {
            sessionId = Guid.NewGuid().ToString();
            sessionStartTime = Time.time;

            TrackEvent("unity_session_start", new Dictionary<string, object>
            {
                { "deployment_name", deploymentName },
                { "session_id", sessionId },
                { "platform_name", Application.platform.ToString() },
                { "unity_version", Application.unityVersion },
                { "device_model", SystemInfo.deviceModel },
                { "graphics_device", SystemInfo.graphicsDeviceName },
                { "graphics_memory_mb", SystemInfo.graphicsMemorySize },
                { "system_memory_mb", SystemInfo.systemMemorySize },
                { "processor_type", SystemInfo.processorType },
                { "processor_count", SystemInfo.processorCount },
                { "screen_resolution", Screen.width + "x" + Screen.height },
                { "quality_level", QualitySettings.names[QualitySettings.GetQualityLevel()] }
            });
        }

        private void OnApplicationQuit()
        {
            TrackEvent("unity_session_end", new Dictionary<string, object>
            {
                { "deployment_name", deploymentName },
                { "session_id", sessionId },
                { "session_duration_s", Time.time - sessionStartTime }
            });
        }

        public void TrackEvent(string eventName)
        {
            TrackEvent(eventName, null);
        }

        public void TrackEvent(string eventName, Dictionary<string, object> eventData)
        {
            StartCoroutine(SendEventCoroutine(eventName, eventData));
        }

        public void TrackButtonClick(string buttonName)
        {
            TrackEvent("unity_button_click", new Dictionary<string, object>
            {
                { "button_name", buttonName }
            });
        }

        public void TrackSceneChange(string sceneName)
        {
            TrackEvent("unity_scene_change", new Dictionary<string, object>
            {
                { "scene_name", sceneName }
            });
        }

        public void TrackInteraction(string objectName, string interactionType)
        {
            TrackEvent("unity_object_interaction", new Dictionary<string, object>
            {
                { "object_name", objectName },
                { "interaction_type", interactionType }
            });
        }

        public void TrackPerformanceSample()
        {
            TrackEvent("unity_performance_sample", new Dictionary<string, object>
            {
                { "fps", Mathf.RoundToInt(1.0f / Mathf.Max(Time.deltaTime, 0.0001f)) },
                { "frame_time_ms", Time.deltaTime * 1000f }
            });
        }

        public void TrackFunnelStep(string funnelName, string stepName, int stepNumber)
        {
            TrackEvent("unity_funnel_step", new Dictionary<string, object>
            {
                { "funnel_name", funnelName },
                { "step_name", stepName },
                { "step_number", stepNumber }
            });
        }

        public void TrackError(string message, string stackTrace = "")
        {
            TrackEvent("unity_error", new Dictionary<string, object>
            {
                { "message", message },
                { "stack_trace", Truncate(stackTrace, 500) }
            });
        }

        private IEnumerator SendEventCoroutine(string eventName, Dictionary<string, object> eventData)
        {
            var payload = new Dictionary<string, object>
            {
                {
                    "type", "event"
                },
                {
                    "payload", new Dictionary<string, object>
                    {
                        { "website", websiteId },
                        { "url", string.IsNullOrEmpty(Application.absoluteURL) ? "unity://local" : Application.absoluteURL },
                        { "name", eventName },
                        { "data", BuildEventData(eventData) }
                    }
                }
            };

            string jsonPayload = SimpleJsonSerializer.Serialize(payload);

            using (UnityWebRequest request = BuildPostRequest(jsonPayload))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success && logFailures)
                {
                    Debug.LogWarning("Umami Analytics Error: " + request.error);
                }
            }
        }

        private UnityWebRequest BuildPostRequest(string jsonPayload)
        {
            UnityWebRequest request = new UnityWebRequest(umamiEndpoint, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("User-Agent", "Unity/" + Application.unityVersion);
            return request;
        }

        private Dictionary<string, object> BuildEventData(Dictionary<string, object> eventData)
        {
            var data = new Dictionary<string, object>
            {
                { "deployment_name", deploymentName },
                { "session_id", sessionId },
                { "platform_name", Application.platform.ToString() },
                { "session_seconds", Time.time - sessionStartTime }
            };

            if (eventData != null)
            {
                foreach (var item in eventData)
                {
                    data[item.Key] = item.Value;
                }
            }

            return data;
        }

        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

    }

    public static class SimpleJsonSerializer
    {
        public static string Serialize(Dictionary<string, object> dict)
        {
            var items = new List<string>();
            foreach (var kvp in dict)
            {
                items.Add("\"" + Escape(kvp.Key) + "\":" + SerializeValue(kvp.Value));
            }
            return "{" + string.Join(",", items.ToArray()) + "}";
        }

        private static string SerializeValue(object value)
        {
            if (value == null) return "null";
            if (value is string) return "\"" + Escape((string)value) + "\"";
            if (value is bool) return (bool)value ? "true" : "false";
            if (value is int || value is long || value is float || value is double || value is decimal)
            {
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (value is Dictionary<string, object>) return Serialize((Dictionary<string, object>)value);

            return "\"" + Escape(value.ToString()) + "\"";
        }

        private static string Escape(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
