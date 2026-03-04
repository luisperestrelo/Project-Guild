using UnityEngine;
using UnityEngine.InputSystem;
using ProjectGuild.View.Runners;
using ProjectGuild.View.UI;

namespace ProjectGuild.View
{
    /// <summary>
    /// Camera that follows a target (runner) with orbit and zoom controls.
    /// Also supports a "hub scene mode" where the camera targets a fixed position
    /// inside the Guild Hall scene (activated by the Guild Hall button or H hotkey).
    ///
    /// Scene transitions (camera jumping 500+ meters between scene zones) trigger a
    /// fade-to-black via SceneTransitionOverlay. Same-zone target changes snap directly.
    ///
    /// Controls:
    /// - Right mouse drag: orbit around target
    /// - Scroll wheel: zoom in/out
    /// - H: jump camera to Guild Hall (hub scene mode)
    ///
    /// Call SetTarget() to snap to a different runner (exits hub mode).
    /// Call EnterHubSceneMode() to view the Guild Hall without a runner.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Follow Settings")]
        [SerializeField] private float _followSpeed = 8f;
        [SerializeField] private Vector3 _defaultOffset = new(0f, 12f, -10f);

        [Header("Orbit")]
        [SerializeField] private float _orbitSpeed = 3f;
        [SerializeField] private float _minPitch = 10f;
        [SerializeField] private float _maxPitch = 80f;

        [Header("UI")]
        [SerializeField] private UIManager _uiManager;

        [Header("Zoom")]
        [SerializeField] private float _zoomSpeed = 3f;
        [SerializeField] private float _minDistance = 5f;
        [SerializeField] private float _maxDistance = 50f;

        [Header("Hub Scene")]
        [SerializeField] private WorldSceneManager _worldSceneManager;

        [Header("Scene Transitions")]
        [Tooltip("Fade overlay for scene transitions. If null, camera snaps without fade.")]
        [SerializeField] private SceneTransitionOverlay _sceneTransitionOverlay;

        [Tooltip("World-space distance threshold for triggering a fade transition. " +
            "Below 2000m scene spacing, well above any same-zone movement.")]
        [SerializeField] private float _fadeDistanceThreshold = 500f;

        private Transform _target;
        private float _currentYaw;
        private float _currentPitch = 35f;
        private float _currentDistance = 15f;

        // Hub scene mode: camera orbits a fixed point instead of following a runner
        private bool _hubSceneMode;
        private Vector3 _hubSceneTarget;

        /// <summary>
        /// True when the camera is in hub scene mode (viewing Guild Hall, not following a runner).
        /// </summary>
        public bool IsInHubSceneMode => _hubSceneMode;

        /// <summary>
        /// The current follow target transform (null when in hub scene mode or no target set).
        /// Used by VisualSyncSystem to determine if a departing/arriving runner is being watched.
        /// </summary>
        public Transform CurrentTarget => _target;

        // Input actions
        private InputAction _lookAction;
        private InputAction _orbitButtonAction;
        private InputAction _zoomAction;
        private InputAction _hubHotkeyAction;

        public void SetTarget(Transform target)
        {
            var oldTarget = _target;
            _target = target;
            ExitHubSceneMode();
            ExecuteTargetSnap(oldTarget);
        }

        public void SetTarget(RunnerVisual runner)
        {
            var oldTarget = _target;
            _target = runner?.transform;
            ExitHubSceneMode();
            ExecuteTargetSnap(oldTarget);
        }

        /// <summary>
        /// Enter hub scene mode: camera targets a fixed position inside the Guild Hall scene.
        /// The hub node ID is used to look up the scene offset from WorldSceneManager.
        /// Exits automatically when SetTarget() is called (runner selection).
        /// </summary>
        public void EnterHubSceneMode(string hubNodeId)
        {
            if (_worldSceneManager == null) return;

            // Ensure the hub scene is loaded (it should be — hub is exempt from auto-unload)
            _worldSceneManager.EnsureNodeSceneLoaded(hubNodeId);

            // Remember where camera was before switching
            Vector3 previousCameraPos = transform.position;

            // Target the scene's focal point (center of interest)
            var sceneRoot = _worldSceneManager.GetNodeSceneRoot(hubNodeId);
            if (sceneRoot != null)
            {
                _hubSceneTarget = sceneRoot.SceneFocalPosition + new Vector3(0f, 1f, 0f);
            }
            else
            {
                // Scene not ready yet — use the scene offset as fallback
                _hubSceneTarget = _worldSceneManager.GetNodeSceneOffset(hubNodeId) + new Vector3(0f, 1f, 0f);
            }

            _hubSceneMode = true;
            _target = null;

            // Fade if jumping a large distance (different scene zone)
            if (ShouldFade(previousCameraPos, _hubSceneTarget))
            {
                PlayFade(SnapToHubTarget);
            }
            else
            {
                SnapToHubTarget();
            }
        }

        private void ExitHubSceneMode()
        {
            _hubSceneMode = false;
        }

        /// <summary>
        /// Snap or fade to the new target, depending on distance from old target.
        /// </summary>
        private void ExecuteTargetSnap(Transform oldTarget)
        {
            if (_target == null) return;

            Vector3 oldPos = oldTarget != null ? oldTarget.position : transform.position;
            Vector3 newPos = _target.position;

            if (ShouldFade(oldPos, newPos))
            {
                PlayFade(SnapToTarget);
            }
            else
            {
                SnapToTarget();
            }
        }

        /// <summary>
        /// Returns true if the camera should fade between two world positions.
        /// Triggers when distance exceeds threshold (different scene zones are 2000+ m apart).
        /// </summary>
        private bool ShouldFade(Vector3 from, Vector3 to)
        {
            if (_sceneTransitionOverlay == null) return false;
            return Vector3.Distance(from, to) > _fadeDistanceThreshold;
        }

        /// <summary>
        /// Wrap a camera snap action in a fade-to-black transition.
        /// </summary>
        private void PlayFade(System.Action snapAction)
        {
            if (_sceneTransitionOverlay != null)
            {
                _sceneTransitionOverlay.PlayFadeTransition(snapAction);
            }
            else
            {
                snapAction();
            }
        }

        /// <summary>
        /// Instantly teleport camera to the correct orbit position around the current target.
        /// Preserves current yaw, pitch, and distance — just changes what we're looking at.
        /// </summary>
        private void SnapToTarget()
        {
            if (_target == null) return;

            Vector3 targetPos = _target.position;
            Quaternion rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);
            transform.position = targetPos + offset;
            transform.LookAt(targetPos);
        }

        private void SnapToHubTarget()
        {
            Quaternion rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);
            transform.position = _hubSceneTarget + offset;
            transform.LookAt(_hubSceneTarget);
        }

        private void Awake()
        {
            if (_uiManager == null)
                _uiManager = FindAnyObjectByType<UIManager>();
            if (_worldSceneManager == null)
                _worldSceneManager = FindAnyObjectByType<WorldSceneManager>();
            if (_sceneTransitionOverlay == null)
                _sceneTransitionOverlay = FindAnyObjectByType<SceneTransitionOverlay>();

            _lookAction = new InputAction("Look", InputActionType.Value,
                binding: "<Pointer>/delta");

            _orbitButtonAction = new InputAction("OrbitButton", InputActionType.Button,
                binding: "<Mouse>/rightButton");

            _zoomAction = new InputAction("Zoom", InputActionType.Value,
                binding: "<Mouse>/scroll/y");

            _hubHotkeyAction = new InputAction("HubHotkey", InputActionType.Button,
                binding: "<Keyboard>/h");
        }

        private void OnEnable()
        {
            _lookAction.Enable();
            _orbitButtonAction.Enable();
            _zoomAction.Enable();
            _hubHotkeyAction.Enable();
        }

        private void OnDisable()
        {
            _lookAction.Disable();
            _orbitButtonAction.Disable();
            _zoomAction.Disable();
            _hubHotkeyAction.Disable();
        }

        private void Start()
        {
            _currentDistance = _defaultOffset.magnitude;
            _currentPitch = 35f;
            _currentYaw = 0f;
        }

        private void LateUpdate()
        {
            bool uiBlocking = _uiManager != null && _uiManager.IsPointerOverUI();

            // H hotkey: jump to Guild Hall (suppress when typing in a text field)
            if (_hubHotkeyAction.WasPressedThisFrame()
                && (_uiManager == null || !_uiManager.IsTextFieldFocused()))
            {
                _uiManager?.JumpToGuildHall();
            }

            // Orbit with right mouse button (skip when pointer is over UI)
            if (!uiBlocking && _orbitButtonAction.IsPressed())
            {
                Vector2 lookDelta = _lookAction.ReadValue<Vector2>();
                _currentYaw += lookDelta.x * _orbitSpeed * 0.1f;
                _currentPitch -= lookDelta.y * _orbitSpeed * 0.1f;
                _currentPitch = Mathf.Clamp(_currentPitch, _minPitch, _maxPitch);
            }

            // Zoom with scroll wheel (skip when pointer is over UI)
            if (!uiBlocking)
            {
                float scrollValue = _zoomAction.ReadValue<float>();
                if (scrollValue != 0f)
                {
                    // Scroll values from New Input System are larger than old GetAxis,
                    // normalize by dividing by 120 (standard scroll tick delta)
                    float normalizedScroll = scrollValue / 120f;
                    _currentDistance -= normalizedScroll * _zoomSpeed * _currentDistance * 0.1f;
                    _currentDistance = Mathf.Clamp(_currentDistance, _minDistance, _maxDistance);
                }
            }

            // Calculate camera position from orbit angles
            Vector3 targetPos;
            if (_hubSceneMode)
                targetPos = _hubSceneTarget;
            else
                targetPos = _target != null ? _target.position : Vector3.zero;

            Quaternion rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);
            Vector3 desiredPosition = targetPos + offset;

            // Snap when target has teleported (scene transition), smooth follow otherwise
            if (Vector3.Distance(transform.position, desiredPosition) > _fadeDistanceThreshold)
                transform.position = desiredPosition;
            else
                transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * _followSpeed);

            transform.LookAt(targetPos);
        }
    }
}
