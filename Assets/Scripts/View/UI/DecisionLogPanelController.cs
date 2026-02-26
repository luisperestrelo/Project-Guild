using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    public enum DecisionLogScopeFilter
    {
        CurrentNode,
        SelectedRunner,
        All,
    }

    /// <summary>
    /// Controls the Decision Log content panel.
    /// Reads from MacroDecisionLog and MicroDecisionLog and displays automation
    /// decision entries with scope filtering (node/runner/all) and layer filtering
    /// (all/macro/micro). Collapse/expand is owned by the parent LogPanelContainerController.
    /// Plain C# class (not MonoBehaviour).
    /// </summary>
    public class DecisionLogPanelController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // ─── Header elements ────────────────────────────────
        private readonly Button _scopeNodeBtn;
        private readonly Button _scopeRunnerBtn;
        private readonly Button _scopeAllBtn;
        private readonly Button _layerAllBtn;
        private readonly Button _layerMacroBtn;
        private readonly Button _layerMicroBtn;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _entriesContainer;
        private readonly Label _newEntriesIndicator;

        // ─── State ───────────────────────────────────────
        private DecisionLogScopeFilter _activeScopeFilter = DecisionLogScopeFilter.SelectedRunner;
        private DecisionLayer? _activeLayerFilter; // null = all
        private bool _userScrolledUp;
        private int _lastMacroGeneration;
        private int _lastMicroGeneration;
        private string _lastFilterTarget;

        private const int MaxDisplayedEntries = 100;

        // Row cache
        private readonly List<(VisualElement row, Label textLabel)> _rowCache = new();

        // Unread tracking for container badge
        public int NewEntriesSinceLastView { get; private set; }

        public DecisionLogPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

            // Scope filters
            _scopeNodeBtn = root.Q<Button>("scope-node");
            _scopeRunnerBtn = root.Q<Button>("scope-runner");
            _scopeAllBtn = root.Q<Button>("scope-all");

            _scopeNodeBtn.clicked += () => SetScopeFilter(DecisionLogScopeFilter.CurrentNode);
            _scopeRunnerBtn.clicked += () => SetScopeFilter(DecisionLogScopeFilter.SelectedRunner);
            _scopeAllBtn.clicked += () => SetScopeFilter(DecisionLogScopeFilter.All);

            // Layer filters
            _layerAllBtn = root.Q<Button>("layer-all");
            _layerMacroBtn = root.Q<Button>("layer-macro");
            _layerMicroBtn = root.Q<Button>("layer-micro");

            _layerAllBtn.clicked += () => SetLayerFilter(null);
            _layerMacroBtn.clicked += () => SetLayerFilter(DecisionLayer.Macro);
            _layerMicroBtn.clicked += () => SetLayerFilter(DecisionLayer.Micro);

            // Scroll area
            _scrollView = root.Q<ScrollView>("decision-log-scroll");
            _entriesContainer = root.Q("decision-log-entries");

            _scrollView.verticalScroller.valueChanged += OnScrollChanged;

            // New entries indicator
            _newEntriesIndicator = root.Q<Label>("new-entries-indicator");
            _newEntriesIndicator.RegisterCallback<ClickEvent>(_ => ScrollToBottom());

            // Use default scope filter from player preferences
            var defaultScope = DecisionLogScopeFilter.SelectedRunner;
            var prefs = uiManager.Preferences;
            if (prefs != null)
            {
                switch (prefs.DecisionLogDefaultScopeFilter)
                {
                    case "CurrentNode": defaultScope = DecisionLogScopeFilter.CurrentNode; break;
                    case "All": defaultScope = DecisionLogScopeFilter.All; break;
                }
            }
            SetScopeFilter(defaultScope);
            SetLayerFilter(null);
        }

        // ─── Unread API (used by LogPanelContainerController) ───

        public void NotifyNewEntries(int count)
        {
            NewEntriesSinceLastView += count;
        }

        public void ResetNewEntries()
        {
            NewEntriesSinceLastView = 0;
            ForceRefreshOnNextTick();
        }

        // ─── Scope Filter ────────────────────────────────

        private void SetScopeFilter(DecisionLogScopeFilter filter)
        {
            _activeScopeFilter = filter;
            SetFilterActive(_scopeNodeBtn, filter == DecisionLogScopeFilter.CurrentNode);
            SetFilterActive(_scopeRunnerBtn, filter == DecisionLogScopeFilter.SelectedRunner);
            SetFilterActive(_scopeAllBtn, filter == DecisionLogScopeFilter.All);
            ForceRefreshOnNextTick();
        }

        // ─── Layer Filter ────────────────────────────────

        private void SetLayerFilter(DecisionLayer? layer)
        {
            _activeLayerFilter = layer;
            SetFilterActive(_layerAllBtn, layer == null);
            SetFilterActive(_layerMacroBtn, layer == DecisionLayer.Macro);
            SetFilterActive(_layerMicroBtn, layer == DecisionLayer.Micro);
            ForceRefreshOnNextTick();
        }

        private void ForceRefreshOnNextTick()
        {
            _lastMacroGeneration = -1;
            _lastMicroGeneration = -1;
        }

        private static void SetFilterActive(Button btn, bool active)
        {
            if (active)
                btn.AddToClassList("filter-active");
            else
                btn.RemoveFromClassList("filter-active");
        }

        // ─── Scroll ──────────────────────────────────────

        private void OnScrollChanged(float value)
        {
            float maxScroll = _scrollView.verticalScroller.highValue;
            _userScrolledUp = maxScroll > 0 && value < maxScroll - 20;

            if (!_userScrolledUp)
                _newEntriesIndicator.style.display = DisplayStyle.None;
        }

        private void ScrollToBottom()
        {
            _userScrolledUp = false;
            _newEntriesIndicator.style.display = DisplayStyle.None;
            _scrollView.scrollOffset = new Vector2(0, float.MaxValue);
        }

        // ─── Refresh (called every tick by container) ────

        public void Refresh()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            string filterTarget = GetFilterTarget(sim);
            if (filterTarget != _lastFilterTarget)
            {
                _lastFilterTarget = filterTarget;
                ForceRefreshOnNextTick();
            }

            // Check generation counters — detects changes even when buffer is full
            var macroLog = sim.CurrentGameState.MacroDecisionLog;
            var microLog = sim.CurrentGameState.MicroDecisionLog;
            int macroGen = macroLog.GenerationCounter;
            int microGen = microLog.GenerationCounter;

            if (macroGen == _lastMacroGeneration && microGen == _lastMicroGeneration)
                return;

            bool hadNewEntries = _lastMacroGeneration >= 0 || _lastMicroGeneration >= 0;
            _lastMacroGeneration = macroGen;
            _lastMicroGeneration = microGen;

            var entries = GetFilteredEntries(sim);
            int limit = System.Math.Min(entries.Count, MaxDisplayedEntries);
            RebuildEntries(entries, limit);

            if (hadNewEntries && _userScrolledUp)
            {
                _newEntriesIndicator.text = "New entries below";
                _newEntriesIndicator.style.display = DisplayStyle.Flex;
            }

            if (!_userScrolledUp)
            {
                _scrollView.schedule.Execute(ScrollToBottom);
            }
        }

        /// <summary>
        /// Returns the combined generation counter from both logs.
        /// Used by the container for unread tracking when this tab is not active.
        /// </summary>
        public int GetTotalEntryCount()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return 0;
            return sim.CurrentGameState.MacroDecisionLog.Entries.Count
                 + sim.CurrentGameState.MicroDecisionLog.Entries.Count;
        }

        private string GetFilterTarget(GameSimulation sim)
        {
            string selectedRunnerId = _uiManager.SelectedRunnerId;
            switch (_activeScopeFilter)
            {
                case DecisionLogScopeFilter.CurrentNode:
                    var runner = selectedRunnerId != null
                        ? sim.CurrentGameState.Runners.Find(r => r.Id == selectedRunnerId)
                        : null;
                    return $"node:{runner?.CurrentNodeId}:{_activeLayerFilter}";
                case DecisionLogScopeFilter.SelectedRunner:
                    return $"runner:{selectedRunnerId}:{_activeLayerFilter}";
                case DecisionLogScopeFilter.All:
                default:
                    return $"all:{_activeLayerFilter}";
            }
        }

        private List<DecisionLogEntry> GetFilteredEntries(GameSimulation sim)
        {
            string selectedRunnerId = _uiManager.SelectedRunnerId;
            var macroLog = sim.CurrentGameState.MacroDecisionLog;
            var microLog = sim.CurrentGameState.MicroDecisionLog;

            // If filtering to a specific layer, only query that log
            if (_activeLayerFilter == DecisionLayer.Macro)
                return GetFilteredFromLog(macroLog, selectedRunnerId, sim);
            if (_activeLayerFilter == DecisionLayer.Micro)
                return GetFilteredFromLog(microLog, selectedRunnerId, sim);

            // All layers — merge both logs
            var macroEntries = GetFilteredFromLog(macroLog, selectedRunnerId, sim);
            var microEntries = GetFilteredFromLog(microLog, selectedRunnerId, sim);
            return MergeMostRecentFirst(macroEntries, microEntries);
        }

        private List<DecisionLogEntry> GetFilteredFromLog(
            DecisionLog log, string selectedRunnerId, GameSimulation sim)
        {
            switch (_activeScopeFilter)
            {
                case DecisionLogScopeFilter.CurrentNode:
                    if (selectedRunnerId != null)
                    {
                        var runner = sim.CurrentGameState.Runners.Find(r => r.Id == selectedRunnerId);
                        if (runner?.CurrentNodeId != null)
                            return log.GetForNode(runner.CurrentNodeId);
                    }
                    return log.GetAll();

                case DecisionLogScopeFilter.SelectedRunner:
                    if (selectedRunnerId != null)
                        return log.GetForRunner(selectedRunnerId);
                    return log.GetAll();

                case DecisionLogScopeFilter.All:
                default:
                    return log.GetAll();
            }
        }

        /// <summary>
        /// Merge two most-recent-first lists into one most-recent-first list.
        /// </summary>
        private static List<DecisionLogEntry> MergeMostRecentFirst(
            List<DecisionLogEntry> a, List<DecisionLogEntry> b)
        {
            var merged = new List<DecisionLogEntry>(a.Count + b.Count);
            int ia = 0, ib = 0;
            while (ia < a.Count && ib < b.Count)
            {
                if (a[ia].TickNumber >= b[ib].TickNumber)
                    merged.Add(a[ia++]);
                else
                    merged.Add(b[ib++]);
            }
            while (ia < a.Count) merged.Add(a[ia++]);
            while (ib < b.Count) merged.Add(b[ib++]);
            return merged;
        }

        private void RebuildEntries(List<DecisionLogEntry> entries, int limit)
        {
            while (_rowCache.Count < limit)
            {
                var row = new VisualElement();
                row.AddToClassList("decision-log-entry");

                var textLabel = new Label();
                textLabel.AddToClassList("entry-text");
                row.Add(textLabel);

                // Right-click: copy and logbook
                _uiManager.RegisterContextMenu(row, () => new List<(string, System.Action)>
                {
                    ("Copy Text", () => GUIUtility.systemCopyBuffer = textLabel.text),
                    ("Copy to Logbook", () => _uiManager.AppendToLogbook(textLabel.text))
                });

                _rowCache.Add((row, textLabel));
            }

            _entriesContainer.Clear();

            // Entries are most-recent-first; display oldest-first (chronological)
            for (int i = limit - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var (row, textLabel) = _rowCache[limit - 1 - i];

                // Format: "[timestamp] [LAYER] RunnerName: Condition -> Action"
                string timestamp = TimeFormatHelper.FormatElapsedTime(entry.GameTime);
                string layerTag = entry.Layer == DecisionLayer.Macro ? "MACRO" : "MICRO";
                string deferred = entry.WasDeferred ? " (deferred)" : "";
                textLabel.text = $"[{timestamp}] [{layerTag}] {entry.RunnerName}: {entry.ConditionSnapshot} \u2192 {entry.ActionDetail}{deferred}";

                // Update layer class
                row.RemoveFromClassList("entry-macro");
                row.RemoveFromClassList("entry-micro");

                if (entry.Layer == DecisionLayer.Macro)
                    row.AddToClassList("entry-macro");
                else
                    row.AddToClassList("entry-micro");

                _entriesContainer.Add(row);
            }
        }
    }
}
