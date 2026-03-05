using UnityEngine;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;
using ProjectGuild.View.Runners;

namespace ProjectGuild.View
{
    /// <summary>
    /// View-layer implementation of <see cref="INodeGeometryProvider"/>.
    /// Uses loaded node scenes and runner visual positions to provide distances
    /// for sim-driven in-node transit phases (exit, gathering spot, deposit point).
    ///
    /// Returns null when scene not loaded or runner visual not found — sim treats
    /// as 0 (instant transit), which is correct since there's no scene to animate.
    ///
    /// Same wiring pattern as NavMeshTravelPathCache: MonoBehaviour found by
    /// GameBootstrapper and assigned to GameSimulation.NodeGeometryProvider.
    /// </summary>
    public class NodeGeometryProvider : MonoBehaviour, INodeGeometryProvider
    {
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private WorldSceneManager _worldSceneManager;
        [SerializeField] private VisualSyncSystem _visualSyncSystem;

        private void OnEnable()
        {
            if (_simulationRunner == null)
                _simulationRunner = FindAnyObjectByType<SimulationRunner>();
            if (_worldSceneManager == null)
                _worldSceneManager = FindAnyObjectByType<WorldSceneManager>();
            if (_visualSyncSystem == null)
                _visualSyncSystem = FindAnyObjectByType<VisualSyncSystem>();
        }

        public float? GetExitDistance(string runnerId, string nodeId, string destinationNodeId)
        {
            var runnerPos = GetRunnerPositionInScene(runnerId, nodeId, out var sceneRoot);
            if (!runnerPos.HasValue) return null;

            Vector3 departureDir = ComputeDepartureDirection(nodeId, destinationNodeId);
            Vector3 exitPoint = sceneRoot.GetExitPosition(departureDir);

            return DistanceOrZero(runnerPos.Value, exitPoint);
        }

        public float? GetGatheringSpotDistance(string runnerId, string nodeId, int gatherableIndex, int spotIndex)
        {
            var runnerPos = GetRunnerPositionInScene(runnerId, nodeId, out var sceneRoot);
            if (!runnerPos.HasValue) return null;

            Vector3 gatheringSpot = sceneRoot.GetGatheringPosition(gatherableIndex, spotIndex);

            return DistanceOrZero(runnerPos.Value, gatheringSpot);
        }

        public float? GetDepositPointDistance(string runnerId, string nodeId)
        {
            var runnerPos = GetRunnerPositionInScene(runnerId, nodeId, out var sceneRoot);
            if (!runnerPos.HasValue) return null;

            if (!sceneRoot.DepositPointPosition.HasValue)
                return null;

            return DistanceOrZero(runnerPos.Value, sceneRoot.DepositPointPosition.Value);
        }

        // ─── Helpers ──────────────────────────────────────────────

        /// <summary>
        /// Max distance a runner visual can be from the scene root and still be
        /// considered "positioned in the scene." Node scenes are compact — 50m is generous.
        /// If the visual is farther than this, it hasn't been moved into the scene yet
        /// (still at overworld coords, thousands of meters away due to scene offsets).
        /// </summary>
        private const float MaxVisualDistanceFromScene = 50f;

        /// <summary>
        /// Get the runner's effective position for distance calculations.
        /// If the visual is already inside the node scene, uses the visual's actual position.
        /// If the visual is stale (hasn't been moved into the scene yet — happens on the
        /// same tick as arrival), falls back to the scene's spawn/arrival position, which is
        /// where VisualSyncSystem will place the visual on the next frame.
        /// Returns null if scene/visual not available.
        /// </summary>
        private Vector3? GetRunnerPositionInScene(string runnerId, string nodeId, out NodeSceneRoot sceneRoot)
        {
            sceneRoot = null;

            if (_worldSceneManager == null || !_worldSceneManager.IsNodeSceneReady(nodeId))
                return null;

            sceneRoot = _worldSceneManager.GetNodeSceneRoot(nodeId);
            if (sceneRoot == null) return null;

            if (_visualSyncSystem == null) return null;
            var visual = _visualSyncSystem.GetRunnerVisual(runnerId);
            if (visual == null) return null;

            // Check if the visual is actually inside the scene
            float distToScene = Vector3.Distance(visual.transform.position, sceneRoot.transform.position);
            if (distToScene <= MaxVisualDistanceFromScene)
                return visual.transform.position;

            // Visual is stale (still at overworld coords). Fall back to the arrival position
            // that VisualSyncSystem will place the visual at on the next frame.
            var sim = _simulationRunner?.Simulation;
            var runner = sim?.FindRunner(runnerId);
            if (runner == null) return null;

            return _visualSyncSystem.GetNodeSceneArrivalPosition(runner);
        }

        private static float DistanceOrZero(Vector3 from, Vector3 to)
        {
            float distance = Vector3.Distance(from, to);
            return distance < 0.5f ? 0f : distance;
        }

        private Vector3 ComputeDepartureDirection(string fromNodeId, string toNodeId)
        {
            var sim = _simulationRunner?.Simulation;
            var map = sim?.CurrentGameState?.Map;
            if (map == null) return Vector3.zero;

            var fromNode = map.GetNode(fromNodeId);
            var toNode = map.GetNode(toNodeId);
            if (fromNode == null || toNode == null) return Vector3.zero;

            float dx = toNode.WorldX - fromNode.WorldX;
            float dz = toNode.WorldZ - fromNode.WorldZ;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < 0.001f) return Vector3.zero;

            return new Vector3(dx / dist, 0f, dz / dist);
        }
    }
}
