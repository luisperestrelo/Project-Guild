using UnityEngine;

namespace ProjectGuild.View.Runners
{
    /// <summary>
    /// Visual representation of a runner in the 3D world. Managed by VisualSyncSystem —
    /// don't create these manually. The sync system spawns one per runner and updates
    /// its position each frame by interpolating between the simulation's tick positions.
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

        // Interpolation targets — set by VisualSyncSystem each tick
        private Vector3 _previousPosition;
        private Vector3 _targetPosition;
        private float _interpolationT;

        // Name label
        private TextMesh _nameLabel;

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
            _nameLabel = labelObj.AddComponent<TextMesh>();
            _nameLabel.text = runnerName;
            _nameLabel.characterSize = 0.15f;
            _nameLabel.anchor = TextAnchor.MiddleCenter;
            _nameLabel.alignment = TextAlignment.Center;
            _nameLabel.fontSize = 48;
            _nameLabel.color = Color.white;
        }

        /// <summary>
        /// Called by VisualSyncSystem when the simulation ticks and the runner's
        /// position has changed. We store the current position as "previous" and
        /// set the new target, then interpolate between them during Update().
        /// This gives smooth movement even though the simulation ticks at 10/sec.
        /// </summary>
        public void SetTargetPosition(Vector3 newTarget)
        {
            _previousPosition = transform.position;
            _targetPosition = newTarget;
            _interpolationT = 0f;
        }

        /// <summary>
        /// Snap immediately to a position (no interpolation). Used for teleports,
        /// initial placement, and when a runner arrives at a node.
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
                // Interpolation speed: complete in one tick interval (0.1 sec at 10 ticks/sec)
                _interpolationT += Time.deltaTime * 10f;
                if (_interpolationT > 1f) _interpolationT = 1f;

                transform.position = Vector3.Lerp(_previousPosition, _targetPosition, _interpolationT);

                // Face movement direction
                Vector3 direction = _targetPosition - _previousPosition;
                if (direction.sqrMagnitude > 0.001f)
                {
                    direction.y = 0f;
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }

            // Make name label face camera
            if (_nameLabel != null && Camera.main != null)
            {
                _nameLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }
    }
}
