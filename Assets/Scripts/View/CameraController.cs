using UnityEngine;
using UnityEngine.InputSystem;
using ProjectGuild.View.Runners;

namespace ProjectGuild.View
{
    /// <summary>
    /// Camera that follows a target (runner) with orbit and zoom controls.
    ///
    /// Controls:
    /// - Right mouse drag: orbit around target
    /// - Scroll wheel: zoom in/out
    ///
    /// Call SetTarget() to snap to a different runner.
    /// Uses the New Input System (InputAction inline definitions).
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

        [Header("Zoom")]
        [SerializeField] private float _zoomSpeed = 3f;
        [SerializeField] private float _minDistance = 5f;
        [SerializeField] private float _maxDistance = 50f;

        private Transform _target;
        private float _currentYaw;
        private float _currentPitch = 35f;
        private float _currentDistance = 15f;

        // Input actions
        private InputAction _lookAction;
        private InputAction _orbitButtonAction;
        private InputAction _zoomAction;

        public void SetTarget(Transform target)
        {
            _target = target;
            SnapToTarget();
        }

        public void SetTarget(RunnerVisual runner)
        {
            _target = runner?.transform;
            SnapToTarget();
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

        private void Awake()
        {
            _lookAction = new InputAction("Look", InputActionType.Value,
                binding: "<Pointer>/delta");

            _orbitButtonAction = new InputAction("OrbitButton", InputActionType.Button,
                binding: "<Mouse>/rightButton");

            _zoomAction = new InputAction("Zoom", InputActionType.Value,
                binding: "<Mouse>/scroll/y");
        }

        private void OnEnable()
        {
            _lookAction.Enable();
            _orbitButtonAction.Enable();
            _zoomAction.Enable();
        }

        private void OnDisable()
        {
            _lookAction.Disable();
            _orbitButtonAction.Disable();
            _zoomAction.Disable();
        }

        private void Start()
        {
            _currentDistance = _defaultOffset.magnitude;
            _currentPitch = 35f;
            _currentYaw = 0f;
        }

        private void LateUpdate()
        {
            // Orbit with right mouse button
            if (_orbitButtonAction.IsPressed())
            {
                Vector2 lookDelta = _lookAction.ReadValue<Vector2>();
                _currentYaw += lookDelta.x * _orbitSpeed * 0.1f;
                _currentPitch -= lookDelta.y * _orbitSpeed * 0.1f;
                _currentPitch = Mathf.Clamp(_currentPitch, _minPitch, _maxPitch);
            }

            // Zoom with scroll wheel
            float scrollValue = _zoomAction.ReadValue<float>();
            if (scrollValue != 0f)
            {
                // Scroll values from New Input System are larger than old GetAxis,
                // normalize by dividing by 120 (standard scroll tick delta)
                float normalizedScroll = scrollValue / 120f;
                _currentDistance -= normalizedScroll * _zoomSpeed * _currentDistance * 0.1f;
                _currentDistance = Mathf.Clamp(_currentDistance, _minDistance, _maxDistance);
            }

            // Calculate camera position from orbit angles
            Vector3 targetPos = _target != null ? _target.position : Vector3.zero;

            Quaternion rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);
            Vector3 desiredPosition = targetPos + offset;

            // Smooth position follow, instant rotation (feels crisp)
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * _followSpeed);
            transform.LookAt(targetPos);
        }
    }
}
