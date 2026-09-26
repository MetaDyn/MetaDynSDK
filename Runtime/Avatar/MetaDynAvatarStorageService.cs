using System;
using System.Collections;
using System.IO;
using System.Text;
using MetaDyn.Dashboard;
using UnityEngine;
using UnityEngine.Networking;

namespace MetaDyn.Avatar
{
    /// <summary>
    /// Handles authenticated Supabase persistence for runtime user avatar models.
    /// Stores model bytes in Supabase Storage and metadata in public.avatar_models.
    /// </summary>
    public sealed class MetaDynAvatarStorageService : MonoBehaviour
    {
        public static MetaDynAvatarStorageService Instance { get; private set; }

        public const long MaxAvatarBytes = 104857600;
        private const string BucketName = "avatar-models";
        private const string GlbExtension = ".glb";
        private const string GlbFormat = "glb";
        private const string GlbMimeType = "model/gltf-binary";

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                if (transform.parent != null)
                    transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Ensures a scene has a storage service without requiring explicit prefab wiring.
        /// </summary>
        public static MetaDynAvatarStorageService GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<MetaDynAvatarStorageService>();
            if (existing != null)
                return existing;

            var serviceObject = new GameObject("MetaDynAvatarStorageService");
            return serviceObject.AddComponent<MetaDynAvatarStorageService>();
        }

        public bool ValidateAvatarFile(byte[] bytes, string filename, out string error)
        {
            error = null;

            if (!IsAuthenticated(out error))
                return false;

            if (bytes == null || bytes.Length == 0)
            {
                error = "Avatar file is empty.";
                return false;
            }

            if (bytes.LongLength > MaxAvatarBytes)
            {
                error = "Avatar file is larger than the 100 MB limit.";
                return false;
            }

            string extension = Path.GetExtension(filename ?? string.Empty).ToLowerInvariant();
            if (extension != GlbExtension)
            {
                error = "Only .glb avatar uploads are supported right now. VRM support is planned.";
                return false;
            }

            return true;
        }

        public void UploadAndSelectAvatar(
            byte[] bytes,
            string filename,
            Action<MetaDynAvatarModelRecord> onSuccess,
            Action<string> onError)
        {
            if (!ValidateAvatarFile(bytes, filename, out string validationError))
            {
                onError?.Invoke(validationError);
                return;
            }

            StartCoroutine(UploadAndSelectAvatarCoroutine(bytes, filename, onSuccess, onError));
        }

        public void FetchAvatarModel(
            string avatarModelId,
            Action<MetaDynAvatarModelRecord> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(avatarModelId))
            {
                onError?.Invoke("Avatar model id is empty.");
                return;
            }

            if (!IsAuthenticated(out string authError))
            {
                onError?.Invoke(authError);
                return;
            }

            StartCoroutine(FetchAvatarModelCoroutine(avatarModelId, onSuccess, onError));
        }

        public void DownloadAvatarBytes(
            MetaDynAvatarModelRecord model,
            Action<byte[]> onSuccess,
            Action<string> onError)
        {
            if (!ValidateModelForDownload(model, out string error))
            {
                onError?.Invoke(error);
                return;
            }

            StartCoroutine(DownloadAvatarBytesCoroutine(model, onSuccess, onError));
        }

        private IEnumerator UploadAndSelectAvatarCoroutine(
            byte[] bytes,
            string filename,
            Action<MetaDynAvatarModelRecord> onSuccess,
            Action<string> onError)
        {
            var auth = SupabaseAuthManager.Instance;
            var config = auth.Config;
            string userId = auth.CurrentSession.user.id;
            string avatarModelId = Guid.NewGuid().ToString();
            string safeName = string.IsNullOrWhiteSpace(filename) ? "avatar.glb" : Path.GetFileName(filename);
            string objectPath = $"users/{userId}/{avatarModelId}/avatar.glb";

            var record = new MetaDynAvatarModelRecord
            {
                id = avatarModelId,
                user_id = userId,
                object_path = objectPath,
                display_name = safeName,
                format = GlbFormat,
                mime_type = GlbMimeType,
                file_size_bytes = bytes.LongLength,
                status = "active"
            };

            yield return InsertAvatarModelCoroutine(config, auth, record, onError);
            if (_lastRequestFailed)
                yield break;

            yield return UploadStorageObjectCoroutine(config, auth, objectPath, bytes, GlbMimeType, onError);
            if (_lastRequestFailed)
                yield break;

            yield return UpdateProfileActiveAvatarCoroutine(config, auth, userId, avatarModelId, onError);
            if (_lastRequestFailed)
                yield break;

            if (auth.CurrentSession?.user?.profile != null)
                auth.CurrentSession.user.profile.active_avatar_model_id = avatarModelId;

            Debug.Log($"[MetaDynAvatarStorage] Uploaded and selected avatar model {avatarModelId}.");
            onSuccess?.Invoke(record);
        }

        private bool _lastRequestFailed;

        private IEnumerator InsertAvatarModelCoroutine(
            SupabaseConfig config,
            SupabaseAuthManager auth,
            MetaDynAvatarModelRecord record,
            Action<string> onError)
        {
            _lastRequestFailed = false;
            string url = $"{config.SupabaseUrl}/rest/v1/avatar_models";
            string json = "{" +
                          $"\"id\":\"{EscapeJson(record.id)}\"," +
                          $"\"user_id\":\"{EscapeJson(record.user_id)}\"," +
                          $"\"object_path\":\"{EscapeJson(record.object_path)}\"," +
                          $"\"display_name\":\"{EscapeJson(record.display_name)}\"," +
                          $"\"format\":\"{EscapeJson(record.format)}\"," +
                          $"\"mime_type\":\"{EscapeJson(record.mime_type)}\"," +
                          $"\"file_size_bytes\":{record.file_size_bytes}," +
                          $"\"status\":\"{EscapeJson(record.status)}\"" +
                          "}";

            using (var request = CreateJsonRequest(url, "POST", json, config, auth))
            {
                request.SetRequestHeader("Prefer", "return=minimal");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                    FailRequest("Failed to create avatar metadata", request, onError);
            }
        }

        private IEnumerator UploadStorageObjectCoroutine(
            SupabaseConfig config,
            SupabaseAuthManager auth,
            string objectPath,
            byte[] bytes,
            string contentType,
            Action<string> onError)
        {
            _lastRequestFailed = false;
            string url = $"{config.SupabaseUrl}/storage/v1/object/{BucketName}/{UnityWebRequest.EscapeURL(objectPath).Replace("%2F", "/")}";

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("apikey", config.AnonKey);
                request.SetRequestHeader("Authorization", auth.GetAuthHeader());
                request.SetRequestHeader("Content-Type", contentType);
                request.SetRequestHeader("x-upsert", "true");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                    FailRequest("Failed to upload avatar model", request, onError);
            }
        }

        private IEnumerator UpdateProfileActiveAvatarCoroutine(
            SupabaseConfig config,
            SupabaseAuthManager auth,
            string userId,
            string avatarModelId,
            Action<string> onError)
        {
            _lastRequestFailed = false;
            string url = $"{config.SupabaseUrl}/rest/v1/profiles?id=eq.{UnityWebRequest.EscapeURL(userId)}";
            string json = $"{{\"active_avatar_model_id\":\"{EscapeJson(avatarModelId)}\"}}";

            using (var request = CreateJsonRequest(url, "PATCH", json, config, auth))
            {
                request.SetRequestHeader("Prefer", "return=minimal");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                    FailRequest("Failed to select active avatar model", request, onError);
            }
        }

        private IEnumerator FetchAvatarModelCoroutine(
            string avatarModelId,
            Action<MetaDynAvatarModelRecord> onSuccess,
            Action<string> onError)
        {
            var auth = SupabaseAuthManager.Instance;
            var config = auth.Config;
            string url = $"{config.SupabaseUrl}/rest/v1/avatar_models?id=eq.{UnityWebRequest.EscapeURL(avatarModelId)}&select=*";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", config.AnonKey);
                request.SetRequestHeader("Authorization", auth.GetAuthHeader());

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    FailRequest("Failed to fetch avatar metadata", request, onError);
                    yield break;
                }

                string json = UnwrapFirstJsonObject(request.downloadHandler.text);
                if (string.IsNullOrWhiteSpace(json))
                {
                    onError?.Invoke("Avatar metadata was not found.");
                    yield break;
                }

                MetaDynAvatarModelRecord record = JsonUtility.FromJson<MetaDynAvatarModelRecord>(json);
                onSuccess?.Invoke(record);
            }
        }

        private IEnumerator DownloadAvatarBytesCoroutine(
            MetaDynAvatarModelRecord model,
            Action<byte[]> onSuccess,
            Action<string> onError)
        {
            var auth = SupabaseAuthManager.Instance;
            var config = auth.Config;
            string url = $"{config.SupabaseUrl}/storage/v1/object/{BucketName}/{UnityWebRequest.EscapeURL(model.object_path).Replace("%2F", "/")}";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", config.AnonKey);
                request.SetRequestHeader("Authorization", auth.GetAuthHeader());

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    FailRequest("Failed to download avatar model", request, onError);
                    yield break;
                }

                byte[] bytes = request.downloadHandler.data;
                if (bytes == null || bytes.LongLength == 0)
                {
                    onError?.Invoke("Downloaded avatar model is empty.");
                    yield break;
                }

                if (bytes.LongLength > MaxAvatarBytes)
                {
                    onError?.Invoke("Downloaded avatar model exceeds the 100 MB client limit.");
                    yield break;
                }

                onSuccess?.Invoke(bytes);
            }
        }

        private bool ValidateModelForDownload(MetaDynAvatarModelRecord model, out string error)
        {
            error = null;

            if (!IsAuthenticated(out error))
                return false;

            if (model == null)
            {
                error = "Avatar model metadata is missing.";
                return false;
            }

            if (!string.Equals(model.status, "active", StringComparison.OrdinalIgnoreCase))
            {
                error = "Avatar model is not active.";
                return false;
            }

            if (!string.Equals(model.format, GlbFormat, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Avatar model format '{model.format}' is not supported by this build.";
                return false;
            }

            if (model.file_size_bytes > MaxAvatarBytes)
            {
                error = "Avatar model exceeds the 100 MB client limit.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.object_path) || !model.object_path.StartsWith("users/", StringComparison.Ordinal))
            {
                error = "Avatar model storage path is invalid.";
                return false;
            }

            return true;
        }

        private bool IsAuthenticated(out string error)
        {
            error = null;
            var auth = SupabaseAuthManager.Instance;
            if (auth == null || !auth.IsAuthenticated || auth.CurrentSession?.user == null)
            {
                error = "Supabase authentication is required for avatar uploads.";
                return false;
            }

            if (auth.Config == null ||
                string.IsNullOrWhiteSpace(auth.Config.SupabaseUrl) ||
                string.IsNullOrWhiteSpace(auth.Config.AnonKey))
            {
                error = "Supabase configuration is missing.";
                return false;
            }

            return true;
        }

        private static UnityWebRequest CreateJsonRequest(
            string url,
            string method,
            string json,
            SupabaseConfig config,
            SupabaseAuthManager auth)
        {
            var request = new UnityWebRequest(url, method);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", config.AnonKey);
            request.SetRequestHeader("Authorization", auth.GetAuthHeader());
            return request;
        }

        private void FailRequest(string prefix, UnityWebRequest request, Action<string> onError)
        {
            _lastRequestFailed = true;
            string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            string error = $"{prefix}: {request.error} {body}";
            Debug.LogError($"[MetaDynAvatarStorage] {error}");
            onError?.Invoke(error);
        }

        private static string UnwrapFirstJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            string trimmed = json.Trim();
            if (trimmed == "[]")
                return null;

            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                return trimmed.Substring(1, trimmed.Length - 2).Trim();

            return trimmed;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    [Serializable]
    public sealed class MetaDynAvatarModelRecord
    {
        public string id;
        public string user_id;
        public string object_path;
        public string display_name;
        public string format;
        public string mime_type;
        public long file_size_bytes;
        public string status;
        public string created_at;
        public string updated_at;
    }
}
