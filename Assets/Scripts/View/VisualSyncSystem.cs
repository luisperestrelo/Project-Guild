using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;
using ProjectGuild.View.Runners;

namespace ProjectGuild.View
{
    /// <summary>
    /// Synchronizes the simulation state with the Unity visual scene.
    /// Each simulation tick, this system updates runner visual positions based
    /// on their travel state and spawns visuals for new runners.
    ///
    /// Runners at nodes with loaded additive scenes are positioned using the
    /// scene's GatheringSpots/SpawnPoints. Runners at nodes without scenes
    /// (or traveling) use overworld positions.
    ///
    /// This is the core "bridge" that makes the pure C# simulation visible.
    /// </summary>
    public class VisualSyncSystem : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("Prefab for runner visual. If null, creates a placeholder capsule.")]
        [SerializeField] private GameObject _runnerPrefab;

        [Header("References")]
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private WorldSceneManager _worldSceneManager;
        [SerializeField] private NavMeshTravelPathCache _navMeshPathCache;

        [Header("Scene Transitions")]
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private SceneTransitionOverlay _sceneTransitionOverlay;

        // Runtime tracking
        private readonly Dictionary<string, RunnerVisual> _runnerVisuals = new();
        private readonly Dictionary<string, RunnerPositionContext> _runnerPositionContexts = new();

        // Travel direction cache: remembers where each runner came from for directional spawning
        private readonly Dictionary<string, TravelDirectionEntry> _travelDirectionCache = new();

        // Departure walk state: runners walking toward edge/entrance before scene transition
        private readonly Dictionary<string, DepartureWalkState> _departureWalks = new();

        // Runners with a pending arrival fade — suppresses normal position updates
        // until the fade callback snaps the visual to the correct position.
        private readonly HashSet<string> _pendingArrivalFades = new();

        private bool _worldBuilt;

        /// <summary>
        /// Tracks each runner's previous positioning context so we can detect transitions
        /// (overworld ↔ node scene) and pick the right visual movement method.
        /// </summary>
        private struct RunnerPositionContext
        {
            public bool InNodeScene;       // Was positioned inside a loaded node scene?
            public string NodeId;          // Which node?
        }

        /// <summary>
        /// Cached from/to node IDs from when a runner started traveling.
        /// Used to compute approach direction for directional spawns on arrival.
        /// </summary>
        private struct TravelDirectionEntry
        {
            public string FromNodeId;
            public string ToNodeId;
        }

        /// <summary>
        /// Active departure walk: runner walks toward scene edge/entrance before
        /// transitioning to overworld. Purely visual — sim travel progresses underneath.
        /// </summary>
        private struct DepartureWalkState
        {
            public string FromNodeId;
            public string ToNodeId;
            public Vector3 DepartureTarget;
            public float MaxDurationSeconds;
            public float ElapsedSeconds;
            public bool ReachedTarget;
        }

        private GameSimulation Sim => _simulationRunner?.Simulation;

        public RunnerVisual GetRunnerVisual(string runnerId)
        {
            return _runnerVisuals.TryGetValue(runnerId, out var visual) ? visual : null;
        }

        private static Material CreatePlaceholderMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }

        private void OnEnable()
        {
            if (_simulationRunner == null)
                _simulationRunner = FindAnyObjectByType<SimulationRunner>();
            if (_worldSceneManager == null)
                _worldSceneManager = FindAnyObjectByType<WorldSceneManager>();
            if (_navMeshPathCache == null)
                _navMeshPathCache = FindAnyObjectByType<NavMeshTravelPathCache>();
            if (_cameraController == null)
                _cameraController = FindAnyObjectByType<CameraController>();
            if (_sceneTransitionOverlay == null)
                _sceneTransitionOverlay = FindAnyObjectByType<SceneTransitionOverlay>();
        }

        /// <summary>
        /// Call after StartNewGame or LoadGame to spawn the visual world.
        /// Creates runner visuals. Node scene content is loaded on-demand by WorldSceneManager.
        /// </summary>
        public void BuildWorld()
        {
            ClearWorld();

            if (Sim?.CurrentGameState?.Map == null) return;

            // Create runner visuals
            foreach (var runner in Sim.CurrentGameState.Runners)
            {
                CreateRunnerVisual(runner);
            }

            // Subscribe to events for new runners and travel tracking
            Sim.Events.Subscribe<RunnerCreated>(OnRunnerCreated);
            Sim.Events.Subscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
            Sim.Events.Subscribe<RunnerArrivedAtNode>(OnRunnerArrivedAtNode);

            _worldBuilt = true;
        }

        private void ClearWorld()
        {
            foreach (var kvp in _runnerVisuals)
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            _runnerVisuals.Clear();

            _runnerPositionContexts.Clear();
            _travelDirectionCache.Clear();
            _departureWalks.Clear();
            _pendingArrivalFades.Clear();

            _worldBuilt = false;
        }

        private void CreateRunnerVisual(Runner runner)
        {
            if (_runnerVisuals.ContainsKey(runner.Id)) return;

            GameObject obj;
            if (_runnerPrefab != null)
            {
                obj = Instantiate(_runnerPrefab);
            }
            else
            {
                // Placeholder: capsule
                obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                obj.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
                var capsuleRenderer = obj.GetComponent<Renderer>();
                if (capsuleRenderer != null)
                    capsuleRenderer.material = CreatePlaceholderMaterial(Color.white);
            }

            // Put runners on their own physics layer for selective raycasting
            int runnerLayer = LayerMask.NameToLayer("Runners");
            if (runnerLayer >= 0)
                SetLayerRecursive(obj, runnerLayer);

            var visual = obj.AddComponent<RunnerVisual>();
            Vector3 startPos = GetRunnerWorldPosition(runner);
            visual.Initialize(runner.Id, runner.Name, startPos);

            _runnerVisuals[runner.Id] = visual;
        }

        private void LateUpdate()
        {
            if (!_worldBuilt || Sim == null) return;

            // Update all runner visual positions with context-aware movement
            foreach (var runner in Sim.CurrentGameState.Runners)
            {
                if (!_runnerVisuals.TryGetValue(runner.Id, out var visual)) continue;
                if (visual == null) continue;

                UpdateRunnerVisualPosition(runner, visual);
            }

        }

        // ─── Runner Visual Updates ────────────────────────────────

        /// <summary>
        /// Determines the correct position for a runner and calls the appropriate
        /// movement method on the visual (snap, walk, or tick interpolation).
        /// </summary>
        private void UpdateRunnerVisualPosition(Runner runner, RunnerVisual visual)
        {
            // Active departure walk: runner is walking toward scene edge before overworld transition.
            // Suppresses all normal position logic — the departure handler manages the snap.
            if (UpdateDepartureWalk(runner.Id, visual))
                return;

            // Pending arrival fade: visual is being faded in to a node scene.
            // Don't touch position until the fade callback snaps the visual.
            if (_pendingArrivalFades.Contains(runner.Id))
                return;

            Vector3 worldPos = GetRunnerWorldPosition(runner);

            bool inNodeScene = !IsRunnerInOverworld(runner);

            _runnerPositionContexts.TryGetValue(runner.Id, out var prev);

            // Scene transition (overworld ↔ node scene, or different node):
            // Snap to spawn point, then next frame walks to actual position (gathering spot, etc.)
            if (inNodeScene != prev.InNodeScene || runner.CurrentNodeId != prev.NodeId)
            {
                Vector3 arrivalPos = inNodeScene
                    ? GetNodeSceneArrivalPosition(runner)
                    : worldPos;

                // Fade when arriving at a node scene and camera is following this runner
                bool cameraFollowing = _cameraController != null
                    && _cameraController.CurrentTarget == visual.transform;

                if (inNodeScene && cameraFollowing && _sceneTransitionOverlay != null)
                {
                    // Suppress all updates until the fade callback fires
                    string capturedId = runner.Id;
                    Vector3 snapPos = arrivalPos;
                    _pendingArrivalFades.Add(capturedId);

                    _sceneTransitionOverlay.PlayFadeTransition(() =>
                    {
                        visual.SnapToPosition(snapPos);
                        _pendingArrivalFades.Remove(capturedId);

                        // Set context now that the snap has actually happened
                        _runnerPositionContexts[capturedId] = new RunnerPositionContext
                        {
                            InNodeScene = true,
                            NodeId = runner.CurrentNodeId,
                        };
                    });

                    // Don't update context yet — the fade callback will do it
                    return;
                }
                else
                {
                    visual.SnapToPosition(arrivalPos);
                }
            }
            // Inside a node scene: NavMesh walk (avoids obstacles) or straight-line fallback
            else if (inNodeScene)
            {
                NavMeshWalkTo(visual, worldPos);
            }
            // Overworld (traveling): tick interpolation
            else
            {
                visual.SetTargetPosition(worldPos);
            }

            // Update stored context
            _runnerPositionContexts[runner.Id] = new RunnerPositionContext
            {
                InNodeScene = inNodeScene,
                NodeId = runner.CurrentNodeId,
            };
        }

        /// <summary>
        /// Returns true if a runner should be positioned in the overworld
        /// (traveling, or at a node without a loaded scene).
        /// </summary>
        private bool IsRunnerInOverworld(Runner runner)
        {
            if (runner.State == RunnerState.Traveling) return true;
            if (_worldSceneManager == null) return true;
            return !_worldSceneManager.IsNodeSceneReady(runner.CurrentNodeId);
        }

        // ─── Runner Positioning ───────────────────────────────────

        /// <summary>
        /// Calculate the world position of a runner based on their simulation state.
        /// If traveling, interpolates between from/to node positions in the overworld.
        /// If at a node with a loaded scene, uses the scene's gathering/spawn spots.
        /// If at a node without a scene, uses overworld position with idle spread.
        /// </summary>
        private static readonly Vector3 RunnerYOffset = new(0f, 1f, 0f);

        private Vector3 GetRunnerWorldPosition(Runner runner)
        {
            // Traveling: interpolate in overworld space
            if (runner.State == RunnerState.Traveling && runner.Travel != null)
            {
                return GetTravelingPosition(runner);
            }

            // At a node: try to position inside loaded node scene
            return GetAtNodePosition(runner);
        }

        private Vector3 GetTravelingPosition(Runner runner)
        {
            // Try NavMesh path first (routes around obstacles, respects area/entrance nodes)
            if (_navMeshPathCache != null)
            {
                var pathPos = _navMeshPathCache.GetPositionAlongPath(runner.Id, runner.Travel.Progress);
                if (pathPos.HasValue)
                {
                    float terrainY = TerrainHeightSampler.GetHeight(pathPos.Value.x, pathPos.Value.z);
                    return new Vector3(pathPos.Value.x, terrainY, pathPos.Value.z) + RunnerYOffset;
                }
            }

            // Fallback: straight-line lerp (no NavMesh baked, or path computation failed)
            var fromNode = Sim.CurrentGameState.Map.GetNode(runner.Travel.FromNodeId);
            var toNode = Sim.CurrentGameState.Map.GetNode(runner.Travel.ToNodeId);

            if (fromNode != null && toNode != null)
            {
                Vector3 from = runner.Travel.StartWorldX.HasValue
                    ? new Vector3(runner.Travel.StartWorldX.Value, 0f, runner.Travel.StartWorldZ.Value)
                    : NodeWorldPosition(fromNode);
                Vector3 to = NodeWorldPosition(toNode);
                Vector3 lerpedXZ = Vector3.Lerp(from, to, runner.Travel.Progress);

                float terrainY = TerrainHeightSampler.GetHeight(lerpedXZ.x, lerpedXZ.z);
                return new Vector3(lerpedXZ.x, terrainY, lerpedXZ.z) + RunnerYOffset;
            }

            return Vector3.up; // Fallback
        }

        private Vector3 GetAtNodePosition(Runner runner)
        {
            string nodeId = runner.CurrentNodeId;
            var currentNode = Sim.CurrentGameState.Map.GetNode(nodeId);
            if (currentNode == null) return Vector3.up;

            // If this node has a loaded scene with a NodeSceneRoot, use scene positions
            if (_worldSceneManager != null && _worldSceneManager.IsNodeSceneReady(nodeId))
            {
                var sceneRoot = _worldSceneManager.GetNodeSceneRoot(nodeId);
                if (sceneRoot != null)
                    return GetNodeScenePosition(runner, sceneRoot);
            }

            // No loaded scene — use overworld position with idle spread
            return GetOverworldNodePosition(runner, currentNode);
        }

        /// <summary>
        /// Position a runner inside a loaded node scene using gathering spots, deposit point,
        /// or spawn points.
        /// </summary>
        private Vector3 GetNodeScenePosition(Runner runner, NodeSceneRoot sceneRoot)
        {
            // Depositing: walk to the bank/deposit point (Guild Hall)
            if (runner.State == RunnerState.Depositing && sceneRoot.DepositPointPosition.HasValue)
            {
                return sceneRoot.DepositPointPosition.Value + RunnerYOffset;
            }

            // Gathering: use the gathering spot for the runner's current gatherable index
            if (runner.State == RunnerState.Gathering && runner.Gathering != null)
            {
                int gatherableIndex = runner.Gathering.GatherableIndex;
                int runnerIndexInGroup = GetRunnerIndexInGatherableGroup(runner, gatherableIndex);
                return sceneRoot.GetGatheringPosition(gatherableIndex, runnerIndexInGroup) + RunnerYOffset;
            }

            // Idle or other states: use a spawn point
            int runnerIndex = GetRunnerIndexAtNode(runner);
            return sceneRoot.GetSpawnPosition(runnerIndex) + RunnerYOffset;
        }

        /// <summary>
        /// Get the arrival (spawn) position for a runner entering a node scene.
        /// For circumference nodes with directional spawns, picks the point closest
        /// to the runner's overworld approach direction. Entrance nodes use round-robin.
        /// </summary>
        private Vector3 GetNodeSceneArrivalPosition(Runner runner)
        {
            string nodeId = runner.CurrentNodeId;
            if (_worldSceneManager != null && _worldSceneManager.IsNodeSceneReady(nodeId))
            {
                var sceneRoot = _worldSceneManager.GetNodeSceneRoot(nodeId);
                if (sceneRoot != null)
                {
                    int runnerIndex = GetRunnerIndexAtNode(runner);

                    // Try directional spawn if we have approach direction info
                    if (sceneRoot.HasDirectionalSpawns
                        && _travelDirectionCache.TryGetValue(runner.Id, out var travelDir))
                    {
                        Vector3 approachDir = ComputeApproachDirection(travelDir.FromNodeId, nodeId);
                        if (approachDir.sqrMagnitude > 0.001f)
                            return sceneRoot.GetDirectionalSpawnPosition(approachDir, runnerIndex) + RunnerYOffset;
                    }

                    return sceneRoot.GetSpawnPosition(runnerIndex) + RunnerYOffset;
                }
            }

            // Fallback: use the final position
            return GetRunnerWorldPosition(runner);
        }

        /// <summary>
        /// Compute the XZ approach direction for a runner arriving at a node.
        /// Returns the direction FROM the destination BACK TOWARD the source
        /// (i.e., the direction the runner is coming from).
        /// </summary>
        private Vector3 ComputeApproachDirection(string fromNodeId, string toNodeId)
        {
            var map = Sim?.CurrentGameState?.Map;
            if (map == null) return Vector3.zero;

            var fromNode = map.GetNode(fromNodeId);
            var toNode = map.GetNode(toNodeId);
            if (fromNode == null || toNode == null) return Vector3.zero;

            float dx = fromNode.WorldX - toNode.WorldX;
            float dz = fromNode.WorldZ - toNode.WorldZ;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < 0.001f) return Vector3.zero;

            return new Vector3(dx / dist, 0f, dz / dist);
        }

        /// <summary>
        /// Position a runner at a node in the overworld (no loaded scene).
        /// Spreads multiple runners in a small circle so they don't stack.
        /// </summary>
        private Vector3 GetOverworldNodePosition(Runner runner, WorldNode node)
        {
            int idleIndex = GetRunnerIndexAtNode(runner);

            Vector3 spread = Vector3.zero;
            if (idleIndex > 0)
            {
                float angle = idleIndex * 2.094f; // ~120 degrees apart (2*PI/3)
                spread = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.2f;
            }

            return NodeWorldPosition(node) + RunnerYOffset + spread;
        }

        /// <summary>
        /// Get the index of this runner among all non-traveling runners at the same node.
        /// Used for spreading runners at spawn points or in a circle.
        /// </summary>
        private int GetRunnerIndexAtNode(Runner runner)
        {
            int index = 0;
            foreach (var r in Sim.CurrentGameState.Runners)
            {
                if (r.Id == runner.Id) break;
                if (r.CurrentNodeId == runner.CurrentNodeId && r.State != RunnerState.Traveling)
                    index++;
            }
            return index;
        }

        /// <summary>
        /// Get the index of this runner among all runners gathering the same gatherable
        /// at the same node. Used to spread runners across spots within a GatherableSpotGroup.
        /// </summary>
        private int GetRunnerIndexInGatherableGroup(Runner runner, int gatherableIndex)
        {
            int index = 0;
            foreach (var r in Sim.CurrentGameState.Runners)
            {
                if (r.Id == runner.Id) break;
                if (r.CurrentNodeId == runner.CurrentNodeId
                    && r.State == RunnerState.Gathering
                    && r.Gathering != null
                    && r.Gathering.GatherableIndex == gatherableIndex)
                {
                    index++;
                }
            }
            return index;
        }

        private Vector3 NodeWorldPosition(WorldNode node)
        {
            float terrainY = TerrainHeightSampler.GetHeight(node.WorldX, node.WorldZ);
            return new Vector3(node.WorldX, terrainY, node.WorldZ);
        }

        // ─── In-Scene NavMesh Pathfinding ────────────────────────────

        /// <summary>
        /// Walk a runner visual to a target position using a NavMesh path if available.
        /// Falls back to straight-line walk if NavMesh has no data at the positions
        /// (e.g. node scene without a baked NavMeshSurface).
        /// </summary>
        private static void NavMeshWalkTo(RunnerVisual visual, Vector3 target)
        {
            Vector3 from = visual.transform.position;

            // Try to find valid NavMesh positions near start and end
            if (!NavMesh.SamplePosition(from, out NavMeshHit startHit, 5f, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(target, out NavMeshHit endHit, 5f, NavMesh.AllAreas))
            {
                visual.WalkToPosition(target);
                return;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path)
                || path.status == NavMeshPathStatus.PathInvalid
                || path.corners.Length < 2)
            {
                visual.WalkToPosition(target);
                return;
            }

            // NavMesh corners are at ground level — preserve the target's Y offset
            // so runners walk above the ground plane, not at surface level.
            float yOffset = target.y - endHit.position.y;
            var corners = path.corners;
            for (int i = 0; i < corners.Length; i++)
                corners[i].y += yOffset;

            visual.WalkAlongPath(corners);
        }

        // ─── Event Handlers ──────────────────────────────────────────

        private void OnRunnerCreated(RunnerCreated evt)
        {
            var runner = Sim.FindRunner(evt.RunnerId);
            if (runner != null)
                CreateRunnerVisual(runner);
        }

        private void OnRunnerStartedTravel(RunnerStartedTravel evt)
        {
            // Cache travel direction for directional spawning on arrival
            _travelDirectionCache[evt.RunnerId] = new TravelDirectionEntry
            {
                FromNodeId = evt.FromNodeId,
                ToNodeId = evt.ToNodeId,
            };

            // Start departure walk if runner was visually in a node scene
            TryStartDepartureWalk(evt);
        }

        private void OnRunnerArrivedAtNode(RunnerArrivedAtNode evt)
        {
            // Don't clean _travelDirectionCache here — LateUpdate needs it for directional
            // spawn selection this frame. The cache is bounded (one entry per runner) and
            // overwritten on each new RunnerStartedTravel, so no cleanup needed.

            // Safety nets: clean up any stale state
            _departureWalks.Remove(evt.RunnerId);
            _pendingArrivalFades.Remove(evt.RunnerId);
        }

        // ─── Departure Walk ─────────────────────────────────────────

        /// <summary>
        /// Minimum estimated travel duration to trigger a departure walk.
        /// Very short trips skip the walk-out animation entirely.
        /// </summary>
        private const float DepartureWalkMinTripSeconds = 3f;

        /// <summary>
        /// Walk speed for departure animation (matches RunnerVisual.WalkSpeed).
        /// </summary>
        private const float DepartureWalkSpeed = 8f;

        private void TryStartDepartureWalk(RunnerStartedTravel evt)
        {
            // Only if runner was visually in a node scene
            if (!_runnerPositionContexts.TryGetValue(evt.RunnerId, out var prevCtx)) return;
            if (!prevCtx.InNodeScene) return;

            // Skip for very short trips
            if (evt.EstimatedDurationSeconds < DepartureWalkMinTripSeconds) return;

            // Need the scene root to compute departure target
            if (_worldSceneManager == null || !_worldSceneManager.IsNodeSceneReady(evt.FromNodeId)) return;
            var sceneRoot = _worldSceneManager.GetNodeSceneRoot(evt.FromNodeId);
            if (sceneRoot == null) return;

            Vector3 departureTarget = ComputeDepartureTarget(sceneRoot, evt.FromNodeId, evt.ToNodeId);

            // Compute duration from actual walk distance (not trip duration)
            if (!_runnerVisuals.TryGetValue(evt.RunnerId, out var visual)) return;

            float walkDistance = Vector3.Distance(visual.transform.position, departureTarget);
            if (walkDistance < 0.5f) return; // Already at the edge, skip

            float duration = walkDistance / DepartureWalkSpeed;

            _departureWalks[evt.RunnerId] = new DepartureWalkState
            {
                FromNodeId = evt.FromNodeId,
                ToNodeId = evt.ToNodeId,
                DepartureTarget = departureTarget,
                MaxDurationSeconds = duration,
                ElapsedSeconds = 0f,
                ReachedTarget = false,
            };

            // Start the walk animation immediately (NavMesh to avoid obstacles)
            NavMeshWalkTo(visual, departureTarget);
        }

        /// <summary>
        /// Compute where a departing runner should walk to.
        /// Circumference nodes: directional spawn closest to destination direction.
        /// Entrance nodes: first spawn point (the entrance/cave mouth).
        /// </summary>
        private Vector3 ComputeDepartureTarget(NodeSceneRoot sceneRoot, string fromNodeId, string toNodeId)
        {
            if (sceneRoot.HasDirectionalSpawns)
            {
                // Walk toward the edge closest to the destination
                Vector3 departDir = ComputeDepartureDirection(fromNodeId, toNodeId);
                if (departDir.sqrMagnitude > 0.001f)
                    return sceneRoot.GetDirectionalSpawnPosition(departDir, 0) + RunnerYOffset;
            }

            // Entrance nodes: walk to the entrance (first spawn point)
            return sceneRoot.GetSpawnPosition(0) + RunnerYOffset;
        }

        /// <summary>
        /// Compute the departure direction: from source node TOWARD destination.
        /// Opposite of approach direction (which is from destination toward source).
        /// </summary>
        private Vector3 ComputeDepartureDirection(string fromNodeId, string toNodeId)
        {
            var map = Sim?.CurrentGameState?.Map;
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

        /// <summary>
        /// Update an active departure walk. Returns true if the walk is still active
        /// (suppresses normal position update), false if complete.
        /// </summary>
        private bool UpdateDepartureWalk(string runnerId, RunnerVisual visual)
        {
            if (!_departureWalks.TryGetValue(runnerId, out var walk)) return false;

            walk.ElapsedSeconds += Time.deltaTime;

            // Check if reached target position
            float distToTarget = Vector3.Distance(visual.transform.position, walk.DepartureTarget);
            if (distToTarget < 0.3f)
                walk.ReachedTarget = true;

            // Walk complete: reached target or timed out
            if (walk.ReachedTarget || walk.ElapsedSeconds >= walk.MaxDurationSeconds)
            {
                CompleteDepartureWalk(runnerId, visual);
                return false; // Walk done, let normal update run
            }

            // Still walking — update state back to dictionary
            _departureWalks[runnerId] = walk;
            return true;
        }

        private void CompleteDepartureWalk(string runnerId, RunnerVisual visual)
        {
            _departureWalks.Remove(runnerId);

            var runner = Sim.FindRunner(runnerId);
            if (runner == null) return;

            // If camera is following this runner, fade before snapping.
            // Position is computed in the callback so it reflects where the runner
            // actually is at mid-fade time (not when the fade started).
            bool cameraFollowing = _cameraController != null
                && _cameraController.CurrentTarget == visual.transform;

            if (cameraFollowing && _sceneTransitionOverlay != null)
            {
                string capturedId = runnerId;
                _sceneTransitionOverlay.PlayFadeTransition(() =>
                {
                    var r = Sim.FindRunner(capturedId);
                    if (r != null)
                        visual.SnapToPosition(GetRunnerWorldPosition(r));
                });
            }
            else
            {
                visual.SnapToPosition(GetRunnerWorldPosition(runner));
            }

            // Update position context to overworld
            _runnerPositionContexts[runnerId] = new RunnerPositionContext
            {
                InNodeScene = false,
                NodeId = runner.CurrentNodeId,
            };
        }

        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private void OnDestroy()
        {
            if (Sim?.Events != null)
            {
                Sim.Events.Unsubscribe<RunnerCreated>(OnRunnerCreated);
                Sim.Events.Unsubscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
                Sim.Events.Unsubscribe<RunnerArrivedAtNode>(OnRunnerArrivedAtNode);
            }
        }
    }
}
