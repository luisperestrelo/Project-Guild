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

            if (Sim?.CurrentGameState?.Map == null) return;

            // Create node markers
            foreach (var node in Sim.CurrentGameState.Map.Nodes)
            {
                CreateNodeMarker(node);
            }

            // Create bank marker near hub
            CreateBankMarker();

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

                // Color from node data
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(node.ColorR, node.ColorG, node.ColorB);
                }
            }

            marker.name = $"Node_{node.Id}";
            marker.transform.position = NodeWorldPosition(node);

            // Attach NodeMarker component for click-to-select
            var nodeMarkerComponent = marker.AddComponent<NodeMarker>();
            nodeMarkerComponent.Initialize(node.Id);

            // Put nodes on their own physics layer for selective raycasting
            int nodeLayer = LayerMask.NameToLayer("Nodes");
            if (nodeLayer >= 0)
                SetLayerRecursive(marker, nodeLayer);

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

        private void CreateBankMarker()
        {
            var hubId = Sim.CurrentGameState.Map?.HubNodeId;
            if (hubId == null) return;
            var hub = Sim.CurrentGameState.Map.GetNode(hubId);
            if (hub == null) return;

            var bankObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bankObj.name = "BankMarker";
            bankObj.transform.localScale = new Vector3(2f, 2f, 2f);
            bankObj.transform.position = new Vector3(hub.WorldX + 4f, 1f, hub.WorldZ);

            var renderer = bankObj.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(0.7f, 0.6f, 0.2f);

            bankObj.AddComponent<BankMarker>();

            int bankLayer = LayerMask.NameToLayer("Bank");
            if (bankLayer >= 0)
                SetLayerRecursive(bankObj, bankLayer);

            // Floating label
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(bankObj.transform, worldPositionStays: true);
            labelObj.transform.position = bankObj.transform.position + new Vector3(0f, 2f, 0f);
            labelObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            var label = labelObj.AddComponent<TMPro.TextMeshPro>();
            label.text = "Bank";
            label.fontSize = 6f;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.color = Color.yellow;
            label.rectTransform.sizeDelta = new Vector2(4f, 2f);
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

            // Update all runner visual positions
            foreach (var runner in Sim.CurrentGameState.Runners)
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
                var fromNode = Sim.CurrentGameState.Map.GetNode(runner.Travel.FromNodeId);
                var toNode = Sim.CurrentGameState.Map.GetNode(runner.Travel.ToNodeId);

                if (fromNode != null && toNode != null)
                {
                    // Use override start position if set (redirect — avoids visual snap)
                    Vector3 from = runner.Travel.StartWorldX.HasValue
                        ? new Vector3(runner.Travel.StartWorldX.Value, 0f, runner.Travel.StartWorldZ.Value)
                        : NodeWorldPosition(fromNode);
                    Vector3 to = NodeWorldPosition(toNode);
                    return Vector3.Lerp(from, to, runner.Travel.Progress) + RunnerYOffset;
                }
            }

            // At a node — spread multiple idle runners in a small circle so they don't stack
            var currentNode = Sim.CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            if (currentNode != null)
            {
                int idleIndex = 0;
                foreach (var r in Sim.CurrentGameState.Runners)
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
