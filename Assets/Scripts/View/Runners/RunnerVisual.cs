using TMPro;
using UnityEngine;

namespace ProjectGuild.View.Runners
{
    /// <summary>
    /// Visual representation of a runner in the 3D world. Managed by VisualSyncSystem —
    /// don't create these manually. The sync system spawns one per runner and updates
    /// its position each frame by interpolating between the simulation's tick positions.
    ///
    /// Three movement modes (caller decides which to use):
    /// - SetTargetPosition: tick interpolation (0.1s) for sim-driven travel
    /// - WalkToPosition: visible walk at WalkSpeed for in-node movement (spot changes, repositioning)
    /// - SnapToPosition: instant teleport for scene transitions and initial placement
    ///
    /// For now this is a placeholder capsule. Will be replaced with actual character
    /// models and animations later.
    /// </summary>
    public class RunnerVisual : MonoBehaviour
    {
        /// <summary>
        /// The simulation runner ID this visual represents.
        /// </summary>
        public string RunnerId { get; private set; }
        public string RunnerName { get; private set; }

        // Interpolation state
        private Vector3 _previousPosition;
        private Vector3 _targetPosition;
        private float _interpolationT;
        private float _interpolationSpeed = 10f; // 1/duration — default completes in 0.1s (one tick)

        // Walk speed for within-node movement (gathering spot changes, repositioning, etc.)
        private const float WalkSpeed = 8f; // meters per second

        // Name label
        private TextMeshPro _nameLabel;

        public void Initialize(string runnerId, string runnerName, Vector3 startPosition)
        {
            RunnerId = runnerId;
            RunnerName = runnerName;
            transform.position = startPosition;
            _previousPosition = startPosition;
            _targetPosition = startPosition;
            _interpolationT = 1f;
            gameObject.name = $"Runner_{runnerName}";

            // Create a floating name label above the capsule
            var labelObj = new GameObject("NameLabel");
            labelObj.transform.SetParent(transform);
            labelObj.transform.localPosition = new Vector3(0f, 2.2f, 0f);
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

            _previousPosition = transform.position;
            _targetPosition = newTarget;
            _interpolationT = 0f;
            _interpolationSpeed = 10f; // 0.1s = one tick interval
        }

        /// <summary>
        /// Visible walk to a position at WalkSpeed. Used for in-node movement:
        /// gathering spot changes, moving to crafting stations, combat repositioning, etc.
        /// Purely visual — the sim doesn't wait for this to complete.
        /// </summary>
        public void WalkToPosition(Vector3 target)
        {
            if ((target - _targetPosition).sqrMagnitude < 0.0001f) return;

            _previousPosition = transform.position;
            _targetPosition = target;
            _interpolationT = 0f;

            float distance = (_targetPosition - _previousPosition).magnitude;
            float walkDuration = distance > 0.01f ? distance / WalkSpeed : 0.1f;
            _interpolationSpeed = 1f / walkDuration;
        }

        /// <summary>
        /// Snap immediately to a position (no interpolation). Used for scene transitions,
        /// initial placement, and cross-scene jumps.
        /// </summary>
        public void SnapToPosition(Vector3 position)
        {
            transform.position = position;
            _previousPosition = position;
            _targetPosition = position;
            _interpolationT = 1f;
        }

        private void Update()
        {
            if (_interpolationT < 1f)
            {
                _interpolationT += Time.deltaTime * _interpolationSpeed;
                if (_interpolationT > 1f) _interpolationT = 1f;

                transform.position = Vector3.Lerp(_previousPosition, _targetPosition, _interpolationT);

                // Face movement direction
                Vector3 direction = _targetPosition - _previousPosition;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }

            // Billboard name label — Y-axis only so it stays upright from any angle
            if (_nameLabel != null && Camera.main != null)
            {
                var camForward = Camera.main.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.001f)
                    _nameLabel.transform.rotation = Quaternion.LookRotation(camForward);
            }
        }
    }
}
