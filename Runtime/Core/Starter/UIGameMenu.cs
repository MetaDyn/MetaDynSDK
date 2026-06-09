using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using MetaDyn.Dashboard;
using MetaDyn.Networking;

namespace Starter
{
	/// <summary>
	/// Shows in-game menu, handles player connecting/disconnecting to the network game and cursor locking.
	/// Now supports automatic world joining via MetaDynRuntimeConfig and UGS.
	/// </summary>
	public class UIGameMenu : MonoBehaviour
	{
		[Header("Start Game Setup")]
		[Tooltip("Specifies which game mode player should join - e.g. Platformer, ThirdPersonCharacter")]
		public string GameModeIdentifier;
		public int MaxPlayerCount = 8;

		[Header("Debug")]
		[Tooltip("For debug purposes it is possible to force single-player game (starts faster)")]
		public bool ForceSinglePlayer;
		
		[Header("Editor Testing")]
		[Tooltip("In Editor only: Override the configured room name for testing. Leave empty to use MetaDynRuntimeConfig.")]
		public string EditorRoomNameOverride = "";

		[Header("UI Setup")]
		public CanvasGroup PanelGroup;
		
		[Header("UI References")]
		[Tooltip("Optional: Text field to display the configured world name")]
		public TextMeshProUGUI WorldNameText;
		
		[Tooltip("Optional: Legacy room input field (hidden if runtime config exists)")]
		public TMP_InputField RoomText;
		
		public TMP_InputField NicknameText;
		public TextMeshProUGUI DisplayNameText;
		public TextMeshProUGUI StatusText;
		public GameObject StartGroup;
		public GameObject DisconnectGroup;

		[Header("Profile Display")]
		[Tooltip("RawImage to display user's profile avatar from Supabase storage")]
		public RawImage ProfileAvatarImage;

		[Tooltip("Optional: Default texture to show while loading or if no avatar")]
		public Texture2D DefaultAvatarTexture;

		[Header("Avatar Selection")]
		[Tooltip("Container for Ready Player Me avatar entries (horizontal scrollview content)")]
		public Transform RPMAvatarContainer;

		[Tooltip("Container for Avatar SDK avatar entries (horizontal scrollview content)")]
		public Transform AvatarSDKContainer;

		[Tooltip("Prefab for avatar selection entries (should have Image for thumbnail and Button component)")]
		public GameObject AvatarEntryPrefab;

		[Header("Events")]
		[Tooltip("Called when player successfully connects and spawns")]
		public UnityEvent OnGameConnected;

		[Tooltip("Called when player disconnects from game")]
		public UnityEvent OnGameDisconnected;

		private bool _ugsSessionConnected = false;
		private static string _shutdownStatus;
		private const string AVATAR_CHOICE_KEY = "AvatarChoice";
		private List<GameObject> _spawnedAvatarEntries = new List<GameObject>();
		private bool _avatarsPopulated = false;
		private bool _pendingSpawnAfterAvatarSelection = false;
		private bool _isStartingGame = false;

		private bool HasPanelGroup => PanelGroup != null;

		public async void StartGame()
		{
			if (_isStartingGame)
				return;

			// Verify authentication if required before starting network session
			if (WebAuthBridge.Instance != null && WebAuthBridge.Instance.RequireAuthentication)
			{
				if (SupabaseAuthManager.Instance == null || !SupabaseAuthManager.Instance.IsAuthenticated)
				{
					if (StatusText != null)
						StatusText.text = "Authentication required to join.";
					
					Debug.LogWarning("[UIGameMenu] StartGame aborted: Authentication is required but user is not logged in.");
					return;
				}
			}

			_isStartingGame = true;

			if (this == null)
				return;

			PlayerPrefs.SetString("PlayerName", NicknameText.text);

			var runtimeConfig = MetaDyn.MetaDynRuntimeConfig.Instance;
			if (runtimeConfig == null || !runtimeConfig.IsValid())
			{
				if (StatusText != null)
					StatusText.text = "Connection Failed: MetaDyn runtime config is missing or invalid.";

				Debug.LogError("[MetaDyn] Cannot join UGS session: MetaDynRuntimeConfig is missing or invalid.");
				_isStartingGame = false;
				return;
			}

			// Get room name from runtime config
			string roomName = runtimeConfig.roomName;
			string roomNameOverride = null;
			Debug.Log($"[MetaDyn] Using runtime config - World: {roomName}, Max Players: {runtimeConfig.maxPlayers}");
			
			// EDITOR ONLY: Allow override for testing
			#if UNITY_EDITOR
			if (!string.IsNullOrEmpty(EditorRoomNameOverride))
			{
				roomName = EditorRoomNameOverride;
				roomNameOverride = EditorRoomNameOverride;
				Debug.Log($"[MetaDyn] EDITOR OVERRIDE: Using room name '{roomName}' for testing");
			}
			#endif

			if (StatusText != null)
			{
				StatusText.text = $"Joining {runtimeConfig.worldDisplayName}...";
			}

			try
			{
				var joinResult = await MetaDynUGSSessionService.Instance.JoinConfiguredWorldAsync(
					runtimeConfig,
					NicknameText.text,
					roomNameOverride);

				if (this == null)
					return;

				if (joinResult.Ok)
				{
					_ugsSessionConnected = true;

					if (StatusText != null)
						StatusText.text = "Waiting for player spawn...";

					// Trigger player spawning now that the UI is hidden
					MetaDynUGSSessionService.Instance.EnsurePlayerObjectsSpawned();
				}
				else
{
					if (StatusText != null)
						StatusText.text = $"Connection Failed: {joinResult.Error}";
					_ugsSessionConnected = false;
				}
			}
			catch (Exception ex)
			{
				if (StatusText != null)
					StatusText.text = $"Unexpected Error: {ex.Message}";
				_ugsSessionConnected = false;
			}
			finally
			{
				_isStartingGame = false;
			}

			var nickname = PlayerPrefs.GetString("PlayerName");
			if (DisplayNameText != null)
			{
				DisplayNameText.text = nickname; 
			}
		}

		public async void DisconnectClicked()
		{
			await Disconnect();
		}

		public async void BackToMenu()
		{
			await Disconnect();

			SceneManager.LoadScene(0);
		}

		public void TogglePanelVisibility()
		{
			if (!HasPanelGroup)
				return;

			if (PanelGroup.gameObject.activeSelf && !_ugsSessionConnected)
				return; // Panel cannot be hidden if the game is not running

			PanelGroup.gameObject.SetActive(!PanelGroup.gameObject.activeSelf);
		}

		private void OnEnable()
		{
			// Subscribe to local player ready event
			MetaDyn.Networking.MetaDynUGSPlayerController.OnLocalPlayerReady += HandleLocalPlayerReady;

			// Load or generate nickname (will be overwritten by profile if authenticated)
var nickname = PlayerPrefs.GetString("PlayerName");
			if (string.IsNullOrEmpty(nickname))
			{
				nickname = "Player" + UnityEngine.Random.Range(10000, 100000);
			}
			NicknameText.text = nickname;

			// NEW: Display configured world name and hide/show legacy room input
			UpdateWorldDisplayAndRoomInput();

			// Avatar population will happen in Update()
			_avatarsPopulated = false;

			// Try to load previous shutdown status
			if (StatusText != null)
				StatusText.text = _shutdownStatus != null ? _shutdownStatus : string.Empty;
			_shutdownStatus = null;

			// Try web-first authentication for WebGL builds
			TryWebAuthentication();
		}

		private void OnDisable()
		{
			MetaDyn.Networking.MetaDynUGSPlayerController.OnLocalPlayerReady -= HandleLocalPlayerReady;
		}

		private void HandleLocalPlayerReady()
		{
			Debug.Log("[UIGameMenu] Local player is ready, hiding menu.");
			
			if (StatusText != null)
				StatusText.text = "";

			if (HasPanelGroup)
				PanelGroup.gameObject.SetActive(false);

			// Invoke connected event
			OnGameConnected?.Invoke();
		}

		/// <summary>
		/// Attempt web-first authentication for WebGL builds.
/// If token found, validate and auto-spawn.
		/// If no token, redirect to dashboard login.
		/// Falls through to normal flow if auth disabled or not in WebGL.
		/// </summary>
		private void TryWebAuthentication()
		{
			// Check if authentication is required at all
			if (WebAuthBridge.Instance == null || !WebAuthBridge.Instance.RequireAuthentication)
			{
				Debug.Log("[UIGameMenu] Authentication not required, using guest mode");
				return; // Normal flow - user picks nickname/avatar and clicks Start
			}

#if UNITY_WEBGL && !UNITY_EDITOR
			if (LoginUI.ShouldHandleAuthenticationInBuild())
			{
				Debug.Log("[UIGameMenu] LoginUI is handling authentication for this WebGL build");
				return;
			}

			// Check if web auth is enabled
			if (!WebAuthBridge.Instance.EnableWebAuth)
			{
				Debug.Log("[UIGameMenu] Web auth disabled, LoginUI will handle authentication");
				return;
			}

			// Keep panel visible like manual flow - it will be hidden after connection succeeds
			StatusText.text = "Checking authentication...";

			// Check if SupabaseAuthManager exists
			if (SupabaseAuthManager.Instance == null)
			{
				Debug.LogError("[UIGameMenu] SupabaseAuthManager not found, cannot authenticate");
				PanelGroup.gameObject.SetActive(true);
				return;
			}

			// Try to initialize from web token
			SupabaseAuthManager.Instance.InitializeFromWebToken(
				OnWebAuthSuccess,
				(error) => {
					// This typically won't be called as we redirect to login if no token
					Debug.LogError($"[UIGameMenu] Web auth failed: {error}");
						if (HasPanelGroup)
							PanelGroup.gameObject.SetActive(true);
					}
				);
#endif
		}

		/// <summary>
		/// Called when web authentication succeeds
		/// </summary>
		private void OnWebAuthSuccess(SupabaseUser user)
		{
			Debug.Log($"[UIGameMenu] Web auth successful! User: {user.email}");
			if (StatusText != null)
				StatusText.text = "Fetching profile...";

			// Fetch profile to get display name and avatar
			SupabaseAuthManager.Instance.FetchProfile(user.id, OnProfileFetched, OnProfileFetchFailed);
		}

		/// <summary>
		/// Called when profile is fetched after web auth
		/// </summary>
		private void OnProfileFetched(SupabaseProfile profile)
		{
			Debug.Log($"[UIGameMenu] Profile loaded! Name: {profile.name}, Avatar Index: {profile.avatar_index}, Avatar URL: {profile.avatar_url ?? "none"}");

			// Ensure panel is visible during connection
			if (PanelGroup != null)
				PanelGroup.gameObject.SetActive(true);

			ApplyAuthenticatedProfile(profile);

			if (StatusText != null)
				StatusText.text = $"Welcome, {profile.name}!";

			// Check if user has selected an avatar
			bool hasAvatar = LoadAvatarFromProfile(profile);

			if (hasAvatar)
			{
				// Avatar already chosen - prompt user to start manually
				if (StatusText != null)
					StatusText.text = "Welcome back! Avatar loaded. Press Start to join.";
			}
			else
			{
				// No avatar chosen - show picker, spawn after selection
				ShowAvatarPickerThenSpawn();
			}
		}

		/// <summary>
		/// Apply authenticated profile data to the shared game menu UI.
		/// Used by both web auth and in-Unity login so the same profile image/name path is updated.
		/// </summary>
		public void ApplyAuthenticatedProfile(SupabaseProfile profile)
		{
			if (profile == null)
				return;

			if (NicknameText != null && !string.IsNullOrEmpty(profile.name))
			{
				NicknameText.text = profile.name;
				PlayerPrefs.SetString("PlayerName", profile.name);
			}

			if (DisplayNameText != null && !string.IsNullOrEmpty(profile.name))
			{
				DisplayNameText.text = profile.name;
			}

			LoadProfileAvatarImage(profile);
		}

		/// <summary>
		/// Load the user's profile avatar image from Supabase storage bucket
		/// </summary>
		private void LoadProfileAvatarImage(SupabaseProfile profile)
		{
			if (ProfileAvatarImage == null)
			{
				Debug.Log("[UIGameMenu] ProfileAvatarImage not assigned, skipping avatar load");
				return;
			}

			// Show default texture while loading
			if (DefaultAvatarTexture != null)
			{
				ProfileAvatarImage.texture = DefaultAvatarTexture;
				ProfileAvatarImage.enabled = true;
			}

			// Check if profile has an avatar URL
			if (profile == null || string.IsNullOrEmpty(profile.avatar_url))
			{
				Debug.Log("[UIGameMenu] No avatar_url in profile, using default");
				return;
			}

			// Load the avatar image from URL
			if (SupabaseAuthManager.Instance != null)
			{
				SupabaseAuthManager.Instance.LoadImageFromUrl(
					profile.avatar_url,
					onSuccess: (texture) => {
						if (ProfileAvatarImage != null)
						{
							ProfileAvatarImage.texture = texture;
							ProfileAvatarImage.enabled = true;
							Debug.Log($"[UIGameMenu] Profile avatar loaded: {texture.width}x{texture.height}");
						}
					},
					onError: (error) => {
						Debug.LogWarning($"[UIGameMenu] Failed to load profile avatar: {error}");
					}
				);
			}
		}

		private System.Collections.IEnumerator DelayedStartGame()
		{
			// Wait for one frame to ensure UI and state are settled
			yield return null;
			StartGame();
		}

		/// <summary>
		/// Called when profile fetch fails
		/// </summary>
		private void OnProfileFetchFailed(string error)
		{
			Debug.LogError($"[UIGameMenu] Profile fetch failed: {error}");
			if (StatusText != null)
				StatusText.text = $"Failed to load profile: {error}";

			if (HasPanelGroup)
				PanelGroup.gameObject.SetActive(true);
		}
		
		/// <summary>
		/// NEW: Update the world name display and manage legacy room input visibility
		/// </summary>
		private void UpdateWorldDisplayAndRoomInput()
		{
			bool hasRuntimeConfig = MetaDyn.MetaDynRuntimeConfig.Instance != null && 
			                        MetaDyn.MetaDynRuntimeConfig.Instance.IsValid();
			
			// Update world name text if available
			if (WorldNameText != null)
			{
				if (hasRuntimeConfig)
				{
					string displayName = MetaDyn.MetaDynRuntimeConfig.Instance.worldDisplayName;
					WorldNameText.text = $"World: {displayName}";
					WorldNameText.gameObject.SetActive(true);
				}
				else
				{
					WorldNameText.gameObject.SetActive(false);
				}
			}
			
			// Hide legacy room input if runtime config exists
			if (RoomText != null)
			{
				if (hasRuntimeConfig)
				{
					// Hide the input field since we're using runtime config
					RoomText.gameObject.SetActive(false);
				}
				else
				{
					// Show the input field as fallback
					RoomText.gameObject.SetActive(true);
					
					// Load saved room name if available
					string savedRoom = PlayerPrefs.GetString("RoomName", "");
					if (!string.IsNullOrEmpty(savedRoom))
					{
						RoomText.text = savedRoom;
					}
				}
			}
			
			// Log configuration status
			if (hasRuntimeConfig)
			{
				Debug.Log($"[MetaDyn] Configured to join world: {MetaDyn.MetaDynRuntimeConfig.Instance.roomName}");
			}
			else
			{
				Debug.LogWarning("[MetaDyn] No runtime config found - using manual room entry");
			}
		}

		private void Update()
		{
			// Populate avatar selection once Registry exists
			if (!_avatarsPopulated && MetaDynUGSAvatarRegistry.Instance != null)
			{
				PopulateAvatarSelection();
				UpdateAvatarButtonVisuals();
				_avatarsPopulated = true;
			}

			// Enter/Esc key is used for locking/unlocking cursor in game view.
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				TogglePanelVisibility();
			}

			if (HasPanelGroup && PanelGroup.gameObject.activeSelf)
			{
				if (StartGroup != null)
					StartGroup.SetActive(!_ugsSessionConnected);
				if (DisconnectGroup != null)
					DisconnectGroup.SetActive(_ugsSessionConnected);

				// Only allow room text editing if runtime config doesn't exist
				if (RoomText != null)
				{
					bool hasRuntimeConfig = MetaDyn.MetaDynRuntimeConfig.Instance != null &&
					                        MetaDyn.MetaDynRuntimeConfig.Instance.IsValid();
					RoomText.interactable = !_ugsSessionConnected && !hasRuntimeConfig;

					// Save room name changes if in legacy mode
					if (!hasRuntimeConfig && !_ugsSessionConnected)
					{
						PlayerPrefs.SetString("RoomName", RoomText.text);
					}
				}

				if (NicknameText != null)
					NicknameText.interactable = !_ugsSessionConnected;

				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
			else if (MetaDyn.Dashboard.WebAuthBridge.Instance != null && !MetaDyn.Dashboard.WebAuthBridge.Instance.EnableWebAuth)
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
			else if (LoginUI.ShouldKeepCursorUnlocked())
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
			else
			{
				// Only lock cursor on true desktop platforms. 
				// On WebGL and Mobile, we keep it unlocked to avoid browser security banners and support touch.
				#if !UNITY_ANDROID && !UNITY_IOS && !UNITY_WEBGL
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
				#else
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
				#endif
			}
}

		public async Task Disconnect()
		{
			if (!_ugsSessionConnected)
				return;

			if (StatusText != null)
				StatusText.text = "Disconnecting...";

			if (HasPanelGroup)
				PanelGroup.interactable = false;

			if (_ugsSessionConnected)
			{
				await MetaDynUGSSessionService.Instance.LeaveSessionAsync();
				_ugsSessionConnected = false;
			}
			
			// Invoke disconnected event
			OnGameDisconnected?.Invoke();

			// Reset of scene network objects is needed, reload the whole scene
			SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
		}

		/// <summary>
		/// Called by UI buttons to select an avatar by index
		/// </summary>
		public void SelectAvatar(int index)
		{
			PlayerPrefs.SetInt(AVATAR_CHOICE_KEY, index);
			PlayerPrefs.Save();
			Debug.Log($"[Avatar] Avatar at index {index} selected");
			UpdateAvatarButtonVisuals();

			// Save to Supabase if authenticated
			if (SupabaseAuthManager.Instance != null && SupabaseAuthManager.Instance.IsAuthenticated)
			{
				SupabaseAuthManager.Instance.UpdateAvatarIndex(
					index,
					() => {
						Debug.Log($"[Avatar] Avatar index {index} saved to Supabase");
						// If we were waiting for avatar selection, update status
							if (_pendingSpawnAfterAvatarSelection)
							{
								_pendingSpawnAfterAvatarSelection = false;
								if (StatusText != null)
									StatusText.text = "Avatar saved! Press Start to join.";
							}
						},
					(error) => {
						Debug.LogWarning($"[Avatar] Failed to save avatar to Supabase: {error}");
						// Update status even if save failed - we have it in PlayerPrefs
							if (_pendingSpawnAfterAvatarSelection)
							{
								_pendingSpawnAfterAvatarSelection = false;
								if (StatusText != null)
									StatusText.text = "Avatar selected. Press Start to join.";
							}
						}
					);
			}
			else if (_pendingSpawnAfterAvatarSelection)
			{
				// Not authenticated, just update status
				_pendingSpawnAfterAvatarSelection = false;
				if (StatusText != null)
					StatusText.text = "Avatar selected. Press Start to join.";
			}
		}

		/// <summary>
		/// Load avatar index from Supabase profile into PlayerPrefs.
		/// Returns true if avatar is set, false if user needs to pick one.
		/// </summary>
		public bool LoadAvatarFromProfile(SupabaseProfile profile)
		{
			if (profile == null)
			{
				Debug.LogWarning("[UIGameMenu] Profile is null, cannot load avatar");
				return false;
			}

			if (profile.NeedsAvatarSelection)
			{
				Debug.Log("[UIGameMenu] User has not selected an avatar yet");
				return false;
			}

			// Valid avatar index from Supabase - store in PlayerPrefs
			PlayerPrefs.SetInt(AVATAR_CHOICE_KEY, profile.avatar_index);
			PlayerPrefs.Save();
			Debug.Log($"[UIGameMenu] Loaded avatar index {profile.avatar_index} from profile");

			// Update visuals if already populated
			if (_avatarsPopulated)
			{
				UpdateAvatarButtonVisuals();
			}

			return true;
		}

		/// <summary>
		/// Show avatar picker and spawn after selection
		/// </summary>
		public void ShowAvatarPickerThenSpawn()
		{
			_pendingSpawnAfterAvatarSelection = true;
			if (HasPanelGroup)
				PanelGroup.gameObject.SetActive(true);
			if (StatusText != null)
				StatusText.text = "Please select an avatar";
			Debug.Log("[UIGameMenu] Waiting for avatar selection before spawning");
		}

		/// <summary>
		/// Populates the avatar selection UI from MetaDynUGSAvatarRegistry
		/// </summary>
		private void PopulateAvatarSelection()
		{
			// Clear existing entries
			foreach (var entry in _spawnedAvatarEntries)
			{
				if (entry != null) Destroy(entry);
			}
			_spawnedAvatarEntries.Clear();

			// Check if we have the required references
			if (AvatarEntryPrefab == null)
			{
				Debug.LogWarning("[UIGameMenu] Avatar entry prefab not assigned");
				return;
			}

			if (MetaDynUGSAvatarRegistry.Instance == null)
			{
				Debug.LogWarning("[UIGameMenu] MetaDynUGSAvatarRegistry not found, cannot populate avatar selection");
				return;
			}

			int totalEntries = 0;

			// Populate Ready Player Me avatars
			if (RPMAvatarContainer != null)
			{
				totalEntries += PopulateAvatarContainerFromRegistry(
					RPMAvatarContainer,
					MetaDynUGSAvatarRegistry.Instance.readyPlayerMeAvatars,
					0
				);
			}

			// Populate Avatar SDK avatars
			if (AvatarSDKContainer != null)
			{
				totalEntries += PopulateAvatarContainerFromRegistry(
					AvatarSDKContainer,
					MetaDynUGSAvatarRegistry.Instance.avatarSDKAvatars,
					MetaDynUGSAvatarRegistry.Instance.readyPlayerMeAvatars.Count
				);
			}

			Debug.Log($"[UIGameMenu] Total avatar entries: {totalEntries}");

			#if UNITY_WEBGL && !UNITY_EDITOR
			// WebGL: Force canvas rebuild after a frame
			StartCoroutine(RefreshAvatarThumbnailsDelayed());
			#endif
		}

		#if UNITY_WEBGL && !UNITY_EDITOR
		/// <summary>
		/// WebGL-specific: Refresh avatar thumbnails after UI has settled
		/// </summary>
		private System.Collections.IEnumerator RefreshAvatarThumbnailsDelayed()
		{
			yield return new WaitForEndOfFrame();

			foreach (var entry in _spawnedAvatarEntries)
			{
				if (entry == null) continue;

				var images = entry.GetComponentsInChildren<UnityEngine.UI.Image>();
				foreach (var img in images)
				{
					if (img.sprite != null)
					{
						img.enabled = false;
						img.enabled = true;
					}
				}
			}

			Debug.Log("[UIGameMenu] WebGL: Refreshed avatar thumbnails");
		}
		#endif

		/// <summary>
		/// Populates a specific container with avatars from the UGS registry
		/// </summary>
		private int PopulateAvatarContainerFromRegistry(Transform container, List<MetaDynUGSAvatarRegistry.AvatarEntry> avatars, int startIndex)
		{
			int count = 0;

			for (int i = 0; i < avatars.Count; i++)
			{
				var avatarData = avatars[i];
				if (avatarData.prefab == null) continue;

				GameObject entryObj = Instantiate(AvatarEntryPrefab, container);
				_spawnedAvatarEntries.Add(entryObj);

				UnityEngine.UI.Image thumbnailImage = FindThumbnailImage(entryObj);

				if (thumbnailImage != null && avatarData.thumbnail != null)
				{
					thumbnailImage.sprite = avatarData.thumbnail;
					thumbnailImage.preserveAspect = true;
					thumbnailImage.type = UnityEngine.UI.Image.Type.Simple;
					thumbnailImage.enabled = true;
					thumbnailImage.gameObject.SetActive(true);
				}

				var button = entryObj.GetComponent<UnityEngine.UI.Button>();
				if (button != null)
				{
					int combinedIndex = startIndex + i;
					button.onClick.AddListener(() => SelectAvatar(combinedIndex));
				}

				count++;
			}

			return count;
		}

		private UnityEngine.UI.Image FindThumbnailImage(GameObject entryObj)
		{
			Transform thumbnailTransform = entryObj.transform.Find("Thumbnail");
			if (thumbnailTransform == null)
			{
				foreach (Transform child in entryObj.GetComponentsInChildren<Transform>())
				{
					if (child.name.ToLower().Contains("thumbnail"))
					{
						thumbnailTransform = child;
						break;
					}
				}
			}

			if (thumbnailTransform != null)
			{
				return thumbnailTransform.GetComponent<UnityEngine.UI.Image>();
			}

			var allImages = entryObj.GetComponentsInChildren<UnityEngine.UI.Image>(true);
			foreach (var img in allImages)
			{
				if (img.gameObject != entryObj)
				{
					return img;
				}
			}

			return null;
		}

		/// <summary>
		/// Updates button visuals to show which avatar is selected
		/// </summary>
		private void UpdateAvatarButtonVisuals()
		{
			if (_spawnedAvatarEntries.Count == 0) return;

			int choice = PlayerPrefs.GetInt(AVATAR_CHOICE_KEY, 0);

			// Update each entry's button state
			for (int i = 0; i < _spawnedAvatarEntries.Count; i++)
			{
				var button = _spawnedAvatarEntries[i].GetComponent<UnityEngine.UI.Button>();
				if (button != null)
				{
					// Selected button is non-interactable, unselected is interactable
					button.interactable = (i != choice);
				}
			}
		}
	}
}
