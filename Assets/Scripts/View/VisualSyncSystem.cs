using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;
using ProjectGuild.View.Combat;
using ProjectGuild.View.Runners;
using ProjectGuild.View.UI;

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
        [Header("Runner Visuals")]
        [Tooltip("Character prefabs for male runners. One picked randomly per runner.")]
        [SerializeField] private GameObject[] _maleRunnerPrefabs;
        [Tooltip("Character prefabs for female runners. One picked randomly per runner.")]
        [SerializeField] private GameObject[] _femaleRunnerPrefabs;
        [Tooltip("Animator controller for male runners (built by Tools > Project Guild > Build Runner Animator Controllers).")]
        [SerializeField] private RuntimeAnimatorController _masculineAnimatorController;
        [Tooltip("Animator controller for female runners.")]
        [SerializeField] private RuntimeAnimatorController _feminineAnimatorController;

        [Header("References")]
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private WorldSceneManager _worldSceneManager;
        [SerializeField] private NavMeshTravelPathCache _navMeshPathCache;

        [Header("Death / Ghost")]
        [Tooltip("Ghost prefab for male runners (PolygonDungeon Character_Ghost_01).")]
        [SerializeField] private GameObject _maleGhostPrefab;
        [Tooltip("Ghost prefab for female runners (PolygonDungeon Character_Ghost_02).")]
        [SerializeField] private GameObject _femaleGhostPrefab;

        [Header("Scene Transitions")]
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private SceneTransitionOverlay _sceneTransitionOverlay;

        [Header("UI")]
        [SerializeField] private UIManager _uiManager;

        // Runtime tracking
        private readonly Dictionary<string, RunnerVisual> _runnerVisuals = new();
        private readonly Dictionary<string, RunnerPositionContext> _runnerPositionContexts = new();

        // Travel direction cache: remembers where each runner came from for directional spawning
        private readonly Dictionary<string, TravelDirectionEntry> _travelDirectionCache = new();

        // Runners with a pending arrival fade — suppresses normal position updates
        // until the fade callback snaps the visual to the correct position.
        private readonly HashSet<string> _pendingArrivalFades = new();

        // Death hide timers: after death animation plays, swap to ghost model at hub
        private readonly Dictionary<string, float> _deathHideTimers = new();
        private const float RunnerDeathHideDelay = 2f;

        private bool _worldBuilt;

        /// <summary>
        /// Tracks each runner's previous positioning context so we can detect transitions
        /// (overworld ↔ node scene) and pick the right visual movement method.
        /// </summary>
        private struct RunnerPositionContext
        {
            public bool InNodeScene;       // Was positioned inside a loaded node scene?
            public string NodeId;          // Which node?
            public bool WasExitingNode;    // Was in exit phase of travel last frame?
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
            if (_uiManager == null)
                _uiManager = FindAnyObjectByType<UIManager>();
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

            // Subscribe to events for new runners, travel tracking, and combat
            Sim.Events.Subscribe<RunnerCreated>(OnRunnerCreated);
            Sim.Events.Subscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
            Sim.Events.Subscribe<RunnerArrivedAtNode>(OnRunnerArrivedAtNode);
            Sim.Events.Subscribe<CombatStarted>(OnCombatStarted);
            Sim.Events.Subscribe<CombatActionCompleted>(OnCombatAction);
            Sim.Events.Subscribe<RunnerDied>(OnRunnerDied);
            Sim.Events.Subscribe<RunnerRespawned>(OnRunnerRespawned);
            Sim.Events.Subscribe<RunnerTookDamage>(OnRunnerHit);

            _worldBuilt = true;
        }

        private void ClearWorld()
        {
            foreach (var kvp in _runnerVisuals)
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            _runnerVisuals.Clear();

            _runnerPositionContexts.Clear();
            _travelDirectionCache.Clear();
            _pendingArrivalFades.Clear();
            _deathHideTimers.Clear();

            _worldBuilt = false;
        }

        private void CreateRunnerVisual(Runner runner)
        {
            if (_runnerVisuals.ContainsKey(runner.Id)) return;

            GameObject obj;
            var prefabs = runner.Gender == RunnerGender.Female ? _femaleRunnerPrefabs : _maleRunnerPrefabs;
            if (prefabs != null && prefabs.Length > 0)
            {
                // Deterministic pick based on runner ID so the same runner always gets the same model
                int hash = runner.Id.GetHashCode() & 0x7FFFFFFF;
                var prefab = prefabs[hash % prefabs.Length];
                obj = Instantiate(prefab);

                // Assign the gender-appropriate animator controller
                var animator = obj.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    var controller = runner.Gender == RunnerGender.Female
                        ? _feminineAnimatorController
                        : _masculineAnimatorController;
                    if (controller != null)
                        animator.runtimeAnimatorController = controller;
                }

                // Synty prefabs have no collider — add one for 3D click-picking
                if (obj.GetComponentInChildren<Collider>() == null)
                {
                    var col = obj.AddComponent<CapsuleCollider>();
                    col.center = new Vector3(0f, 0.9f, 0f);
                    col.radius = 0.3f;
                    col.height = 1.8f;
                }
            }
            else
            {
                // Placeholder: capsule (no prefabs assigned or tests)
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

            var config = Sim.Config;
            long tickCount = Sim.CurrentGameState.TickCount;
            var prefs = _uiManager != null ? _uiManager.Preferences : null;

            // Update all runner visual positions with context-aware movement
            foreach (var runner in Sim.CurrentGameState.Runners)
            {
                if (!_runnerVisuals.TryGetValue(runner.Id, out var visual)) continue;
                if (visual == null) continue;

                // Pass sim data for nameplate bars
                visual.SimRunner = runner;
                visual.SimConfig = config;
                visual.CurrentTick = tickCount;
                visual.DisplayPrefs = prefs;

                UpdateRunnerVisualPosition(runner, visual);
            }

            // Process runner death hide timers
            if (_deathHideTimers.Count > 0)
            {
                var expired = new List<string>();
                foreach (var key in new List<string>(_deathHideTimers.Keys))
                {
                    float remaining = _deathHideTimers[key] - Time.deltaTime;
                    _deathHideTimers[key] = remaining;
                    if (remaining <= 0f)
                        expired.Add(key);
                }
                foreach (var id in expired)
                {
                    _deathHideTimers.Remove(id);
                    if (_runnerVisuals.TryGetValue(id, out var v) && v != null)
                    {
                        var runner = Sim.FindRunner(id);
                        GameObject ghostPrefab = runner?.Gender == RunnerGender.Female
                            ? _femaleGhostPrefab : _maleGhostPrefab;
                        if (ghostPrefab != null)
                            v.EnterGhostMode(ghostPrefab);
                        else
                            v.SetHidden(true);

                        // Snap to hub
                        v.SnapToPosition(GetHubSpawnPosition(id));
                    }
                }
            }

        }

        // ─── Runner Visual Updates ────────────────────────────────

        /// <summary>
        /// Determines the correct position for a runner and calls the appropriate
        /// movement method on the visual (snap, walk, or tick interpolation).
        /// </summary>
        private void UpdateRunnerVisualPosition(Runner runner, RunnerVisual visual)
        {
            // Dead runners: freeze during death anim, stay at hub once in ghost mode
            if (runner.State == RunnerState.Dead)
                return;

            // Pending arrival fade: visual is being faded in to a node scene.
            // Don't touch position until the fade callback snaps the visual.
            if (_pendingArrivalFades.Contains(runner.Id))
                return;

            _runnerPositionContexts.TryGetValue(runner.Id, out var prev);

            // ─── Exit phase: runner is exiting a node (sim-driven) ───
            bool isExiting = runner.State == RunnerState.Traveling
                && runner.Travel != null
                && runner.Travel.IsExitingNode;

            if (isExiting)
            {
                UpdateExitPhasePosition(runner, visual);
                _runnerPositionContexts[runner.Id] = new RunnerPositionContext
                {
                    InNodeScene = true,
                    NodeId = runner.Travel.FromNodeId,
                    WasExitingNode = true,
                };
                return;
            }

            // ─── Exit→overworld transition: exit just completed this frame ───
            if (prev.WasExitingNode && runner.State == RunnerState.Traveling && runner.Travel != null)
            {
                Vector3 overworldPos = GetTravelingPosition(runner);
                bool cameraFollowing = _cameraController != null
                    && _cameraController.CurrentTarget == visual.transform;

                if (cameraFollowing && _sceneTransitionOverlay != null)
                {
                    string capturedId = runner.Id;
                    _sceneTransitionOverlay.PlayFadeTransition(() =>
                    {
                        var r = Sim.FindRunner(capturedId);
                        if (r != null)
                            visual.SnapToPosition(GetTravelingPosition(r));
                    });
                }
                else
                {
                    visual.SnapToPosition(overworldPos);
                }

                _runnerPositionContexts[runner.Id] = new RunnerPositionContext
                {
                    InNodeScene = false,
                    NodeId = runner.CurrentNodeId,
                    WasExitingNode = false,
                };
                return;
            }

            // ─── Normal positioning (unchanged logic) ───
            Vector3 worldPos = GetRunnerWorldPosition(runner);

            bool inNodeScene = !IsRunnerInOverworld(runner);

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
                // Set athletics-based in-node speed for all in-node movement
                visual.WalkSpeed = GetVisualInNodeSpeed(runner);

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
                WasExitingNode = false,
            };
        }

        /// <summary>
        /// Position a runner during the sim-driven exit phase. The runner walks from
        /// their current position to the node exit point at athletics-based in-node speed.
        /// </summary>
        private void UpdateExitPhasePosition(Runner runner, RunnerVisual visual)
        {
            string nodeId = runner.Travel.FromNodeId;
            if (_worldSceneManager == null || !_worldSceneManager.IsNodeSceneReady(nodeId))
                return;

            var sceneRoot = _worldSceneManager.GetNodeSceneRoot(nodeId);
            if (sceneRoot == null) return;

            // Set walk speed to athletics-based in-node speed
            visual.WalkSpeed = GetVisualInNodeSpeed(runner);

            // Compute departure direction and exit point
            string toNodeId = runner.Travel.ToNodeId;
            Vector3 departureDir = ComputeDepartureDirection(nodeId, toNodeId);
            Vector3 exitPoint = sceneRoot.GetExitPosition(departureDir) + RunnerYOffset;

            NavMeshWalkTo(visual, exitPoint);
        }

        /// <summary>
        /// Returns true if a runner should be positioned in the overworld
        /// (traveling in overworld phase, or at a node without a loaded scene).
        /// During exit phase, runner is still visually in the node scene → returns false
        /// (exit phase is handled separately in UpdateRunnerVisualPosition).
        /// </summary>
        private bool IsRunnerInOverworld(Runner runner)
        {
            if (runner.State == RunnerState.Traveling)
            {
                // Exit phase: runner is still visually in the departure node scene
                if (runner.Travel != null && runner.Travel.IsExitingNode)
                    return false;
                return true;
            }
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
        private static readonly Vector3 RunnerYOffset = Vector3.zero;

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
        /// combat area, or spawn points.
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
                int spotIndex = runner.Gathering.SpotIndex;
                return sceneRoot.GetGatheringPosition(gatherableIndex, spotIndex) + RunnerYOffset;
            }

            // Fighting: position around the combat area center
            if (runner.State == RunnerState.Fighting)
            {
                return GetCombatPosition(runner, sceneRoot) + RunnerYOffset;
            }

            // Dead: keep at spawn position until death hide timer hides the visual
            if (runner.State == RunnerState.Dead)
            {
                return sceneRoot.GetSpawnPosition(0) + RunnerYOffset;
            }

            // Idle or other states: use a spawn point
            int runnerIndex = GetRunnerIndexAtNode(runner);
            return sceneRoot.GetSpawnPosition(runnerIndex) + RunnerYOffset;
        }

        /// <summary>
        /// Position a fighting runner in the combat area. Runners spread in a semicircle
        /// on one side of the combat center, facing the enemies.
        /// </summary>
        private Vector3 GetCombatPosition(Runner runner, NodeSceneRoot sceneRoot)
        {
            Vector3 center = sceneRoot.CombatAreaCenter;
            int idx = GetFighterIndexAtNode(runner);
            int total = GetFighterCountAtNode(runner.CurrentNodeId);

            // Spread runners in an arc on the "player side" (negative Z relative to center)
            float arcSpan = Mathf.Min(total * 0.8f, 3f); // max ~3m spread
            float offset = total <= 1 ? 0f : -arcSpan / 2f + arcSpan * idx / (total - 1);
            return center + new Vector3(offset, 0f, -2.5f);
        }

        private int GetFighterIndexAtNode(Runner runner)
        {
            int idx = 0;
            foreach (var r in Sim.CurrentGameState.Runners)
            {
                if (r.Id == runner.Id) break;
                if (r.State == RunnerState.Fighting && r.CurrentNodeId == runner.CurrentNodeId)
                    idx++;
            }
            return idx;
        }

        private int GetFighterCountAtNode(string nodeId)
        {
            int count = 0;
            foreach (var r in Sim.CurrentGameState.Runners)
            {
                if (r.State == RunnerState.Fighting && r.CurrentNodeId == nodeId)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Get the arrival (spawn) position for a runner entering a node scene.
        /// For circumference nodes with directional spawns, picks the point closest
        /// to the runner's overworld approach direction. Entrance nodes use round-robin.
        /// Public so NodeGeometryProvider can use it as a fallback when the runner visual
        /// hasn't been moved into the scene yet (same-tick arrival).
        /// </summary>
        public Vector3 GetNodeSceneArrivalPosition(Runner runner)
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
        /// <summary>
        /// Calculate in-node walk speed for a runner's visual.
        /// Same formula as the sim: overworld travel speed * InNodeSpeedMultiplier.
        /// </summary>
        private float GetVisualInNodeSpeed(Runner runner)
        {
            float athleticsLevel = runner.GetEffectiveLevel(
                Simulation.Core.SkillType.Athletics, Sim.Config);
            float overworldSpeed = Sim.Config.BaseTravelSpeed
                + (athleticsLevel - 1f) * Sim.Config.AthleticsSpeedPerLevel;
            return overworldSpeed * Sim.Config.InNodeSpeedMultiplier;
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
        }

        private void OnRunnerArrivedAtNode(RunnerArrivedAtNode evt)
        {
            // Don't clean _travelDirectionCache here — LateUpdate needs it for directional
            // spawn selection this frame. The cache is bounded (one entry per runner) and
            // overwritten on each new RunnerStartedTravel, so no cleanup needed.

            // Safety net: clean up any stale state
            _pendingArrivalFades.Remove(evt.RunnerId);
        }

        // ─── Combat Event Handlers ──────────────────────────────────

        private void OnCombatStarted(CombatStarted evt)
        {
            if (_runnerVisuals.TryGetValue(evt.RunnerId, out var visual) && visual != null)
                visual.SetCombatState(true);
        }

        private void OnCombatAction(CombatActionCompleted evt)
        {
            if (!_runnerVisuals.TryGetValue(evt.RunnerId, out var visual) || visual == null) return;

            // Pick animation based on ability type
            bool isMelee = evt.PrimaryEffectType == Simulation.Combat.EffectType.Damage
                || evt.PrimaryEffectType == Simulation.Combat.EffectType.Taunt
                || evt.PrimaryEffectType == Simulation.Combat.EffectType.TauntAoe
                || evt.PrimaryEffectType == Simulation.Combat.EffectType.TauntAll;
            bool isCast = evt.PrimaryEffectType == Simulation.Combat.EffectType.DamageAoe
                || evt.PrimaryEffectType == Simulation.Combat.EffectType.Heal
                || evt.PrimaryEffectType == Simulation.Combat.EffectType.HealSelf
                || evt.PrimaryEffectType == Simulation.Combat.EffectType.HealAoe;

            // Melee abilities with magic abilityId override to cast
            if (evt.AbilityId != null && (evt.AbilityId.Contains("fireball")
                || evt.AbilityId.Contains("fire_nova") || evt.AbilityId.Contains("culling_frost")))
                isCast = true;

            if (isCast)
                visual.PlayCastSpell();
            else
                visual.PlayMeleeAttack();

            // Face the target
            if (!string.IsNullOrEmpty(evt.TargetEnemyInstanceId))
            {
                var evm = FindAnyObjectByType<EnemyVisualManager>();
                var ev = evm?.GetEnemyVisual(evt.TargetEnemyInstanceId);
                if (ev != null)
                    visual.FaceTarget(ev.transform.position);
            }
        }

        private void OnRunnerHit(RunnerTookDamage evt)
        {
            if (_runnerVisuals.TryGetValue(evt.RunnerId, out var visual) && visual != null)
                visual.PlayHitReact();
        }

        private void OnRunnerDied(RunnerDied evt)
        {
            if (_runnerVisuals.TryGetValue(evt.RunnerId, out var visual) && visual != null)
            {
                visual.SetDead(true);
                visual.SetCombatState(false);
                _deathHideTimers[evt.RunnerId] = RunnerDeathHideDelay;
            }
        }

        private void OnRunnerRespawned(RunnerRespawned evt)
        {
            _deathHideTimers.Remove(evt.RunnerId);
            if (_runnerVisuals.TryGetValue(evt.RunnerId, out var visual) && visual != null)
            {
                // Snap to hub position before restoring model
                var runner = Sim?.FindRunner(evt.RunnerId);
                if (runner != null)
                    visual.SnapToPosition(GetRunnerWorldPosition(runner));

                if (visual.IsGhost)
                    visual.ExitGhostMode();
                else
                {
                    visual.SetDead(false);
                    visual.SetHidden(false);
                }

                // Reset position context so normal positioning picks up cleanly
                _runnerPositionContexts[evt.RunnerId] = new RunnerPositionContext
                {
                    InNodeScene = runner != null && !IsRunnerInOverworld(runner),
                    NodeId = runner?.CurrentNodeId,
                    WasExitingNode = false,
                };
            }
        }

        private Vector3 GetHubSpawnPosition(string runnerId)
        {
            var map = Sim?.CurrentGameState?.Map;
            if (map == null) return Vector3.zero;

            string hubId = map.HubNodeId;
            if (_worldSceneManager != null && _worldSceneManager.IsNodeSceneReady(hubId))
            {
                var sceneRoot = _worldSceneManager.GetNodeSceneRoot(hubId);
                if (sceneRoot != null)
                {
                    int hash = runnerId.GetHashCode() & 0x7FFFFFFF;
                    int idx = hash % Mathf.Max(1, sceneRoot.SpawnPoints.Length);
                    return sceneRoot.GetSpawnPosition(idx);
                }
            }

            // Fallback: overworld hub position
            var hubNode = map.GetNode(hubId);
            if (hubNode != null)
                return new Vector3(hubNode.WorldX, 0f, hubNode.WorldZ);

            return Vector3.zero;
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
                Sim.Events.Unsubscribe<CombatStarted>(OnCombatStarted);
                Sim.Events.Unsubscribe<CombatActionCompleted>(OnCombatAction);
                Sim.Events.Unsubscribe<RunnerDied>(OnRunnerDied);
                Sim.Events.Unsubscribe<RunnerRespawned>(OnRunnerRespawned);
                Sim.Events.Unsubscribe<RunnerTookDamage>(OnRunnerHit);
            }
        }
    }
}
