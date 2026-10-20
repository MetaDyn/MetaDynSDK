using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using MetaDyn.Dashboard;
using UnityEngine;
using UnityEngine.Networking;

namespace MetaDyn
{
    /// <summary>Authenticated persistence for one current image per space and frame ID.</summary>
    public sealed class MetaDynPictureFrameStorageService : MonoBehaviour
    {
        [Serializable]
        public sealed class FrameRecord
        {
            public string space_id;
            public string frame_id;
            public string object_path;
            public string uploaded_by;
            public string mime_type;
            public long file_size_bytes;
            public int width;
            public int height;
            public long revision;
        }

        [Serializable]
        private sealed class FrameRecordList { public FrameRecord[] items; }

        public const int MaxImageBytes = 8 * 1024 * 1024;
        public const int MaxImageDimension = 2048;
        private const string BucketName = "space-images";
        public static MetaDynPictureFrameStorageService Instance { get; private set; }
        private MetaDynEmptyFrame pendingPickerFrame;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MetaDynOpenImagePicker(string gameObjectName, string successMethod, string failureMethod, int maxBytes);
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Creates the service without requiring a scene or prefab edit.</summary>
        public static MetaDynPictureFrameStorageService GetOrCreate()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<MetaDynPictureFrameStorageService>();
            if (existing != null) return existing;
            return new GameObject("MetaDynPictureFrameStorageService").AddComponent<MetaDynPictureFrameStorageService>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Uses this singleton as the browser callback target, avoiding duplicate scene object names.</summary>
        public bool OpenWebGLPicker(MetaDynEmptyFrame frame)
        {
            if (pendingPickerFrame != null) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            pendingPickerFrame = frame;
            try
            {
                MetaDynOpenImagePicker(gameObject.name, nameof(OnImageSelected), nameof(OnImagePickerFailed), MaxImageBytes);
                return true;
            }
            catch (Exception)
            {
                pendingPickerFrame = null;
                frame.OnImagePickerFailed("The browser image chooser could not open.");
                return true;
            }
#else
            return false;
#endif
        }

        /// <summary>Browser callback. The temporary MEMFS file is deleted by the frame.</summary>
        public void OnImageSelected(string path)
        {
            var frame = pendingPickerFrame;
            pendingPickerFrame = null;
            if (frame != null) frame.OnImageSelected(path);
            else if (!string.IsNullOrEmpty(path))
                try { System.IO.File.Delete(path); } catch (Exception) { /* Temporary browser file only. */ }
        }

        /// <summary>Browser callback for selection or read failure.</summary>
        public void OnImagePickerFailed(string error)
        {
            var frame = pendingPickerFrame;
            pendingPickerFrame = null;
            if (frame != null) frame.OnImagePickerFailed(error);
        }

        /// <summary>Checks a signed-in user and the configured Supabase connection.</summary>
        public static bool TryGetContext(out SupabaseAuthManager auth, out MetaDynRuntimeConfig runtime, out string error)
        {
            auth = SupabaseAuthManager.Instance;
            runtime = MetaDynRuntimeConfig.Instance;
            error = null;
            if (auth == null || !auth.IsAuthenticated || auth.CurrentSession?.user == null)
                error = "Sign in to use picture frames.";
            else if (auth.Config == null || string.IsNullOrWhiteSpace(auth.Config.SupabaseUrl) || string.IsNullOrWhiteSpace(auth.Config.AnonKey))
                error = "Supabase configuration is missing.";
            else if (runtime == null || !Guid.TryParse(runtime.spaceId, out _))
                error = "A valid space ID is required for picture frames.";
            return error == null;
        }

        /// <summary>Client-side convenience check; Supabase policies remain authoritative.</summary>
        public static bool CanCurrentUserEdit(out string error)
        {
            if (!TryGetContext(out var auth, out var runtime, out error)) return false;
            if (!Guid.TryParse(auth.CurrentSession.user.id, out _))
            {
                error = "Authenticated user ID is invalid.";
                return false;
            }
            if (!string.Equals(auth.CurrentSession.user.id, runtime.ownerId, StringComparison.OrdinalIgnoreCase))
            {
                error = "Only the space owner can change pictures in this MVP.";
                return false;
            }
            return true;
        }

        /// <summary>Fetches the durable current selection, including an empty selection.</summary>
        public void Fetch(string frameId, Action<FrameRecord> onSuccess, Action<string> onError)
        {
            if (!TryGetContext(out var auth, out var runtime, out var error)) { onError?.Invoke(error); return; }
            if (!Guid.TryParse(frameId, out _)) { onError?.Invoke("Frame ID must be a GUID."); return; }
            StartCoroutine(FetchRoutine(auth, runtime.spaceId, frameId, onSuccess, onError));
        }

        private IEnumerator FetchRoutine(SupabaseAuthManager auth, string spaceId, string frameId, Action<FrameRecord> onSuccess, Action<string> onError)
        {
            string url = $"{auth.Config.SupabaseUrl}/rest/v1/space_picture_frames?space_id=eq.{Escape(spaceId)}&frame_id=eq.{Escape(frameId)}&select=space_id,frame_id,object_path,uploaded_by,mime_type,file_size_bytes,width,height,revision";
            using (var request = UnityWebRequest.Get(url))
            {
                Authorize(request, auth);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { onError?.Invoke(RequestError("Could not load picture", request)); yield break; }
                try
                {
                    var list = JsonUtility.FromJson<FrameRecordList>("{\"items\":" + request.downloadHandler.text + "}");
                    if (list?.items == null) onError?.Invoke("Picture metadata response is invalid.");
                    else onSuccess?.Invoke(list.items.Length > 0 ? list.items[0] : null);
                }
                catch (Exception) { onError?.Invoke("Picture metadata response is invalid."); }
            }
        }

        /// <summary>Uploads a new object, then publishes its path. Previous image stays selected on failure.</summary>
        public void Upload(string frameId, byte[] bytes, string extension, int width, int height, Action<FrameRecord> onSuccess, Action<string> onError)
        {
            if (!CanCurrentUserEdit(out var error)) { onError?.Invoke(error); return; }
            if (!Guid.TryParse(frameId, out _) || bytes == null || bytes.Length == 0 || bytes.Length > MaxImageBytes ||
                width < 1 || height < 1 || width > MaxImageDimension || height > MaxImageDimension ||
                (extension != ".jpg" && extension != ".png"))
            { onError?.Invoke("Image or frame ID is invalid."); return; }
            StartCoroutine(UploadRoutine(frameId, bytes, extension, width, height, onSuccess, onError));
        }

        private IEnumerator UploadRoutine(string frameId, byte[] bytes, string extension, int width, int height, Action<FrameRecord> onSuccess, Action<string> onError)
        {
            var auth = SupabaseAuthManager.Instance;
            string spaceId = Guid.Parse(MetaDynRuntimeConfig.Instance.spaceId).ToString("D");
            string userId = Guid.Parse(auth.CurrentSession.user.id).ToString("D");
            frameId = Guid.Parse(frameId).ToString("D");
            string mime = extension == ".png" ? "image/png" : "image/jpeg";
            string objectPath = $"users/{userId}/spaces/{spaceId}/frames/{frameId}/{Guid.NewGuid():N}{extension}";
            string uploadUrl = $"{auth.Config.SupabaseUrl}/storage/v1/object/{BucketName}/{EscapePath(objectPath)}";
            using (var request = new UnityWebRequest(uploadUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                Authorize(request, auth);
                request.SetRequestHeader("Content-Type", mime);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { onError?.Invoke(RequestError("Could not upload image", request)); yield break; }
            }

            string json = "{" +
                $"\"space_id\":\"{spaceId}\",\"frame_id\":\"{frameId}\",\"object_path\":\"{objectPath}\"," +
                $"\"uploaded_by\":\"{userId}\",\"mime_type\":\"{mime}\",\"file_size_bytes\":{bytes.Length}," +
                $"\"width\":{width},\"height\":{height}" + "}";
            string metadataUrl = $"{auth.Config.SupabaseUrl}/rest/v1/space_picture_frames?on_conflict=space_id,frame_id";
            using (var request = JsonRequest(metadataUrl, "POST", json, auth))
            {
                request.SetRequestHeader("Prefer", "resolution=merge-duplicates,return=representation");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { onError?.Invoke(RequestError("Image uploaded but could not be published", request)); yield break; }
                if (!TryReadRecord(request.downloadHandler.text, out var record)) { onError?.Invoke("Image metadata response is invalid."); yield break; }
                onSuccess?.Invoke(record);
            }
        }

        /// <summary>Clears the current selection while leaving prior objects available for later cleanup.</summary>
        public void Clear(string frameId, Action<FrameRecord> onSuccess, Action<string> onError)
        {
            if (!CanCurrentUserEdit(out var error)) { onError?.Invoke(error); return; }
            if (!Guid.TryParse(frameId, out _)) { onError?.Invoke("Frame ID must be a GUID."); return; }
            StartCoroutine(ClearRoutine(frameId, onSuccess, onError));
        }

        private IEnumerator ClearRoutine(string frameId, Action<FrameRecord> onSuccess, Action<string> onError)
        {
            var auth = SupabaseAuthManager.Instance;
            string spaceId = Guid.Parse(MetaDynRuntimeConfig.Instance.spaceId).ToString("D");
            string userId = Guid.Parse(auth.CurrentSession.user.id).ToString("D");
            frameId = Guid.Parse(frameId).ToString("D");
            string url = $"{auth.Config.SupabaseUrl}/rest/v1/space_picture_frames?space_id=eq.{Escape(spaceId)}&frame_id=eq.{Escape(frameId)}";
            string json = $"{{\"object_path\":null,\"uploaded_by\":\"{userId}\",\"mime_type\":null,\"file_size_bytes\":0,\"width\":0,\"height\":0}}";
            using (var request = JsonRequest(url, "PATCH", json, auth))
            {
                request.SetRequestHeader("Prefer", "return=representation");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { onError?.Invoke(RequestError("Could not clear picture", request)); yield break; }
                if (!TryReadRecord(request.downloadHandler.text, out var record)) { onError?.Invoke("No picture record was cleared."); yield break; }
                onSuccess?.Invoke(record);
            }
        }

        /// <summary>Downloads one selected image with the current viewer's token.</summary>
        public void Download(FrameRecord record, Action<byte[]> onSuccess, Action<string> onError)
        {
            if (!TryGetContext(out var auth, out var runtime, out var error)) { onError?.Invoke(error); return; }
            if (record == null || !string.Equals(record.space_id, runtime.spaceId, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(record.frame_id, out _) || !Guid.TryParse(record.uploaded_by, out _) ||
                string.IsNullOrEmpty(record.object_path) || record.file_size_bytes < 1 || record.file_size_bytes > MaxImageBytes ||
                record.width < 1 || record.height < 1 || record.width > MaxImageDimension || record.height > MaxImageDimension ||
                !record.object_path.StartsWith($"users/{record.uploaded_by}/spaces/{record.space_id}/frames/{record.frame_id}/", StringComparison.Ordinal))
            { onError?.Invoke("Picture metadata is invalid."); return; }
            StartCoroutine(DownloadRoutine(auth, record.object_path, onSuccess, onError));
        }

        private IEnumerator DownloadRoutine(SupabaseAuthManager auth, string path, Action<byte[]> onSuccess, Action<string> onError)
        {
            string url = $"{auth.Config.SupabaseUrl}/storage/v1/object/{BucketName}/{EscapePath(path)}";
            using (var request = UnityWebRequest.Get(url))
            {
                Authorize(request, auth);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { onError?.Invoke(RequestError("Could not download picture", request)); yield break; }
                byte[] bytes = request.downloadHandler.data;
                if (bytes == null || bytes.Length == 0 || bytes.Length > MaxImageBytes) { onError?.Invoke("Downloaded image exceeds the 8 MB limit or is empty."); yield break; }
                onSuccess?.Invoke(bytes);
            }
        }

        private static UnityWebRequest JsonRequest(string url, string method, string json, SupabaseAuthManager auth)
        {
            var request = new UnityWebRequest(url, method);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            Authorize(request, auth);
            return request;
        }

        private static void Authorize(UnityWebRequest request, SupabaseAuthManager auth)
        {
            request.SetRequestHeader("apikey", auth.Config.AnonKey);
            request.SetRequestHeader("Authorization", auth.GetAuthHeader());
        }

        private static bool TryReadRecord(string json, out FrameRecord record)
        {
            record = null;
            try
            {
                var list = JsonUtility.FromJson<FrameRecordList>("{\"items\":" + json + "}");
                record = list?.items != null && list.items.Length == 1 ? list.items[0] : null;
            }
            catch (Exception) { return false; }
            return record != null;
        }

        private static string Escape(string value) => UnityWebRequest.EscapeURL(value);
        private static string EscapePath(string path) => Escape(path).Replace("%2F", "/");
        private static string RequestError(string prefix, UnityWebRequest request) => $"{prefix} (HTTP {request.responseCode}).";
    }
}
