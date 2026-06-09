using MetaDyn;
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

        private readonly NetworkVariable<FixedString32Bytes> _networkedName = new();
        
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
        private float _airborneTime;
        private float _lastVerticalVelocity;
        private float _lastFootstepTime;
        private float _lastLandTime;

        private const float FootstepCooldown = 0.18f;
private const float LandCooldown = 0.35f;

        public string DisplayName => _networkedName.Value.ToString();
        public bool IsEmoting { get; private set; }
        public Animator Animator => animator;

        public static event System.Action OnLocalPlayerReady;

        private int _animIDSpeed;
private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;

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

            if (playerInput != null)
                playerInput.enabled = IsOwner;

            if (IsOwner)
            {
                SubmitNameServerRpc(PlayerPrefs.GetString("PlayerName", "Guest " + Random.Range(100, 999)));
                BindCamera();
                OnLocalPlayerReady?.Invoke();
            }

            ApplyNetworkedName(_networkedName.Value.ToString());
            RegisterWithUGSUserList(_networkedName.Value.ToString());
        }

        public override void OnNetworkDespawn()
        {
            _networkedName.OnValueChanged -= OnNetworkedNameChanged;
            MetaDynUGSUserListManager.Instance?.UnregisterPlayer(OwnerClientId);
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
            PlayEmoteServerRpc(index);
        }

        [ServerRpc]
        private void PlayEmoteServerRpc(int index)
        {
            PlayEmoteClientRpc(index);
        }

        [ClientRpc]
        private void PlayEmoteClientRpc(int index)
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

        [ServerRpc]
        private void SubmitNameServerRpc(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                playerName = "Guest " + Random.Range(100, 999);

            if (playerName.Length > 31)
                playerName = playerName.Substring(0, 31);

            _networkedName.Value = playerName;
            MetaDynUGSUserListManager.Instance?.RegisterPlayer(OwnerClientId, playerName, GetSupabaseUserId());
            Debug.Log($"[MetaDyn SPAWN] Name set for client {OwnerClientId}: {playerName}");
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
                    _modelRenderers[i].enabled = !_modelHidden;
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

            MetaDynUGSUserListManager.Instance?.RegisterPlayer(OwnerClientId, playerName, GetSupabaseUserId());
        }

        public void RefreshUserListRegistration()
        {
            RegisterWithUGSUserList(_networkedName.Value.ToString());
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
