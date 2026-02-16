using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Manages the runner portrait bar at the top of the screen.
    /// Spawns one portrait per runner from a UXML template, handles click-to-select,
    /// refreshes state labels every tick, and groups portraits by node location.
    ///
    /// This is a plain C# class (not a MonoBehaviour) — idiomatic for UI Toolkit controllers.
    /// </summary>
    public class RunnerPortraitBarController
    {
        private readonly VisualElement _container;
        private readonly VisualTreeAsset _portraitTemplate;
        private readonly UIManager _uiManager;
        private readonly Dictionary<string, VisualElement> _portraits = new();
        private readonly List<VisualElement> _separators = new();

        // Track current grouping to avoid unnecessary rebuilds
        private string _lastGroupingKey = "";

        public RunnerPortraitBarController(
            VisualElement container,
            VisualTreeAsset portraitTemplate,
            UIManager uiManager)
        {
            _container = container;
            _portraitTemplate = portraitTemplate;
            _uiManager = uiManager;
        }

        /// <summary>
        /// Clone the portrait template for a runner. Does not add to container directly —
        /// RebuildGrouping handles positioning.
        /// </summary>
        public void AddPortrait(string runnerId)
        {
            if (_portraits.ContainsKey(runnerId)) return;

            var instance = _portraitTemplate.Instantiate();
            var portraitRoot = instance.Q("portrait-root");

            var runner = _uiManager.Simulation.FindRunner(runnerId);
            if (runner == null) return;

            instance.Q<Label>("portrait-name").text = runner.Name;
            instance.Q<Label>("portrait-state").text = FormatShortState(runner);

            portraitRoot.RegisterCallback<ClickEvent>(evt =>
            {
                _uiManager.SelectRunner(runnerId);
            });

            _portraits[runnerId] = instance;

            // Force a rebuild on next Refresh
            _lastGroupingKey = "";
        }

        /// <summary>
        /// Toggle the "selected" USS class on the correct portrait.
        /// </summary>
        public void SetSelectedRunner(string runnerId)
        {
            foreach (var kvp in _portraits)
            {
                var root = kvp.Value.Q("portrait-root");
                if (root == null) continue;

                if (kvp.Key == runnerId)
                    root.AddToClassList("selected");
                else
                    root.RemoveFromClassList("selected");
            }
        }

        /// <summary>
        /// Update state labels and regroup portraits by node. Called every tick by UIManager.
        /// </summary>
        public void Refresh()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            // Update labels and state colors
            foreach (var kvp in _portraits)
            {
                var runner = sim.FindRunner(kvp.Key);
                if (runner == null) continue;

                var stateLabel = kvp.Value.Q<Label>("portrait-state");
                if (stateLabel != null)
                {
                    stateLabel.text = FormatShortState(runner);
                    ApplyStateClass(stateLabel, runner.State);
                }
            }

            // Check if grouping changed
            RebuildGroupingIfNeeded(sim);
        }

        private void RebuildGroupingIfNeeded(GameSimulation sim)
        {
            // Build a grouping key: "nodeId:runnerId,runnerId|nodeId:runnerId"
            var runners = sim.CurrentGameState.Runners;
            var sorted = runners
                .Where(r => _portraits.ContainsKey(r.Id))
                .OrderBy(r => r.CurrentNodeId ?? "")
                .ThenBy(r => r.Name)
                .ToList();

            var keyParts = sorted.Select(r => $"{r.CurrentNodeId}:{r.Id}");
            string groupingKey = string.Join("|", keyParts);

            if (groupingKey == _lastGroupingKey) return;
            _lastGroupingKey = groupingKey;

            // Clear container
            _container.Clear();
            foreach (var sep in _separators)
                sep.RemoveFromHierarchy();
            _separators.Clear();

            // Re-add grouped by node with separators
            string lastNodeId = null;
            foreach (var runner in sorted)
            {
                if (lastNodeId != null && runner.CurrentNodeId != lastNodeId)
                {
                    // Add separator between node groups
                    var separator = CreateNodeSeparator(sim, runner.CurrentNodeId);
                    _container.Add(separator);
                    _separators.Add(separator);
                }
                else if (lastNodeId == null)
                {
                    // First group — add node label before first portrait
                    var separator = CreateNodeSeparator(sim, runner.CurrentNodeId);
                    _container.Add(separator);
                    _separators.Add(separator);
                }

                _container.Add(_portraits[runner.Id]);
                lastNodeId = runner.CurrentNodeId;
            }
        }

        private VisualElement CreateNodeSeparator(GameSimulation sim, string nodeId)
        {
            var separator = new VisualElement();
            separator.AddToClassList("portrait-node-separator");
            separator.pickingMode = PickingMode.Ignore;

            var nodeLabel = new Label(GetNodeDisplayName(sim, nodeId));
            nodeLabel.AddToClassList("portrait-node-label");
            nodeLabel.pickingMode = PickingMode.Ignore;
            separator.Add(nodeLabel);

            return separator;
        }

        private static string GetNodeDisplayName(GameSimulation sim, string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return "?";
            var node = sim.CurrentGameState.Map.GetNode(nodeId);
            return node?.Name ?? nodeId;
        }

        private static readonly string[] StateClasses =
            { "state-idle", "state-traveling", "state-gathering", "state-depositing" };

        private static void ApplyStateClass(VisualElement element, RunnerState state)
        {
            foreach (var cls in StateClasses)
                element.RemoveFromClassList(cls);

            string newClass = state switch
            {
                RunnerState.Idle => "state-idle",
                RunnerState.Traveling => "state-traveling",
                RunnerState.Gathering => "state-gathering",
                RunnerState.Depositing => "state-depositing",
                _ => null,
            };

            if (newClass != null)
                element.AddToClassList(newClass);
        }

        private static string FormatShortState(Runner runner)
        {
            return runner.State switch
            {
                RunnerState.Idle => "Idle",
                RunnerState.Traveling => $"-> {runner.Travel?.ToNodeId ?? "?"}",
                RunnerState.Gathering => "Gathering",
                RunnerState.Depositing => "Depositing",
                _ => runner.State.ToString(),
            };
        }
    }
}
