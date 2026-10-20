using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDyn
{
    /// <summary>
    /// Drop this on a frame object and assign its child picture renderer. The space owner can
    /// select a JPEG or PNG; all signed-in viewers restore the durable image from Supabase.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class MetaDynEmptyFrame : MonoBehaviour
    {
        [Header("Frame Identity")]
        [SerializeField, Tooltip("Stable GUID for this frame within the space. Give duplicated frames a new ID.")]
        private string frameId;

        [Header("Picture Surface")]
        [SerializeField, Tooltip("Child renderer whose texture displays the picture.")]
        private Renderer pictureRenderer;
        [SerializeField, Tooltip("Leave empty to choose _BaseMap or _MainTex from the material.")]
        private string textureProperty;
        [SerializeField, Tooltip("Shown until an image is saved, and after the image is cleared. If empty, use the material's current texture.")]
        private Texture placeholderTexture;

        [Header("Refresh")]
        [SerializeField, Range(2f, 30f), Tooltip("Seconds between Supabase checks so other connected players see updates. No image bytes go through NGO.")]
        private float refreshSeconds = 5f;

        private Interactable interactable;
        private MetaDynPictureFrameStorageService storage;
        private Material originalMaterial;
        private Material runtimeMaterial;
        private Texture2D loadedTexture;
        private string activePath;
        private string propertyName;
        private bool pickerOpen;
        private bool requestInFlight;
        private bool mutationInFlight;
        private string lastContextError;
        private int generation;
        private Coroutine refreshRoutine;

        /// <summary>Stable identifier used with the current space ID.</summary>
        public string FrameId => frameId;

        private void Reset()
        {
            if (string.IsNullOrEmpty(frameId)) frameId = Guid.NewGuid().ToString();
            pictureRenderer = FindChildRenderer();
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(frameId)) frameId = Guid.NewGuid().ToString();
            refreshSeconds = Mathf.Clamp(refreshSeconds, 2f, 30f);
        }

        [ContextMenu("Generate New Frame ID")]
        private void GenerateNewFrameId() => frameId = Guid.NewGuid().ToString();

        private void Awake()
        {
            interactable = GetComponent<Interactable>();
            if (pictureRenderer == null) pictureRenderer = FindChildRenderer();
            if (pictureRenderer == null)
            {
                Debug.LogError("[MetaDynEmptyFrame] No picture renderer is assigned or found on a child. The frame cannot respond to E until this is fixed.", this);
                enabled = false;
                return;
            }
            if (!Guid.TryParse(frameId, out _))
            {
                Debug.LogError("[MetaDynEmptyFrame] Frame ID is not a valid GUID. The frame cannot respond to E until this is fixed.", this);
                enabled = false;
                return;
            }

            Material material = pictureRenderer.sharedMaterial;
            propertyName = !string.IsNullOrEmpty(textureProperty) ? textureProperty :
                material != null && material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            if (material == null || !material.HasProperty(propertyName))
            {
                Debug.LogError("[MetaDynEmptyFrame] The picture material has no matching texture property. The frame cannot respond to E until this is fixed.", this);
                enabled = false;
                return;
            }
            if (placeholderTexture == null) placeholderTexture = material.GetTexture(propertyName);
            originalMaterial = material;
            runtimeMaterial = pictureRenderer.material;
            ApplyTexture(placeholderTexture);
            storage = MetaDynPictureFrameStorageService.GetOrCreate();
        }

        private Renderer FindChildRenderer()
        {
            foreach (var candidate in GetComponentsInChildren<Renderer>(true))
                if (candidate.gameObject != gameObject) return candidate;
            return null;
        }

        private void OnEnable()
        {
            if (interactable != null)
            {
                if (interactable.OnInteract == null) interactable.OnInteract = new UnityEvent();
                interactable.OnInteract.AddListener(OpenImagePicker);
            }
            if (storage != null) refreshRoutine = StartCoroutine(RefreshLoop());
        }

        private void Start()
        {
            if (refreshRoutine == null && storage != null) refreshRoutine = StartCoroutine(RefreshLoop());
        }

        private void OnDisable()
        {
            if (interactable != null && interactable.OnInteract != null)
                interactable.OnInteract.RemoveListener(OpenImagePicker);
            if (refreshRoutine != null) StopCoroutine(refreshRoutine);
            refreshRoutine = null;
            generation++;
            requestInFlight = false;
            mutationInFlight = false;
            pickerOpen = false;
        }

        private void OnDestroy()
        {
            if (pictureRenderer != null && runtimeMaterial != null && pictureRenderer.sharedMaterial == runtimeMaterial)
                pictureRenderer.sharedMaterial = originalMaterial;
            if (loadedTexture != null) Destroy(loadedTexture);
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
        }

        private IEnumerator RefreshLoop()
        {
            while (true)
            {
                if (!requestInFlight)
                {
                    if (MetaDynPictureFrameStorageService.TryGetContext(out _, out _, out var contextError))
                    {
                        lastContextError = null;
                        Refresh();
                    }
                    else if (contextError != lastContextError)
                    {
                        lastContextError = contextError;
                        Debug.LogWarning("[MetaDynEmptyFrame] Frame " + frameId + " cannot check for image updates: " + contextError, this);
                    }
                }
                yield return new WaitForSeconds(refreshSeconds);
            }
        }

        /// <summary>Rechecks the durable image record immediately.</summary>
        public void Refresh()
        {
            if (requestInFlight || storage == null) return;
            requestInFlight = true;
            int currentGeneration = ++generation;
            Debug.Log("[MetaDynEmptyFrame] Checking image assignment for space " +
                MetaDynRuntimeConfig.Instance?.spaceId + ", frame " + frameId + ".", this);
            storage.Fetch(frameId, record =>
            {
                if (this == null || currentGeneration != generation) return;
                if (record == null || string.IsNullOrEmpty(record.object_path))
                {
                    requestInFlight = false;
                    activePath = null;
                    Debug.Log("[MetaDynEmptyFrame] Frame " + frameId +
                        (record == null ? " has no image assignment." : " has an empty image assignment."), this);
                    ApplyTexture(placeholderTexture);
                    ReleaseLoadedTexture();
                    return;
                }
                if (record.object_path == activePath)
                {
                    requestInFlight = false;
                    Debug.Log("[MetaDynEmptyFrame] Frame " + frameId + " image is current (revision " + record.revision + ").", this);
                    return;
                }
                Debug.Log("[MetaDynEmptyFrame] Frame " + frameId + " found image revision " +
                    record.revision + "; downloading.", this);
                storage.Download(record, bytes =>
                {
                    if (this == null || currentGeneration != generation) return;
                    requestInFlight = false;
                    if (TryApplyImage(bytes, record.object_path, out var error))
                    {
                        Debug.Log("[MetaDynEmptyFrame] Frame " + frameId + " applied image revision " + record.revision + ".", this);
                        return;
                    }
                    Debug.LogWarning("[MetaDynEmptyFrame] Frame " + frameId + ": " + error, this);
                }, error => { if (this == null || currentGeneration != generation) return; requestInFlight = false; Debug.LogWarning("[MetaDynEmptyFrame] Frame " + frameId + ": " + error, this); });
            }, error => { if (this == null || currentGeneration != generation) return; requestInFlight = false; Debug.LogWarning("[MetaDynEmptyFrame] Frame " + frameId + ": " + error, this); });
        }

        /// <summary>Opens the image picker for the authenticated space owner.</summary>
        public void OpenImagePicker()
        {
            Debug.Log("[MetaDynEmptyFrame] Interaction received for frame " + frameId + ".", this);
            if (pickerOpen)
            {
                Debug.LogWarning("[MetaDynEmptyFrame] The image chooser is already open.", this);
                return;
            }
            if (mutationInFlight)
            {
                Debug.LogWarning("[MetaDynEmptyFrame] Wait for the current image change to finish.", this);
                return;
            }
            if (!MetaDynPictureFrameStorageService.CanCurrentUserEdit(out var error))
            {
                Debug.LogWarning("[MetaDynEmptyFrame] " + error, this);
                return;
            }
            if (storage == null)
            {
                Debug.LogError("[MetaDynEmptyFrame] Picture storage service is unavailable.", this);
                return;
            }
            pickerOpen = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!storage.OpenWebGLPicker(this)) OnImagePickerFailed("Another picture selection is already open.");
#elif UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("Choose frame image", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path)) { pickerOpen = false; return; }
            try { UploadSelectedImage(File.ReadAllBytes(path), Path.GetExtension(path)); }
            catch (Exception) { OnImagePickerFailed("Could not read the selected image."); }
#else
            OnImagePickerFailed("Image selection is currently supported in WebGL and the Unity Editor.");
#endif
        }

        /// <summary>Called by the picture-specific WebGL file picker.</summary>
        public void OnImageSelected(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
            {
                OnImagePickerFailed("The browser did not return an image file.");
                return;
            }
            try { if (pickerOpen) UploadSelectedImage(File.ReadAllBytes(virtualPath), Path.GetExtension(virtualPath)); }
            catch (Exception) { OnImagePickerFailed("Could not read the selected image."); }
            finally { try { File.Delete(virtualPath); } catch (Exception) { /* Temporary browser file only. */ } }
        }

        /// <summary>Called when the browser picker rejects a file or cannot read it.</summary>
        public void OnImagePickerFailed(string error)
        {
            pickerOpen = false;
            Debug.LogWarning("[MetaDynEmptyFrame] " + error, this);
        }

        private void UploadSelectedImage(byte[] bytes, string extension)
        {
            pickerOpen = false;
            extension = extension?.ToLowerInvariant();
            if (extension == ".jpeg") extension = ".jpg";
            if (!TryGetImageDimensions(bytes, extension, out int width, out int height, out var error))
            {
                Debug.LogWarning("[MetaDynEmptyFrame] " + error, this);
                return;
            }
            var validationTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            bool decoded = ImageConversion.LoadImage(validationTexture, bytes, true) &&
                validationTexture.width == width && validationTexture.height == height;
            Destroy(validationTexture);
            if (!decoded)
            {
                Debug.LogWarning("[MetaDynEmptyFrame] Image could not be decoded safely.", this);
                return;
            }
            requestInFlight = true;
            mutationInFlight = true;
            int currentGeneration = ++generation;
            storage.Upload(frameId, bytes, extension, width, height, record =>
            {
                if (this == null || currentGeneration != generation) return;
                requestInFlight = false;
                mutationInFlight = false;
                if (!TryApplyImage(bytes, record.object_path, out var applyError))
                    Debug.LogWarning("[MetaDynEmptyFrame] " + applyError, this);
            }, uploadError =>
            {
                if (this == null || currentGeneration != generation) return;
                requestInFlight = false;
                mutationInFlight = false;
                Debug.LogWarning("[MetaDynEmptyFrame] " + uploadError, this);
            });
        }

        /// <summary>Clears this frame's current image; wire this to a UI button when needed.</summary>
        public void ClearImage()
        {
            if (requestInFlight || storage == null) return;
            requestInFlight = true;
            mutationInFlight = true;
            int currentGeneration = ++generation;
            storage.Clear(frameId, _ =>
            {
                if (this == null || currentGeneration != generation) return;
                requestInFlight = false;
                mutationInFlight = false;
                activePath = null;
                ApplyTexture(placeholderTexture);
                ReleaseLoadedTexture();
            }, error =>
            {
                if (this == null || currentGeneration != generation) return;
                requestInFlight = false;
                mutationInFlight = false;
                Debug.LogWarning("[MetaDynEmptyFrame] " + error, this);
            });
        }

        private bool TryApplyImage(byte[] bytes, string path, out string error)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (!TryGetImageDimensions(bytes, extension, out int width, out int height, out error)) return false;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width != width || texture.height != height)
            {
                Destroy(texture);
                error = "Image could not be decoded safely.";
                return false;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            ApplyTexture(texture);
            ReleaseLoadedTexture();
            loadedTexture = texture;
            activePath = path;
            return true;
        }

        private void ApplyTexture(Texture texture)
        {
            if (runtimeMaterial == null) return;
            runtimeMaterial.SetTexture(propertyName, texture);
            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor("_BaseColor", Color.white);
            else if (runtimeMaterial.HasProperty("_Color"))
                runtimeMaterial.SetColor("_Color", Color.white);
        }

        private void ReleaseLoadedTexture()
        {
            if (loadedTexture != null) Destroy(loadedTexture);
            loadedTexture = null;
        }

        private static bool TryGetImageDimensions(byte[] bytes, string extension, out int width, out int height, out string error)
        {
            width = height = 0;
            error = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MetaDynPictureFrameStorageService.MaxImageBytes)
                error = "Image must be between 1 byte and 8 MB.";
            else if (extension == ".png")
            {
                if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 ||
                    bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10 ||
                    bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82)
                    error = "Invalid PNG image.";
                else
                {
                    width = ReadBigEndian32(bytes, 16);
                    height = ReadBigEndian32(bytes, 20);
                }
            }
            else if (extension == ".jpg")
            {
                if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216) error = "Invalid JPEG image.";
                else
                {
                    for (int offset = 2; offset + 9 < bytes.Length;)
                    {
                        if (bytes[offset++] != 255) break;
                        while (offset < bytes.Length && bytes[offset] == 255) offset++;
                        if (offset >= bytes.Length) break;
                        int marker = bytes[offset++];
                        if (marker == 217 || marker == 218) break;
                        if (marker == 1 || marker >= 208 && marker <= 215) continue;
                        if (offset + 2 > bytes.Length) break;
                        int length = bytes[offset] * 256 + bytes[offset + 1];
                        if (length < 2 || offset + length > bytes.Length) break;
                        if ((marker >= 192 && marker <= 195) || (marker >= 197 && marker <= 199) ||
                            (marker >= 201 && marker <= 203) || (marker >= 205 && marker <= 207))
                        {
                            if (length < 7) break;
                            height = bytes[offset + 3] * 256 + bytes[offset + 4];
                            width = bytes[offset + 5] * 256 + bytes[offset + 6];
                            break;
                        }
                        offset += length;
                    }
                    if (width == 0 || height == 0) error = "Invalid JPEG dimensions.";
                }
            }
            else error = "Choose a JPEG or PNG image.";

            if (error == null && (width < 1 || height < 1 || width > MetaDynPictureFrameStorageService.MaxImageDimension ||
                height > MetaDynPictureFrameStorageService.MaxImageDimension))
                error = "Image dimensions must be at most 2048 pixels on each edge.";
            return error == null;
        }

        private static int ReadBigEndian32(byte[] data, int offset) =>
            (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
