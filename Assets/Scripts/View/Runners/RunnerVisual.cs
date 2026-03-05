using TMPro;
using UnityEngine;

namespace ProjectGuild.View.Runners
{
    /// <summary>
    /// Visual representation of a runner in the 3D world. Managed by VisualSyncSystem —
    /// don't create these manually. The sync system spawns one per runner and updates
    /// its position each frame by interpolating between the simulation's tick positions.
    ///
    /// Four movement modes (caller decides which to use):
    /// - SetTargetPosition: tick interpolation (0.1s) for sim-driven travel
    /// - WalkToPosition: visible walk at WalkSpeed for in-node movement (straight line)
    /// - WalkAlongPath: visible walk through NavMesh waypoints (avoids obstacles)
    /// - SnapToPosition: instant teleport for scene transitions and initial placement
    ///
    /// Supports Synty character prefabs with Animator. When an Animator is present,
    /// drives a "Speed" float parameter (0=idle, 1=moving) for locomotion blending.
    /// Root motion is disabled — movement is fully code-driven.
    /// </summary>
    public class RunnerVisual : MonoBehaviour
    {
        /// <summary>
        /// The simulation runner ID this visual represents.
        /// </summary>
        public string RunnerId { get; private set; }
        public string RunnerName { get; private set; }

        // Interpolation state (current segment)
        private Vector3 _previousPosition;
        private Vector3 _targetPosition;
        private float _interpolationT;
        private float _interpolationSpeed = 10f; // 1/duration — default completes in 0.1s (one tick)

        // Multi-waypoint path (NavMesh walks)
        private Vector3[] _pathWaypoints;
        private int _pathIndex; // index of waypoint we're currently walking TOWARD

        // Walk speed for within-node movement (gathering spot changes, repositioning, etc.)
        // Settable so VisualSyncSystem can apply athletics-based in-node speed per runner.
        private float _walkSpeed = 8f;
        public float WalkSpeed { get => _walkSpeed; set => _walkSpeed = value; }

        // Animation
        private Animator _animator;
        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        // Ground snap: Y offset from ground surface to transform.position.
        // Capsule pivot is at center (1m), Synty character pivot is at feet (0m).
        private float _groundYOffset;

        // Name label
        private TextMeshPro _nameLabel;
        private const float NameLabelHeightPrefab = 2.5f;
        private const float NameLabelHeightCapsule = 2.2f;

        public void Initialize(string runnerId, string runnerName, Vector3 startPosition)
        {
            RunnerId = runnerId;
            RunnerName = runnerName;
            transform.position = startPosition;
            _previousPosition = startPosition;
            _targetPosition = startPosition;
            _interpolationT = 1f;
            _pathWaypoints = null;
            _lastFramePosition = startPosition;
            gameObject.name = $"Runner_{runnerName}";

            // Detect and configure Animator (Synty character prefabs)
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _animator.applyRootMotion = false;
                _groundYOffset = 0f; // Synty characters have pivot at feet
            }
            else
            {
                _groundYOffset = 1f; // Capsule pivot is at center, half-height = 1m
            }

            // Create a floating name label above the character
            bool hasPrefabModel = _animator != null;
            float labelHeight = hasPrefabModel ? NameLabelHeightPrefab : NameLabelHeightCapsule;
            var labelObj = new GameObject("NameLabel");
            labelObj.transform.SetParent(transform);
            labelObj.transform.localPosition = new Vector3(0f, labelHeight, 0f);
            _nameLabel = labelObj.AddComponent<TextMeshPro>();
            _nameLabel.text = runnerName;
            _nameLabel.fontSize = 4f;
            _nameLabel.alignment = TextAlignmentOptions.Center;
            _nameLabel.color = Color.white;
            _nameLabel.rectTransform.sizeDelta = new Vector2(6f, 2f);
        }

        /// <summary>
        /// Tick interpolation for sim-driven travel. Completes in one tick interval (0.1s).
        /// Called every frame by VisualSyncSystem — only resets when target actually changes.
        /// </summary>
        public void SetTargetPosition(Vector3 newTarget)
        {
            if ((_targetPosition - newTarget).sqrMagnitude < 0.0001f) return;

            _pathWaypoints = null;
            _previousPosition = transform.position;
            _targetPosition = newTarget;
            _interpolationT = 0f;
            _interpolationSpeed = 10f; // 0.1s = one tick interval
        }

        /// <summary>
        /// Visible walk to a position at WalkSpeed (straight line).
        /// Used when no NavMesh path is available.
        /// </summary>
        public void WalkToPosition(Vector3 target)
        {
            if ((target - FinalDestination).sqrMagnitude < 0.0001f) return;

            _pathWaypoints = null;
            _previousPosition = transform.position;
            _targetPosition = target;
            _interpolationT = 0f;

            float distance = (_targetPosition - _previousPosition).magnitude;
            _interpolationSpeed = distance > 0.01f ? WalkSpeed / distance : 10f;
        }

        /// <summary>
        /// Walk along a multi-waypoint path at WalkSpeed (NavMesh-computed).
        /// Walks through each waypoint in sequence, avoiding obstacles.
        /// </summary>
        public void WalkAlongPath(Vector3[] waypoints)
        {
            if (waypoints == null || waypoints.Length == 0) return;

            // Skip if already walking to the same final destination
            if ((waypoints[^1] - FinalDestination).sqrMagnitude < 0.0001f) return;

            _pathWaypoints = waypoints;
            _pathIndex = 0;
            _previousPosition = transform.position;
            _targetPosition = waypoints[0];
            _interpolationT = 0f;

            float distance = (_targetPosition - _previousPosition).magnitude;
            _interpolationSpeed = distance > 0.01f ? WalkSpeed / distance : 10f;
        }

        /// <summary>
        /// Snap immediately to a position (no interpolation). Used for scene transitions,
        /// initial placement, and cross-scene jumps.
        /// </summary>
        public void SnapToPosition(Vector3 position)
        {
            _pathWaypoints = null;
            transform.position = position;
            _previousPosition = position;
            _targetPosition = position;
            _interpolationT = 1f;
        }

        /// <summary>
        /// The final destination of the current movement (last waypoint if on a path,
        /// or the single target position).
        /// </summary>
        private Vector3 FinalDestination =>
            _pathWaypoints != null && _pathWaypoints.Length > 0
                ? _pathWaypoints[^1]
                : _targetPosition;

        // Track position for velocity-based animation
        private Vector3 _lastFramePosition;

        private void Update()
        {
            if (_interpolationT < 1f)
            {
                _interpolationT += Time.deltaTime * _interpolationSpeed;
                if (_interpolationT > 1f) _interpolationT = 1f;

                Vector3 lerpedPos = Vector3.Lerp(_previousPosition, _targetPosition, _interpolationT);
                transform.position = lerpedPos;

                // Face movement direction
                Vector3 direction = _targetPosition - _previousPosition;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }

            // Advance to next waypoint when current segment is complete
            if (_interpolationT >= 1f && _pathWaypoints != null)
            {
                _pathIndex++;
                if (_pathIndex < _pathWaypoints.Length)
                {
                    _previousPosition = _targetPosition;
                    _targetPosition = _pathWaypoints[_pathIndex];
                    _interpolationT = 0f;

                    float distance = (_targetPosition - _previousPosition).magnitude;
                    _interpolationSpeed = distance > 0.01f ? WalkSpeed / distance : 10f;
                }
                else
                {
                    // Path complete
                    _pathWaypoints = null;
                }
            }

            // Snap to ground every frame (not just during interpolation)
            transform.position = SnapToGround(transform.position, _groundYOffset);

            // Drive animation from actual velocity (avoids stop-go flicker between ticks)
            if (_animator != null)
            {
                Vector3 delta = transform.position - _lastFramePosition;
                delta.y = 0f;
                float frameSpeed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
                _animator.SetFloat(SpeedParam, frameSpeed > 0.1f ? 1f : 0f);
            }
            _lastFramePosition = transform.position;

            // Billboard name label — Y-axis only so it stays upright from any angle
            if (_nameLabel != null && Camera.main != null)
            {
                var camForward = Camera.main.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.001f)
                    _nameLabel.transform.rotation = Quaternion.LookRotation(camForward);
            }
        }

        /// <summary>
        /// Raycast down from above the position to stick the runner to the ground surface.
        /// Works with terrain, mesh colliders, any physics surface.
        /// Falls back to the original position if nothing is hit (flat plane, no collider).
        /// </summary>
        private static int _groundLayerMask = -1;

        /// <summary>
        /// Raycast down to find the ground surface, then place the runner at
        /// groundY + yOffset. The yOffset preserves the hovering offset that
        /// VisualSyncSystem bakes into target positions (RunnerYOffset = 1m
        /// for capsules so the pivot sits above ground, 0 for character models).
        /// </summary>
        private static Vector3 SnapToGround(Vector3 pos, float yOffset)
        {
            // Cache layer mask excluding the Runners layer
            if (_groundLayerMask == -1)
            {
                int runnersLayer = LayerMask.NameToLayer("Runners");
                _groundLayerMask = runnersLayer >= 0 ? ~(1 << runnersLayer) : ~0;
            }

            Vector3 origin = new Vector3(pos.x, pos.y + 20f, pos.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 40f, _groundLayerMask))
            {
                return new Vector3(pos.x, hit.point.y + yOffset, pos.z);
            }
            return pos;
        }
    }
}
