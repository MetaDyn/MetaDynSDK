using MetaDyn;
using MetaDyn.Avatar;
using MetaDyn.Dashboard;
using MetaDyn.UserList;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using System.Collections.Generic;

namespace MetaDyn.Networking
{
    /// <summary>
    /// Minimal NGO player controller for the UGS migration path.
    /// NGO player controller for the UGS migration path while keeping the existing PlayerInput and CameraFollow.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(100)]
    public sealed class MetaDynUGSPlayerController : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private Animator animator;

        [Header("First-Person View")]
        [SerializeField] private Transform modelRoot;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 2f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float jumpImpulse = 10f;
        [SerializeField] private float upGravity = -25f;
        [SerializeField] private float downGravity = -40f;
        [SerializeField] private float rotationSpeed = 8f;

        [Header("Movement Accelerations")]
        [SerializeField] private float groundAcceleration = 10f;
        [SerializeField] private float groundDeceleration = 10f;
        [SerializeField] private float airAcceleration = 10f;
        [SerializeField] private float airDeceleration = 1.3f;

        [Header("Grounding")]
        [SerializeField] private float groundedStickForce = -2f;
        [SerializeField] private float groundedProbeDistance = 0.08f;
        [SerializeField] private float minAirTimeForLanding = 0.18f;
        [SerializeField] private float minLandingVelocity = -4f;

        [Header("Sounds")]
        [SerializeField] private AudioClip[] footstepAudioClips;
        [SerializeField] private AudioClip landingAudioClip;
        [Range(0f, 1f)]
        [SerializeField] private float footstepAudioVolume = 0.3f;

        private readonly NetworkVariable<FixedString32Bytes> _networkedName = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<FixedString64Bytes> _networkedAvatarModelId = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        
        private CharacterController _characterController;
private NetworkTransform _networkTransform;
        private CameraFollow _cameraFollow;
        private NameTag _nameTag;
        private Renderer[] _modelRenderers;
        private GameplayInput _latestInput;
        private Vector3 _moveVelocity;
        private float _verticalVelocity;
        private bool _modelHidden;
        private bool _wasGrounded;
        private bool _isJumping;
        private bool _migrationPresentationHidden;
        private float _airborneTime;
        private float _lastVerticalVelocity;
        private float _lastFootstepTime;
        private float _lastLandTime;
        private AnimatorParameterSnapshot[] _pendingAvatarSwapParameters;
        private AnimatorLayerSnapshot[] _pendingAvatarSwapLayers;

        private const float FootstepCooldown = 0.18f;
private const float LandCooldown = 0.35f;

        public string DisplayName => _networkedName.Value.ToString();
        public string ActiveAvatarModelId => _networkedAvatarModelId.Value.ToString();
        public bool IsEmoting { get; private set; }
        public Animator Animator => animator;
        public static MetaDynUGSPlayerController LocalPlayer { get; private set; }

        public static event System.Action OnLocalPlayerReady;
        public static event System.Action<ulong, string, string> OnChatMessageReceived;

        private const int MaxChatMessageCharacters = 120;

        private int _animIDSpeed;
private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;

        private struct AnimatorParameterSnapshot
        {
            public int Hash;
            public AnimatorControllerParameterType Type;
            public float FloatValue;
            public int IntValue;
            public bool BoolValue;
        }

        private struct AnimatorLayerSnapshot
        {
            public int StateHash;
            public float NormalizedTime;
            public float Weight;
        }

        // Emote state
        private RuntimeAnimatorController _originalController;
        private AudioSource _emoteAudioSource;
        private float _emoteStartTime;
        private float _currentEmoteDuration;
        private string _currentEmoteName;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _networkTransform = GetComponent<NetworkTransform>();
            NetworkObject.SynchronizeTransform = false;

            if (playerInput == null)
                playerInput = GetComponent<PlayerInput>();

            if (animator == null)
                animator = GetComponent<Animator>();

            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            AssignAnimationIDs();
            ConfigureCharacterController();
            CacheModelRenderers();
        }

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[MetaDyn SPAWN] Spawned '{name}': owner={OwnerClientId}, localClient={NetworkManager.LocalClientId}, isOwner={IsOwner}.");

            if (_networkTransform != null)
            {
                _networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                _networkTransform.SyncScaleX = false;
                _networkTransform.SyncScaleY = false;
                _networkTransform.SyncScaleZ = false;
                _networkTransform.UseUnreliableDeltas = true;
                _networkTransform.PositionThreshold = 0.01f;
                _networkTransform.RotAngleThreshold = 0.25f;
            }

            _nameTag = GetComponentInChildren<NameTag>(true);
            _networkedName.OnValueChanged += OnNetworkedNameChanged;
            _networkedAvatarModelId.OnValueChanged += OnNetworkedAvatarModelChanged;

            if (playerInput != null)
                playerInput.enabled = IsOwner;

            if (IsOwner)
            {
                LocalPlayer = this;
                MetaDynUGSSessionService.Instance?.ApplyInitialDistributedAuthoritySpawnPose(this);
                PublishDisplayName(PlayerPrefs.GetString("PlayerName", "Guest " + Random.Range(100, 999)));
                BindCamera();
                OnLocalPlayerReady?.Invoke();
            }

            ApplyNetworkedName(_networkedName.Value.ToString());
            ApplyNetworkedAvatarModel(_networkedAvatarModelId.Value.ToString());
            RegisterWithUGSUserList(_networkedName.Value.ToString());

            OwnerNetworkAnimator ownerNetworkAnimator = GetComponent<OwnerNetworkAnimator>();
            if (ownerNetworkAnimator == null)
            {
                Debug.LogError($"[MetaDyn UGS Player] '{name}' requires OwnerNetworkAnimator because locomotion parameters are written by the owning client.", this);
            }
            else if (ownerNetworkAnimator.Animator != animator)
            {
                Debug.LogError($"[MetaDyn UGS Player] '{name}' OwnerNetworkAnimator must reference the same root Animator as MetaDynUGSPlayerController.", this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (LocalPlayer == this)
                LocalPlayer = null;

            _networkedName.OnValueChanged -= OnNetworkedNameChanged;
            _networkedAvatarModelId.OnValueChanged -= OnNetworkedAvatarModelChanged;
            MetaDynUGSUserListManager.Instance?.UnregisterPlayer(OwnerClientId);
            GLBAvatarLoader.Instance?.ReleaseAvatarResources(OwnerClientId);
        }

        public bool TrySendChatMessage(string message)
        {
            if (!IsOwner || !IsSpawned || NetworkManager == null || !NetworkManager.IsConnectedClient)
                return false;

            string sanitizedMessage = SanitizeChatMessage(message);
            if (string.IsNullOrEmpty(sanitizedMessage))
                return false;

            SendChatMessageRpc(new FixedString512Bytes(sanitizedMessage));
            return true;
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        private void SendChatMessageRpc(FixedString512Bytes message)
        {
            string sanitizedMessage = SanitizeChatMessage(message.ToString());
            if (string.IsNullOrEmpty(sanitizedMessage))
                return;

            string senderName = DisplayName;
            if (string.IsNullOrWhiteSpace(senderName))
                senderName = IsOwner
                    ? PlayerPrefs.GetString("PlayerName", $"Player {OwnerClientId}")
                    : $"Player {OwnerClientId}";

            OnChatMessageReceived?.Invoke(OwnerClientId, senderName, sanitizedMessage);
        }

        private static string SanitizeChatMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return string.Empty;

            string sanitized = message
                .Replace('\0', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

            if (sanitized.Length <= MaxChatMessageCharacters)
                return sanitized;

            sanitized = sanitized.Substring(0, MaxChatMessageCharacters);
            if (sanitized.Length > 0 && char.IsHighSurrogate(sanitized[sanitized.Length - 1]))
                sanitized = sanitized.Substring(0, sanitized.Length - 1);

            return sanitized.TrimEnd();
        }

        private void Update()
        {
            if (IsOwner && playerInput != null)
            {
                var input = playerInput.CurrentInput;

                // Block movement input if emoted and movement is not allowed
                bool blockMovement = IsEmoting && EmoteManager.Instance != null && !EmoteManager.Instance.allowMovementDuringEmote;

                if (!blockMovement)
                {
                    _latestInput.MoveDirection = Vector2.ClampMagnitude(input.MoveDirection, 1f);
                    _latestInput.LookRotation = input.LookRotation;
                    _latestInput.Sprint = input.Sprint;
                    _latestInput.Jump = input.Jump;
                }
                else
                {
                    _latestInput.MoveDirection = Vector2.zero;
                    _latestInput.Sprint = false;
                    _latestInput.Jump = false;
                }

                playerInput.ResetInput();

                if (_cameraFollow != null)
                    _cameraFollow.isTargetMoving = _latestInput.MoveDirection.y > 0.1f;

                UpdateFirstPersonVisibility(input);
                SimulateMovement(Time.deltaTime);
            }

            UpdateAnimator();

            // Handle emote timing locally on all clients
            if (IsEmoting)
            {
                float elapsedTime = Time.time - _emoteStartTime;
                float autoStopAfter = EmoteManager.Instance != null ? EmoteManager.Instance.autoStopAfter : 0f;
                float stopTime = autoStopAfter > 0f ? autoStopAfter : _currentEmoteDuration;

                if (elapsedTime >= stopTime)
                {
                    StopEmoteLocal();
                }
            }
        }

        #region EMOTE SYSTEM

        public void RequestPlayEmote(int index)
        {
            if (!IsOwner) return;
            PlayEmoteRpc(index);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        private void PlayEmoteRpc(int index)
        {
            PlayEmoteLocal(index);
        }

        private void PlayEmoteLocal(int index)
        {
            if (EmoteManager.Instance == null)
            {
                Debug.LogWarning("[MetaDyn UGS Player] Cannot play emote: EmoteManager not found in scene.");
                return;
            }

            var emoteData = EmoteManager.Instance.GetEmoteByIndex(index);
            if (emoteData == null || emoteData.animation == null) return;

            if (IsEmoting) StopEmoteLocal();

            if (animator == null) return;

            // Save original controller if not already saved
            if (_originalController == null)
                _originalController = animator.runtimeAnimatorController;

            // Create override controller
            var overrideController = new AnimatorOverrideController(_originalController);
            var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(clips);

            for (int i = 0; i < clips.Count; i++)
            {
                clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clips[i].Key, emoteData.animation);
            }
            overrideController.ApplyOverrides(clips);

            animator.runtimeAnimatorController = overrideController;
            animator.Rebind();

            _currentEmoteDuration = emoteData.animation.length;
            _currentEmoteName = emoteData.emoteName;
            _emoteStartTime = Time.time;
            IsEmoting = true;

            // Audio
            if (emoteData.audioClip != null)
            {
                if (_emoteAudioSource == null) _emoteAudioSource = gameObject.AddComponent<AudioSource>();
                _emoteAudioSource.clip = emoteData.audioClip;
                _emoteAudioSource.spatialBlend = 1f; // Ensure 3D audio
                _emoteAudioSource.Play();
            }

            if (IsOwner && EmoteManager.Instance.debugMode)
                Debug.Log($"[MetaDyn SPAWN] Playing emote: {_currentEmoteName}");
        }

        private void StopEmoteLocal()
        {
            if (!IsEmoting) return;

            if (_emoteAudioSource != null && _emoteAudioSource.isPlaying)
                _emoteAudioSource.Stop();

            if (_originalController != null && animator != null)
            {
                animator.runtimeAnimatorController = _originalController;
                animator.Rebind();
                _originalController = null;
            }

            IsEmoting = false;
            _currentEmoteName = null;
            _currentEmoteDuration = 0f;
        }

        #endregion

        private void PublishDisplayName(string playerName)
        {
            if (!IsSpawned || !IsOwner)
                return;

            if (string.IsNullOrWhiteSpace(playerName))
                playerName = "Guest " + Random.Range(100, 999);

            if (playerName.Length > 31)
                playerName = playerName.Substring(0, 31);

            _networkedName.Value = playerName;
            RegisterWithUGSUserList(playerName);
            Debug.Log($"[MetaDyn SPAWN] Owner published name for client {OwnerClientId}: {playerName}");
        }

        private static string GetSupabaseUserId()
        {
            var authManager = SupabaseAuthManager.Instance;
            if (authManager != null &&
                authManager.IsAuthenticated &&
                authManager.CurrentSession?.user != null)
            {
                return authManager.CurrentSession.user.id;
            }

            return string.Empty;
        }

        private void SimulateMovement(float deltaTime)
        {
            if (_characterController == null || deltaTime <= 0f)
                return;

            bool wasGrounded = _wasGrounded;
            bool grounded = _wasGrounded || _characterController.isGrounded || IsGrounded();
            _lastVerticalVelocity = _verticalVelocity;

            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickForce;
                _isJumping = false;
            }

            if (grounded && _latestInput.Jump)
            {
                _verticalVelocity = jumpImpulse;
                _isJumping = true;
                grounded = false;
                Debug.Log($"[MetaDyn UGS Player] Jump impulse applied once on '{name}'.");
            }

            float speed = _latestInput.Sprint ? sprintSpeed : walkSpeed;
            var lookRotation = Quaternion.Euler(0f, _latestInput.LookRotation.y, 0f);
            var moveDirection = lookRotation * new Vector3(_latestInput.MoveDirection.x, 0f, _latestInput.MoveDirection.y);
            var desiredMoveVelocity = moveDirection * speed;

            float acceleration;
            if (desiredMoveVelocity == Vector3.zero)
            {
                acceleration = grounded ? groundDeceleration : airDeceleration;
            }
            else
            {
                var targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * deltaTime);
                acceleration = grounded ? groundAcceleration : airAcceleration;
            }

            _moveVelocity = Vector3.Lerp(_moveVelocity, desiredMoveVelocity, acceleration * deltaTime);

            float activeGravity = _verticalVelocity >= 0f ? upGravity : downGravity;
            _verticalVelocity += activeGravity * deltaTime;

            Vector3 velocity = _moveVelocity;
            velocity.y = _verticalVelocity;
            CollisionFlags collisionFlags = _characterController.Move(velocity * deltaTime);

            bool groundedAfterMove = (collisionFlags & CollisionFlags.Below) != 0 || _characterController.isGrounded || IsGrounded();
            if (!groundedAfterMove)
            {
                _airborneTime += deltaTime;
            }
            else
            {
                if (_verticalVelocity < 0f)
                    _verticalVelocity = groundedStickForce;

                _isJumping = false;
            }

            if (groundedAfterMove && !wasGrounded && ShouldPlayLanding())
                PlayLandingSound();

            _wasGrounded = groundedAfterMove;

            if (groundedAfterMove)
                _airborneTime = 0f;
}

        private void BindCamera()
        {
            if (Camera.main == null)
            {
                return;
            }

            _cameraFollow = Camera.main.GetComponent<CameraFollow>();
            if (_cameraFollow == null)
            {
                return;
            }

            _cameraFollow.target = transform;
            _cameraFollow.inputSource = playerInput;

            MetaDynUGSMigrationState migrationState = MetaDynUGSSessionService.Instance.MigrationState;
            bool preserveMigrationView = migrationState == MetaDynUGSMigrationState.MigrationDetected ||
                                         migrationState == MetaDynUGSMigrationState.RestartingNetwork;
            if (!preserveMigrationView)
                _cameraFollow.InitializeCameraRotation(transform.eulerAngles.y);
        }

        private void UpdateAnimator()
        {
            if (animator == null || !IsOwner)
                return;

            // When using NetworkAnimator, the Owner sets the values locally, 
            // and the component automatically mirrors them to other clients.
            float currentSpeed = _moveVelocity.magnitude;
            animator.SetFloat(_animIDSpeed, currentSpeed, 0.1f, Time.deltaTime);
            animator.SetFloat(_animIDMotionSpeed, 1f);
            
            bool grounded = _wasGrounded || (_characterController != null && _characterController.isGrounded) || IsGrounded();
            animator.SetBool(_animIDGrounded, grounded);
            animator.SetBool(_animIDJump, _isJumping);
            
            bool isFreeFalling = !grounded && _verticalVelocity < -10f;
            animator.SetBool(_animIDFreeFall, isFreeFalling);
        }

        private void ConfigureCharacterController()
        {
            if (_characterController == null)
                return;

            _characterController.height = 1.8f;
            _characterController.radius = 0.28f;
            _characterController.center = new Vector3(0f, _characterController.height * 0.5f, 0f);
            _characterController.skinWidth = 0.03f;
            _characterController.stepOffset = 0.3f;
            _characterController.minMoveDistance = 0f;
        }

        private bool IsGrounded()
        {
            if (_characterController == null)
                return false;

            Vector3 center = transform.TransformPoint(_characterController.center);
            float bottomSphereOffset = Mathf.Max(0f, (_characterController.height * 0.5f) - _characterController.radius);
            Vector3 origin = center + Vector3.down * bottomSphereOffset + Vector3.up * 0.02f;

            return Physics.SphereCast(
                origin,
                Mathf.Max(0.05f, _characterController.radius * 0.8f),
                Vector3.down,
                out _,
                groundedProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }

        private bool ShouldPlayLanding()
        {
            return _airborneTime >= minAirTimeForLanding && _lastVerticalVelocity <= minLandingVelocity;
        }

        public void RefreshModelRenderers()
        {
            CacheModelRenderers();
            ApplyMigrationPresentationVisibility();
        }

        /// <summary>
        /// Temporarily suppresses only the replacement player's renderers while a
        /// preserved migration visual remains on screen. Input and movement remain live.
        /// </summary>
        public void SetMigrationPresentationHidden(bool hidden)
        {
            _migrationPresentationHidden = hidden;
            ApplyMigrationPresentationVisibility();
        }

        public void RefreshAnimatorAfterAvatarSwap()
        {
            if (animator == null)
                return;

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (IsOwner)
                ApplyOwnerLocomotionParameters();

            animator.Update(0f);
        }

        /// <summary>
        /// Captures synchronized proxy state before the current runtime skeleton hierarchy
        /// is removed. The captured values are consumed by ApplyRuntimeAvatar.
        /// </summary>
        public void PrepareAnimatorForAvatarSwap()
        {
            if (animator == null)
                return;

            _pendingAvatarSwapParameters = CaptureAnimatorParameters();
            _pendingAvatarSwapLayers = CaptureAnimatorLayers();
        }

        /// <summary>
        /// Rebinds the root Animator to a runtime humanoid avatar without discarding the
        /// locomotion state already synchronized to this client by OwnerNetworkAnimator.
        /// </summary>
        public void ApplyRuntimeAvatar(UnityEngine.Avatar runtimeAvatar)
        {
            if (animator == null || runtimeAvatar == null || !runtimeAvatar.isValid)
                return;

            AnimatorParameterSnapshot[] parameterSnapshots = _pendingAvatarSwapParameters ?? CaptureAnimatorParameters();
            AnimatorLayerSnapshot[] layerSnapshots = _pendingAvatarSwapLayers ?? CaptureAnimatorLayers();
            _pendingAvatarSwapParameters = null;
            _pendingAvatarSwapLayers = null;

            animator.avatar = runtimeAvatar;
            animator.Rebind();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            RestoreAnimatorParameters(parameterSnapshots);
            RestoreAnimatorLayers(layerSnapshots);

            // The owner remains the source of truth. Reapply its current controller values
            // without damping so NetworkAnimator observes a coherent post-rebind state.
            if (IsOwner)
                ApplyOwnerLocomotionParameters();

            animator.Update(0f);
        }

        private AnimatorParameterSnapshot[] CaptureAnimatorParameters()
        {
            AnimatorControllerParameter[] parameters = animator.parameters;
            var snapshots = new List<AnimatorParameterSnapshot>(parameters.Length);

            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                var snapshot = new AnimatorParameterSnapshot
                {
                    Hash = parameter.nameHash,
                    Type = parameter.type
                };

                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float:
                        snapshot.FloatValue = animator.GetFloat(parameter.nameHash);
                        break;
                    case AnimatorControllerParameterType.Int:
                        snapshot.IntValue = animator.GetInteger(parameter.nameHash);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        snapshot.BoolValue = animator.GetBool(parameter.nameHash);
                        break;
                    default:
                        // Triggers are event-like and cannot be reconstructed reliably.
                        continue;
                }

                snapshots.Add(snapshot);
            }

            return snapshots.ToArray();
        }

        private AnimatorLayerSnapshot[] CaptureAnimatorLayers()
        {
            var snapshots = new AnimatorLayerSnapshot[animator.layerCount];

            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                AnimatorStateInfo state = animator.IsInTransition(layer)
                    ? animator.GetNextAnimatorStateInfo(layer)
                    : animator.GetCurrentAnimatorStateInfo(layer);

                snapshots[layer] = new AnimatorLayerSnapshot
                {
                    StateHash = state.fullPathHash,
                    NormalizedTime = state.normalizedTime,
                    Weight = animator.GetLayerWeight(layer)
                };
            }

            return snapshots;
        }

        private void RestoreAnimatorParameters(AnimatorParameterSnapshot[] snapshots)
        {
            for (int i = 0; i < snapshots.Length; i++)
            {
                AnimatorParameterSnapshot snapshot = snapshots[i];
                switch (snapshot.Type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(snapshot.Hash, snapshot.FloatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(snapshot.Hash, snapshot.IntValue);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(snapshot.Hash, snapshot.BoolValue);
                        break;
                }
            }
        }

        private void RestoreAnimatorLayers(AnimatorLayerSnapshot[] snapshots)
        {
            int layerCount = Mathf.Min(snapshots.Length, animator.layerCount);
            for (int layer = 0; layer < layerCount; layer++)
            {
                AnimatorLayerSnapshot snapshot = snapshots[layer];
                animator.SetLayerWeight(layer, snapshot.Weight);

                if (snapshot.StateHash != 0 && animator.HasState(layer, snapshot.StateHash))
                    animator.Play(snapshot.StateHash, layer, snapshot.NormalizedTime);
            }
        }

        private void ApplyOwnerLocomotionParameters()
        {
            float currentSpeed = _moveVelocity.magnitude;
            bool grounded = _wasGrounded || (_characterController != null && _characterController.isGrounded) || IsGrounded();
            bool isFreeFalling = !grounded && _verticalVelocity < -10f;

            animator.SetFloat(_animIDSpeed, currentSpeed);
            animator.SetFloat(_animIDMotionSpeed, 1f);
            animator.SetBool(_animIDGrounded, grounded);
            animator.SetBool(_animIDJump, _isJumping);
            animator.SetBool(_animIDFreeFall, isFreeFalling);
        }

        public Transform ModelRoot
        {
            get => modelRoot;
            set => modelRoot = value;
        }

        private void CacheModelRenderers()
{
            if (modelRoot == null)
                modelRoot = transform;

            _modelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        }

        private void UpdateFirstPersonVisibility(GameplayInput input)
        {
            if (_modelRenderers == null)
                return;

            bool shouldHide = input.CameraZoom <= playerInput.firstPersonThreshold;
            if (shouldHide == _modelHidden)
                return;

            _modelHidden = shouldHide;
            for (int i = 0; i < _modelRenderers.Length; i++)
            {
                if (_modelRenderers[i] != null)
                    _modelRenderers[i].enabled = !_modelHidden && !_migrationPresentationHidden;
            }
        }

        private void ApplyMigrationPresentationVisibility()
        {
            if (_modelRenderers == null) return;

            for (int i = 0; i < _modelRenderers.Length; i++)
            {
                if (_modelRenderers[i] != null)
                    _modelRenderers[i].enabled = !_modelHidden && !_migrationPresentationHidden;
            }
        }

        private void OnNetworkedNameChanged(FixedString32Bytes previous, FixedString32Bytes current)
        {
            string playerName = current.ToString();
            ApplyNetworkedName(playerName);
            RegisterWithUGSUserList(playerName);
        }

        private void ApplyNetworkedName(string playerName)
        {
            if (_nameTag != null && !string.IsNullOrEmpty(playerName))
                _nameTag.SetName(playerName);
        }

        private void RegisterWithUGSUserList(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return;

            // The roster is local/event-derived. Only the locally owned player may be
            // compared with this browser's authenticated Supabase identity. A remote
            // proxy must never inherit or expose the viewing user's account identity.
            string localUserId = IsOwner ? GetSupabaseUserId() : string.Empty;
            MetaDynUGSUserListManager.Instance?.RegisterPlayer(OwnerClientId, playerName, localUserId);
        }

        public void RefreshUserListRegistration()
        {
            RegisterWithUGSUserList(_networkedName.Value.ToString());
        }

        public void PublishCustomAvatarModel(string avatarModelId)
        {
            if (!IsSpawned || !IsOwner || string.IsNullOrWhiteSpace(avatarModelId))
                return;

            if (avatarModelId.Length > 63)
                avatarModelId = avatarModelId.Substring(0, 63);

            _networkedAvatarModelId.Value = avatarModelId;
            Debug.Log($"[MetaDyn UGS Player] Published custom avatar model '{avatarModelId}' for client {OwnerClientId}.");
        }

        private void OnNetworkedAvatarModelChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            ApplyNetworkedAvatarModel(current.ToString());
        }

        private void ApplyNetworkedAvatarModel(string avatarModelId)
        {
            if (IsOwner || string.IsNullOrWhiteSpace(avatarModelId))
                return;

            GLBAvatarLoader loader = GLBAvatarLoader.Instance != null
                ? GLBAvatarLoader.Instance
                : FindFirstObjectByType<GLBAvatarLoader>();

            if (loader == null)
            {
                Debug.LogWarning($"[MetaDyn UGS Player] Cannot load remote avatar model '{avatarModelId}': GLBAvatarLoader not found.");
                return;
            }

            loader.LoadAndApplyStoredAvatar(avatarModelId, this);
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (!IsOwner || animationEvent.animatorClipInfo.weight < 0.5f)
                return;

            if (footstepAudioClips == null || footstepAudioClips.Length == 0)
                return;

            if (Time.time - _lastFootstepTime < FootstepCooldown)
                return;

            _lastFootstepTime = Time.time;

            int index = Random.Range(0, footstepAudioClips.Length);
            AudioSource.PlayClipAtPoint(footstepAudioClips[index], transform.position, footstepAudioVolume);
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            // Landing audio is driven by controller ground-state transitions to avoid duplicate animation-event firing.
        }

        private void PlayLandingSound()
        {
            if (landingAudioClip == null || Time.time - _lastLandTime < LandCooldown)
                return;

            _lastLandTime = Time.time;
            AudioSource.PlayClipAtPoint(landingAudioClip, transform.position, footstepAudioVolume);
        }
    }
}
