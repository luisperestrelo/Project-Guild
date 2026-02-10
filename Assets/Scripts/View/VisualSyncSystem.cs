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

        // Runtime tracking
        private readonly Dictionary<string, RunnerVisual> _runnerVisuals = new();
        private readonly Dictionary<string, GameObject> _nodeMarkers = new();
        private bool _worldBuilt;

        private GameSimulation Sim => _simulationRunner?.Simulation;

        public RunnerVisual GetRunnerVisual(string runnerId)
        {
            return _runnerVisuals.TryGetValue(runnerId, out var visual) ? visual : null;
        }

        private void OnEnable()
        {
            if (_simulationRunner == null)
                _simulationRunner = FindAnyObjectByType<SimulationRunner>();
        }

        /// <summary>
        /// Call after StartNewGame or LoadGame to spawn the visual world.
        /// </summary>
        public void BuildWorld()
        {
            ClearWorld();

            if (Sim?.State?.Map == null) return;

            // Create node markers
            foreach (var node in Sim.State.Map.Nodes)
            {
                CreateNodeMarker(node);
            }

            // Create runner visuals
            foreach (var runner in Sim.State.Runners)
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

            _worldBuilt = false;
        }

        private void CreateNodeMarker(WorldNode node)
        {
            GameObject marker;
            if (_nodeMarkerPrefab != null)
            {
                marker = Instantiate(_nodeMarkerPrefab);
            }
            else
            {
                // Placeholder: a flat cylinder as a "landing pad" for the node
                marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.transform.localScale = new Vector3(3f, 0.1f, 3f);

                // Color by node type
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = GetNodeColor(node.Type);
                }
            }

            marker.name = $"Node_{node.Id}";
            marker.transform.position = NodeWorldPosition(node);

            // Add a floating label — parented for cleanup but using world position
            // (can't use localPosition because the cylinder's Y scale is 0.1, which squishes children)
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(marker.transform, worldPositionStays: true);
            labelObj.transform.position = NodeWorldPosition(node) + new Vector3(0f, 2f, 0f);
            labelObj.transform.localScale = new Vector3(1f / 3f, 1f / 0.1f, 1f / 3f); // counteract parent scale
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
            }

            var visual = obj.AddComponent<RunnerVisual>();
            Vector3 startPos = GetRunnerWorldPosition(runner);
            visual.Initialize(runner.Id, runner.Name, startPos);

            _runnerVisuals[runner.Id] = visual;
        }

        private void LateUpdate()
        {
            if (!_worldBuilt || Sim == null) return;

            // Update all runner visual positions
            foreach (var runner in Sim.State.Runners)
            {
                if (!_runnerVisuals.TryGetValue(runner.Id, out var visual)) continue;

                Vector3 worldPos = GetRunnerWorldPosition(runner);
                visual.SetTargetPosition(worldPos);
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

        /// <summary>
        /// Calculate the world position of a runner based on their simulation state.
        /// If traveling, interpolates between the from/to node positions.
        /// TODO: Remove this off-set once we have proper visuals, just make the visuals of the runner prefab be offset.
        /// </summary>
        private static readonly Vector3 RunnerYOffset = new(0f, 1f, 0f);

        private Vector3 GetRunnerWorldPosition(Runner runner)
        {
            if (runner.State == RunnerState.Traveling && runner.Travel != null)
            {
                var fromNode = Sim.State.Map.GetNode(runner.Travel.FromNodeId);
                var toNode = Sim.State.Map.GetNode(runner.Travel.ToNodeId);

                if (fromNode != null && toNode != null)
                {
                    Vector3 from = NodeWorldPosition(fromNode);
                    Vector3 to = NodeWorldPosition(toNode);
                    return Vector3.Lerp(from, to, runner.Travel.Progress) + RunnerYOffset;
                }
            }

            // At a node — spread multiple idle runners in a small circle so they don't stack
            var currentNode = Sim.State.Map.GetNode(runner.CurrentNodeId);
            if (currentNode != null)
            {
                int idleIndex = 0;
                foreach (var r in Sim.State.Runners)
                {
                    if (r.Id == runner.Id) break;
                    if (r.CurrentNodeId == runner.CurrentNodeId && r.State != RunnerState.Traveling)
                        idleIndex++;
                }

                Vector3 spread = Vector3.zero;
                if (idleIndex > 0)
                {
                    float angle = idleIndex * 2.094f; // ~120 degrees apart (2*PI/3)
                    spread = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.2f;
                }

                return NodeWorldPosition(currentNode) + RunnerYOffset + spread;
            }

            return Vector3.up; // Fallback
        }

        private Vector3 NodeWorldPosition(WorldNode node)
        {
            return new Vector3(node.WorldX, 0f, node.WorldZ);
        }

        private Color GetNodeColor(NodeType type)
        {
            return type switch
            {
                NodeType.Hub => new Color(0.2f, 0.6f, 1f),          // Blue
                NodeType.GatheringMine => new Color(0.6f, 0.4f, 0.2f), // Brown
                NodeType.GatheringForest => new Color(0.2f, 0.7f, 0.2f), // Green
                NodeType.GatheringWater => new Color(0.3f, 0.7f, 0.9f),  // Light blue
                NodeType.GatheringHerbs => new Color(0.5f, 0.8f, 0.3f),  // Yellow-green
                NodeType.MobZone => new Color(0.8f, 0.2f, 0.2f),         // Red
                NodeType.Raid => new Color(0.6f, 0.1f, 0.6f),            // Purple
                _ => Color.gray,
            };
        }

        // ─── Event Handlers ──────────────────────────────────────────

        private void OnRunnerCreated(RunnerCreated evt)
        {
            var runner = Sim.FindRunner(evt.RunnerId);
            if (runner != null)
                CreateRunnerVisual(runner);
        }

        private void OnDestroy()
        {
            Sim?.Events.Unsubscribe<RunnerCreated>(OnRunnerCreated);
        }
    }
}
