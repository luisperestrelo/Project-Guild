using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;
using ProjectGuild.View.Runners;

namespace ProjectGuild.View
{
    /// <summary>
    /// Synchronizes the simulation state with the Unity visual scene.
    /// Each simulation tick, this system updates runner visual positions based
    /// on their travel state, spawns visuals for new runners, and handles
    /// world node visual markers.
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

        [Tooltip("Prefab for world node marker. If null, creates a placeholder sphere.")]
        [SerializeField] private GameObject _nodeMarkerPrefab;

        [Header("References")]
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private WorldSceneManager _worldSceneManager;

        // Runtime tracking
        private readonly Dictionary<string, RunnerVisual> _runnerVisuals = new();
        private readonly Dictionary<string, GameObject> _nodeMarkers = new();
        private readonly Dictionary<string, RunnerPositionContext> _runnerPositionContexts = new();
        private Dictionary<string, GameObject> _entranceMarkerPrefabs = new();
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
        }

        /// <summary>
        /// Call after StartNewGame or LoadGame to spawn the visual world.
        /// Creates overworld node markers and runner visuals.
        /// Node scene content is loaded on-demand by WorldSceneManager.
        /// </summary>
        /// <param name="entranceMarkerPrefabs">
        /// Optional dictionary mapping nodeId → entrance marker prefab.
        /// Nodes with a prefab get that model; others get a placeholder cylinder.
        /// </param>
        public void BuildWorld(Dictionary<string, GameObject> entranceMarkerPrefabs = null)
        {
            ClearWorld();

            _entranceMarkerPrefabs = entranceMarkerPrefabs ?? new Dictionary<string, GameObject>();

            if (Sim?.CurrentGameState?.Map == null) return;

            // Create overworld node markers (entrance markers for each node)
            foreach (var node in Sim.CurrentGameState.Map.Nodes)
            {
                CreateNodeMarker(node);
            }

            // Create runner visuals
            foreach (var runner in Sim.CurrentGameState.Runners)
            {
                CreateRunnerVisual(runner);
            }

            // Subscribe to events for new runners
            Sim.Events.Subscribe<RunnerCreated>(OnRunnerCreated);

            _worldBuilt = true;
        }

        private void ClearWorld()
        {
            foreach (var kvp in _runnerVisuals)
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            _runnerVisuals.Clear();

            foreach (var kvp in _nodeMarkers)
                if (kvp.Value != null) Destroy(kvp.Value);
            _nodeMarkers.Clear();

            _runnerPositionContexts.Clear();

            _worldBuilt = false;
        }

        private void CreateNodeMarker(WorldNode node)
        {
            // Try entrance marker prefab (per-node), then generic prefab, then placeholder
            GameObject marker;
            bool isPrefab = false;

            if (_entranceMarkerPrefabs.TryGetValue(node.Id, out var entrancePrefab) && entrancePrefab != null)
            {
                marker = Instantiate(entrancePrefab);
                isPrefab = true;
            }
            else if (_nodeMarkerPrefab != null)
            {
                marker = Instantiate(_nodeMarkerPrefab);
                isPrefab = true;
            }
            else
            {
                // Placeholder: a flat cylinder as a "landing pad" for the node
                marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.transform.localScale = new Vector3(3f, 0.1f, 3f);

                // Color from node data
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material = CreatePlaceholderMaterial(new Color(node.ColorR, node.ColorG, node.ColorB));
            }

            marker.name = $"Node_{node.Id}";
            marker.transform.position = NodeWorldPosition(node);

            // Attach NodeMarker component for click-to-select
            var nodeMarkerComponent = marker.AddComponent<NodeMarker>();
            nodeMarkerComponent.Initialize(node.Id);

            // Ensure prefab has a collider for raycasting (placeholder cylinder gets one from CreatePrimitive)
            if (isPrefab && marker.GetComponentInChildren<Collider>() == null)
                marker.AddComponent<BoxCollider>();

            // Put nodes on their own physics layer for selective raycasting
            int nodeLayer = LayerMask.NameToLayer("Nodes");
            if (nodeLayer >= 0)
                SetLayerRecursive(marker, nodeLayer);

            // Add a floating label above the marker
            Vector3 markerPos = NodeWorldPosition(node);
            float labelHeight = isPrefab ? 4f : 2f;

            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(marker.transform, worldPositionStays: true);
            labelObj.transform.position = markerPos + new Vector3(0f, labelHeight, 0f);

            // Counteract parent scale so the label renders at world scale.
            // For placeholder cylinder (3, 0.1, 3) this matters; for prefabs the scale varies.
            Vector3 parentScale = marker.transform.lossyScale;
            labelObj.transform.localScale = new Vector3(
                1f / Mathf.Max(parentScale.x, 0.01f),
                1f / Mathf.Max(parentScale.y, 0.01f),
                1f / Mathf.Max(parentScale.z, 0.01f));

            var label = labelObj.AddComponent<TextMeshPro>();
            label.text = node.Name;
            label.fontSize = 6f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.yellow;
            label.rectTransform.sizeDelta = new Vector2(8f, 2f);

            _nodeMarkers[node.Id] = marker;
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

                UpdateRunnerVisualPosition(runner, visual);
            }

            // Billboard node labels — rotate only on Y axis so they stay upright
            if (Camera.main != null)
            {
                var camForward = Camera.main.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.001f)
                {
                    var billboardRot = Quaternion.LookRotation(camForward);
                    foreach (var kvp in _nodeMarkers)
                    {
                        var label = kvp.Value.GetComponentInChildren<TextMeshPro>();
                        if (label != null)
                            label.transform.rotation = billboardRot;
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
                visual.SnapToPosition(arrivalPos);
            }
            // Inside a node scene: always walk (spot changes, state changes, repositioning)
            else if (inNodeScene)
            {
                visual.WalkToPosition(worldPos);
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
            var fromNode = Sim.CurrentGameState.Map.GetNode(runner.Travel.FromNodeId);
            var toNode = Sim.CurrentGameState.Map.GetNode(runner.Travel.ToNodeId);

            if (fromNode != null && toNode != null)
            {
                // Use override start position if set (redirect — avoids visual snap)
                Vector3 from = runner.Travel.StartWorldX.HasValue
                    ? new Vector3(runner.Travel.StartWorldX.Value, 0f, runner.Travel.StartWorldZ.Value)
                    : NodeWorldPosition(fromNode);
                Vector3 to = NodeWorldPosition(toNode);
                Vector3 lerpedXZ = Vector3.Lerp(from, to, runner.Travel.Progress);

                // Sample terrain height at interpolated XZ position
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
        /// Position a runner inside a loaded node scene using gathering spots or spawn points.
        /// </summary>
        private Vector3 GetNodeScenePosition(Runner runner, NodeSceneRoot sceneRoot)
        {
            // Gathering: use the gathering spot for the runner's current gatherable index
            if (runner.State == RunnerState.Gathering && runner.Gathering != null)
            {
                int gatherableIndex = runner.Gathering.GatherableIndex;
                int runnerIndexInGroup = GetRunnerIndexInGatherableGroup(runner, gatherableIndex);
                return sceneRoot.GetGatheringPosition(gatherableIndex, runnerIndexInGroup) + RunnerYOffset;
            }

            // Idle, Depositing, or other states: use a spawn point
            int runnerIndex = GetRunnerIndexAtNode(runner);
            return sceneRoot.GetSpawnPosition(runnerIndex) + RunnerYOffset;
        }

        /// <summary>
        /// Get the arrival (spawn) position for a runner entering a node scene.
        /// Used on scene transitions so runners appear at the entrance and walk
        /// to their actual position (gathering spot, etc.) on the next frame.
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
                    return sceneRoot.GetSpawnPosition(runnerIndex) + RunnerYOffset;
                }
            }

            // Fallback: use the final position
            return GetRunnerWorldPosition(runner);
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

        // ─── Event Handlers ──────────────────────────────────────────

        private void OnRunnerCreated(RunnerCreated evt)
        {
            var runner = Sim.FindRunner(evt.RunnerId);
            if (runner != null)
                CreateRunnerVisual(runner);
        }

        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private void OnDestroy()
        {
            Sim?.Events?.Unsubscribe<RunnerCreated>(OnRunnerCreated);
        }
    }
}
