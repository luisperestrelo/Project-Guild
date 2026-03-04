using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectGuild.Bridge;
using ProjectGuild.Data;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.View
{
    /// <summary>
    /// Caches per-runner NavMesh paths for overworld travel and provides NavMesh-backed
    /// distances to the simulation via IPathDistanceProvider.
    ///
    /// Path computation happens synchronously when the sim requests a travel distance
    /// (GetTravelDistance). The computed path is cached so VisualSyncSystem can map
    /// travel progress (0→1) along the NavMesh curve via GetPositionAlongPath.
    ///
    /// The RunnerStartedTravel event subscription is a safety net — it only computes
    /// a path if none was already cached (covers the explicit-distance test overload).
    ///
    /// Falls back gracefully: returns null if NavMesh isn't baked or path fails,
    /// letting the sim use Euclidean fallback and VisualSyncSystem use straight-line lerp.
    /// </summary>
    public class NavMeshTravelPathCache : MonoBehaviour, IPathDistanceProvider
    {
        [Header("References")]
        [SerializeField] private SimulationRunner _simulationRunner;

        private GameSimulation Sim => _simulationRunner?.Simulation;

        // Per-node asset data for approach calculations (view-only fields)
        private Dictionary<string, WorldNodeAsset> _nodeAssetLookup;

        // Cached paths indexed by runner ID
        private readonly Dictionary<string, CachedTravelPath> _cachedPaths = new();

        // Last visual position per runner — updated every frame by GetPositionAlongPath.
        // Used as redirect departure so the new path starts where the runner visually is.
        private readonly Dictionary<string, Vector3> _lastPathPositions = new();

        // Log NavMesh failures once to avoid console spam
        private bool _loggedNavMeshFallback;

        /// <summary>
        /// A computed NavMesh path with precomputed cumulative distances for fast
        /// progress-to-position mapping.
        /// </summary>
        private struct CachedTravelPath
        {
            public Vector3[] Waypoints;
            public float[] CumulativeDistances; // 3D distances (for visual progress mapping)
            public float TotalPathLength;       // 3D total (for visual progress mapping)
            public float TotalPathLengthXZ;     // XZ-only total (for sim travel distance)
            public bool IsValid;
        }

        // ─── Lifecycle ───────────────────────────────────────────

        private void OnEnable()
        {
            if (_simulationRunner == null)
                _simulationRunner = FindAnyObjectByType<SimulationRunner>();
        }

        /// <summary>
        /// Initialize after sim and world scene manager are ready.
        /// Subscribes to travel events and builds node asset lookup.
        /// </summary>
        public void Initialize()
        {
            ClearAll();

            if (Sim?.CurrentGameState?.Map == null) return;

            BuildNodeAssetLookup();

            Sim.Events.Subscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
            Sim.Events.Subscribe<RunnerArrivedAtNode>(OnRunnerArrivedAtNode);
        }

        /// <summary>
        /// Tear down: unsubscribe from events and clear all cached paths.
        /// </summary>
        public void ClearAll()
        {
            _cachedPaths.Clear();
            _lastPathPositions.Clear();
            _loggedNavMeshFallback = false;
            _nodeAssetLookup = null;

            if (Sim?.Events != null)
            {
                Sim.Events.Unsubscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
                Sim.Events.Unsubscribe<RunnerArrivedAtNode>(OnRunnerArrivedAtNode);
            }
        }

        private void BuildNodeAssetLookup()
        {
            _nodeAssetLookup = new Dictionary<string, WorldNodeAsset>();
            var mapAsset = _simulationRunner?.WorldMapAsset;
            if (mapAsset?.Nodes == null) return;

            foreach (var nodeAsset in mapAsset.Nodes)
            {
                if (nodeAsset != null && !string.IsNullOrEmpty(nodeAsset.Id))
                    _nodeAssetLookup[nodeAsset.Id] = nodeAsset;
            }
        }

        // ─── IPathDistanceProvider ──────────────────────────────

        /// <summary>
        /// Compute a NavMesh path from one node to another and return its length.
        /// The path is cached for VisualSyncSystem to use via GetPositionAlongPath.
        /// Returns null if NavMesh is unavailable or path computation fails.
        /// </summary>
        public float? GetTravelDistance(string runnerId, string fromNodeId, string toNodeId)
        {
            var map = Sim?.CurrentGameState?.Map;
            if (map == null || _nodeAssetLookup == null) return null;

            var fromNode = map.GetNode(fromNodeId);
            var toNode = map.GetNode(toNodeId);
            if (fromNode == null || toNode == null) return null;

            Vector3 departure = ComputeNodeDeparturePoint(fromNode, toNode);
            Vector3 arrival = ComputeArrivalPoint(fromNode, toNode);

            var cachedPath = ComputeNavMeshPath(departure, arrival);
            if (!cachedPath.IsValid)
            {
                LogNavMeshFallback(fromNodeId, toNodeId);
                return null;
            }

            _cachedPaths[runnerId] = cachedPath;

            return cachedPath.TotalPathLength;
        }

        /// <summary>
        /// Compute a NavMesh path from a world position to a node (mid-travel redirect)
        /// and return its length. The path is cached for VisualSyncSystem.
        /// Returns null if NavMesh is unavailable or path computation fails.
        /// </summary>
        public float? GetTravelDistance(string runnerId, float fromX, float fromZ, string toNodeId)
        {
            var map = Sim?.CurrentGameState?.Map;
            if (map == null || _nodeAssetLookup == null) return null;

            var toNode = map.GetNode(toNodeId);
            if (toNode == null) return null;

            // If the runner was on a NavMesh path, start the new path from their
            // last visual position (not the sim's straight-line virtual position).
            float departX = fromX;
            float departZ = fromZ;
            if (_lastPathPositions.TryGetValue(runnerId, out var lastPos))
            {
                departX = lastPos.x;
                departZ = lastPos.z;
            }

            float y = TerrainHeightSampler.GetHeight(departX, departZ);
            Vector3 departure = new Vector3(departX, y, departZ);
            Vector3 arrival = ComputeArrivalPointFromPosition(departX, departZ, toNode);

            var cachedPath = ComputeNavMeshPath(departure, arrival);
            if (!cachedPath.IsValid)
            {
                LogNavMeshFallback($"({fromX:F0},{fromZ:F0})", toNodeId);
                return null;
            }

            _cachedPaths[runnerId] = cachedPath;

            return cachedPath.TotalPathLength;
        }

        // ─── Public API (Visual) ────────────────────────────────

        /// <summary>
        /// Get the world position along a cached NavMesh path at the given progress (0→1).
        /// Returns null if no valid path is cached for this runner — caller should
        /// fall back to straight-line lerp.
        /// </summary>
        public Vector3? GetPositionAlongPath(string runnerId, float progress)
        {
            if (!_cachedPaths.TryGetValue(runnerId, out var path) || !path.IsValid)
                return null;

            if (path.Waypoints.Length == 0)
                return null;

            Vector3 result;

            if (path.Waypoints.Length == 1)
            {
                result = path.Waypoints[0];
            }
            else
            {
                float targetDist = Mathf.Clamp01(progress) * path.TotalPathLength;
                result = path.Waypoints[^1]; // default to last waypoint

                // Walk the waypoints to find the segment containing targetDist
                for (int i = 1; i < path.Waypoints.Length; i++)
                {
                    if (targetDist <= path.CumulativeDistances[i])
                    {
                        float segmentStart = path.CumulativeDistances[i - 1];
                        float segmentLength = path.CumulativeDistances[i] - segmentStart;
                        float t = segmentLength > 0.001f
                            ? (targetDist - segmentStart) / segmentLength
                            : 0f;

                        result = Vector3.Lerp(path.Waypoints[i - 1], path.Waypoints[i], t);
                        break;
                    }
                }
            }

            _lastPathPositions[runnerId] = result;
            return result;
        }

        // ─── Event Handlers ──────────────────────────────────────

        /// <summary>
        /// Safety net: compute a NavMesh path only if GetTravelDistance didn't already
        /// cache one (covers the explicit-distance CommandTravel test overload).
        /// </summary>
        private void OnRunnerStartedTravel(RunnerStartedTravel evt)
        {
            if (_cachedPaths.ContainsKey(evt.RunnerId)) return;

            var runner = Sim?.FindRunner(evt.RunnerId);
            if (runner?.Travel == null) return;

            var fromNode = Sim.CurrentGameState.Map.GetNode(evt.FromNodeId);
            var toNode = Sim.CurrentGameState.Map.GetNode(evt.ToNodeId);
            if (fromNode == null || toNode == null) return;

            Vector3 departure = ComputeDeparturePoint(runner, fromNode, toNode);
            Vector3 arrival = ComputeArrivalPoint(fromNode, toNode);

            var cachedPath = ComputeNavMeshPath(departure, arrival);
            _cachedPaths[evt.RunnerId] = cachedPath;
        }

        private void OnRunnerArrivedAtNode(RunnerArrivedAtNode evt)
        {
            _cachedPaths.Remove(evt.RunnerId);
            _lastPathPositions.Remove(evt.RunnerId);
        }

        // ─── Departure / Arrival Point Computation ───────────────

        /// <summary>
        /// Compute departure point for a runner (used by the event handler safety net).
        /// Checks for redirect (StartWorldX), then entrance, then circumference.
        /// </summary>
        private Vector3 ComputeDeparturePoint(Runner runner, WorldNode fromNode, WorldNode toNode)
        {
            // Redirect: use the runner's current virtual position
            if (runner.Travel.StartWorldX.HasValue)
            {
                float x = runner.Travel.StartWorldX.Value;
                float z = runner.Travel.StartWorldZ.Value;
                float y = TerrainHeightSampler.GetHeight(x, z);
                return new Vector3(x, y, z);
            }

            return ComputeNodeDeparturePoint(fromNode, toNode);
        }

        /// <summary>
        /// Compute departure point from a node (non-redirect).
        /// Entrance nodes use their entrance position; area nodes use circumference edge.
        /// </summary>
        private Vector3 ComputeNodeDeparturePoint(WorldNode fromNode, WorldNode toNode)
        {
            if (TryGetEntrancePosition(fromNode.Id, out Vector3 entrancePos))
                return entrancePos;

            return GetCircumferencePoint(fromNode, toNode);
        }

        /// <summary>
        /// Compute the arrival point at the destination node, approaching from another node.
        /// - Entrance node: arrive at the entrance position
        /// - Area node: nearest point on circumference from approach direction
        /// </summary>
        private Vector3 ComputeArrivalPoint(WorldNode fromNode, WorldNode toNode)
        {
            // Entrance node: arrive at the entrance
            if (TryGetEntrancePosition(toNode.Id, out Vector3 entrancePos))
                return entrancePos;

            // Area node: circumference edge from approach direction
            return GetCircumferencePoint(toNode, fromNode);
        }

        /// <summary>
        /// Compute the arrival point at a node, approaching from a world position (redirect).
        /// </summary>
        private Vector3 ComputeArrivalPointFromPosition(float fromX, float fromZ, WorldNode toNode)
        {
            if (TryGetEntrancePosition(toNode.Id, out Vector3 entrancePos))
                return entrancePos;

            return GetCircumferencePointToward(toNode, fromX, fromZ);
        }

        /// <summary>
        /// Try to get the entrance position for a node.
        /// Returns false if the node is not an entrance node or has no asset data.
        /// </summary>
        private bool TryGetEntrancePosition(string nodeId, out Vector3 position)
        {
            position = Vector3.zero;

            if (_nodeAssetLookup == null) return false;
            if (!_nodeAssetLookup.TryGetValue(nodeId, out var asset)) return false;
            if (!asset.IsEntranceNode) return false;

            float centerX = asset.WorldX + asset.EntranceOffset.x;
            float centerZ = asset.WorldZ + asset.EntranceOffset.z;
            float y = TerrainHeightSampler.GetHeight(centerX, centerZ);
            position = new Vector3(centerX, y, centerZ);
            return true;
        }

        /// <summary>
        /// Get the point on a node's circumference in the direction of another node.
        /// If the node has no approach radius, returns its center position.
        /// </summary>
        private Vector3 GetCircumferencePoint(WorldNode node, WorldNode toward)
        {
            return GetCircumferencePointToward(node, toward.WorldX, toward.WorldZ);
        }

        /// <summary>
        /// Get the point on a node's circumference toward a world position.
        /// Shared implementation for both node-to-node and position-based approach.
        /// </summary>
        private Vector3 GetCircumferencePointToward(WorldNode node, float towardX, float towardZ)
        {
            Vector3 center = NodeWorldPosition(node);

            float radius = GetApproachRadius(node.Id);
            if (radius <= 0f) return center;

            float dx = towardX - node.WorldX;
            float dz = towardZ - node.WorldZ;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            if (dist < 0.001f) return center;

            float nx = dx / dist;
            float nz = dz / dist;

            float edgeX = node.WorldX + nx * radius;
            float edgeZ = node.WorldZ + nz * radius;
            float edgeY = TerrainHeightSampler.GetHeight(edgeX, edgeZ);

            return new Vector3(edgeX, edgeY, edgeZ);
        }

        private float GetApproachRadius(string nodeId)
        {
            if (_nodeAssetLookup != null && _nodeAssetLookup.TryGetValue(nodeId, out var asset))
                return asset.ApproachRadius;
            return 0f;
        }

        private static Vector3 NodeWorldPosition(WorldNode node)
        {
            float y = TerrainHeightSampler.GetHeight(node.WorldX, node.WorldZ);
            return new Vector3(node.WorldX, y, node.WorldZ);
        }

        private void LogNavMeshFallback(string from, string to)
        {
            if (_loggedNavMeshFallback) return;
            _loggedNavMeshFallback = true;
            Debug.LogWarning($"[NavMeshTravelPathCache] NavMesh path failed ({from} -> {to}). " +
                             "Falling back to Euclidean distance. Is the NavMesh baked?");
        }

        // ─── NavMesh Path Computation ────────────────────────────

        /// <summary>
        /// Compute a NavMesh path between two world positions.
        /// Returns a CachedTravelPath with IsValid=false if NavMesh isn't available
        /// or path computation fails.
        /// </summary>
        private static CachedTravelPath ComputeNavMeshPath(Vector3 departure, Vector3 arrival)
        {
            var navPath = new NavMeshPath();

            // Sample nearest valid NavMesh positions (agents may be slightly off-mesh)
            if (!NavMesh.SamplePosition(departure, out NavMeshHit startHit, 10f, NavMesh.AllAreas))
                return new CachedTravelPath { IsValid = false };

            if (!NavMesh.SamplePosition(arrival, out NavMeshHit endHit, 10f, NavMesh.AllAreas))
                return new CachedTravelPath { IsValid = false };

            if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, navPath))
                return new CachedTravelPath { IsValid = false };

            if (navPath.status == NavMeshPathStatus.PathInvalid || navPath.corners.Length < 2)
                return new CachedTravelPath { IsValid = false };

            // Build waypoints with terrain-sampled Y
            var corners = navPath.corners;
            var waypoints = new Vector3[corners.Length];
            var cumulativeDistances = new float[corners.Length];

            waypoints[0] = new Vector3(corners[0].x,
                TerrainHeightSampler.GetHeight(corners[0].x, corners[0].z),
                corners[0].z);
            cumulativeDistances[0] = 0f;

            float totalLength = 0f;
            float totalLengthXZ = 0f;
            for (int i = 1; i < corners.Length; i++)
            {
                waypoints[i] = new Vector3(corners[i].x,
                    TerrainHeightSampler.GetHeight(corners[i].x, corners[i].z),
                    corners[i].z);

                totalLength += Vector3.Distance(waypoints[i - 1], waypoints[i]);
                cumulativeDistances[i] = totalLength;

                float dxSeg = waypoints[i].x - waypoints[i - 1].x;
                float dzSeg = waypoints[i].z - waypoints[i - 1].z;
                totalLengthXZ += Mathf.Sqrt(dxSeg * dxSeg + dzSeg * dzSeg);
            }

            return new CachedTravelPath
            {
                Waypoints = waypoints,
                CumulativeDistances = cumulativeDistances,
                TotalPathLength = totalLength,
                TotalPathLengthXZ = totalLengthXZ,
                IsValid = true,
            };
        }
    }
}
