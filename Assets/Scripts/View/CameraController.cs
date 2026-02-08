using UnityEngine;
using ProjectGuild.View.Runners;

namespace ProjectGuild.View
{
    /// <summary>
    /// Camera that follows a target (runner) with orbit and zoom controls.
    ///
    /// Controls:
    /// - Right mouse drag: orbit around target
    /// - Scroll wheel: zoom in/out
    /// - Middle mouse drag: pan offset
    ///
    /// Call SetTarget() to snap to a different runner.
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

        public void SetTarget(Transform target)
        {
            _target = target;
        }

        public void SetTarget(RunnerVisual runner)
        {
            _target = runner?.transform;
        }

        private void Start()
        {
            // Initialize orbit angles from default offset
            _currentDistance = _defaultOffset.magnitude;
            _currentPitch = 35f;
            _currentYaw = 0f;
        }

        private void LateUpdate()
        {
            // Orbit with right mouse button
            if (Input.GetMouseButton(1))
            {
                _currentYaw += Input.GetAxis("Mouse X") * _orbitSpeed;
                _currentPitch -= Input.GetAxis("Mouse Y") * _orbitSpeed;
                _currentPitch = Mathf.Clamp(_currentPitch, _minPitch, _maxPitch);
            }

            // Zoom with scroll wheel
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
            {
                _currentDistance -= scroll * _zoomSpeed * _currentDistance;
                _currentDistance = Mathf.Clamp(_currentDistance, _minDistance, _maxDistance);
            }

            // Calculate camera position from orbit angles
            Vector3 targetPos = _target != null ? _target.position : Vector3.zero;

            Quaternion rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);
            Vector3 desiredPosition = targetPos + offset;

            // Smooth follow
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * _followSpeed);
            transform.LookAt(targetPos);
        }
    }
}
