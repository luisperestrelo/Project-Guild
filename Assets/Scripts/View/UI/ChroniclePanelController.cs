using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    public enum ChronicleFilter
    {
        CurrentNode,
        SelectedRunner,
        Global,
    }

    /// <summary>
    /// Controls the player-facing Chronicle content panel.
    /// Reads from ChronicleService and displays human-readable event entries
    /// with category color-coding, filtering, auto-scroll.
    /// Collapse/expand is owned by the parent LogPanelContainerController.
    /// Plain C# class (not MonoBehaviour).
    /// </summary>
    public class ChroniclePanelController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // ─── Header elements ────────────────────────────────
        private readonly Button _filterNodeBtn;
        private readonly Button _filterRunnerBtn;
        private readonly Button _filterAllBtn;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _entriesContainer;
        private readonly Label _newEntriesIndicator;

        // ─── State ───────────────────────────────────────
        private ChronicleFilter _activeFilter = ChronicleFilter.CurrentNode;
        private bool _userScrolledUp;
        private int _lastEntryCount;
        private int _lastRepeatSum;
        private string _lastFilterTarget;

        private const int MaxDisplayedEntries = 100;

        // Row cache: reuse VisualElements, rebuild only when structure changes
        private readonly List<(VisualElement row, Label textLabel, Label repeatLabel)> _rowCache = new();

        // Unread tracking for container badge
        public int NewEntriesSinceLastView { get; private set; }

        public ChroniclePanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

            // Header
            _filterNodeBtn = root.Q<Button>("filter-node");
            _filterRunnerBtn = root.Q<Button>("filter-runner");
            _filterAllBtn = root.Q<Button>("filter-all");

            _filterNodeBtn.clicked += () => SetFilter(ChronicleFilter.CurrentNode);
            _filterRunnerBtn.clicked += () => SetFilter(ChronicleFilter.SelectedRunner);
            _filterAllBtn.clicked += () => SetFilter(ChronicleFilter.Global);

            // Scroll area
            _scrollView = root.Q<ScrollView>("chronicle-scroll");
            _entriesContainer = root.Q("chronicle-entries");

            // Track user scroll position for auto-scroll behavior
            _scrollView.verticalScroller.valueChanged += OnScrollChanged;

            // New entries indicator
            _newEntriesIndicator = root.Q<Label>("new-entries-indicator");
            _newEntriesIndicator.RegisterCallback<ClickEvent>(_ => ScrollToBottom());

            SetFilter(ChronicleFilter.CurrentNode);
        }

        // ─── Unread API (used by LogPanelContainerController) ───

        public void NotifyNewEntries(int count)
        {
            NewEntriesSinceLastView += count;
        }

        public void ResetNewEntries()
        {
            NewEntriesSinceLastView = 0;
            // Force refresh on next tick so content is up to date
            _lastEntryCount = -1;
            _lastRepeatSum = -1;
        }

        // ─── Filter ──────────────────────────────────────

        private void SetFilter(ChronicleFilter filter)
        {
            _activeFilter = filter;
            SetFilterActive(_filterNodeBtn, filter == ChronicleFilter.CurrentNode);
            SetFilterActive(_filterRunnerBtn, filter == ChronicleFilter.SelectedRunner);
            SetFilterActive(_filterAllBtn, filter == ChronicleFilter.Global);
            // Force full rebuild on filter change
            _lastEntryCount = -1;
            _lastRepeatSum = -1;
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
            // Consider "near bottom" if within 20px of max
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

            // Detect filter context change (different runner or different node)
            string filterTarget = GetFilterTarget(sim);
            if (filterTarget != _lastFilterTarget)
            {
                _lastFilterTarget = filterTarget;
                _lastEntryCount = -1;
                _lastRepeatSum = -1;
            }

            var entries = GetFilteredEntries(sim);

            // Quick shape check: skip rebuild if nothing changed
            int entryCount = entries.Count;
            int repeatSum = 0;
            int limit = System.Math.Min(entryCount, MaxDisplayedEntries);
            for (int i = 0; i < limit; i++)
                repeatSum += entries[i].RepeatCount;

            if (entryCount == _lastEntryCount && repeatSum == _lastRepeatSum)
                return;

            bool hadNewEntries = entryCount > _lastEntryCount && _lastEntryCount >= 0;
            _lastEntryCount = entryCount;
            _lastRepeatSum = repeatSum;

            // Entries come most-recent-first from the service; we want oldest-at-top
            RebuildEntries(entries, limit);

            if (hadNewEntries && _userScrolledUp)
            {
                _newEntriesIndicator.text = "New entries below";
                _newEntriesIndicator.style.display = DisplayStyle.Flex;
            }

            if (!_userScrolledUp)
            {
                // Schedule scroll to bottom after layout
                _scrollView.schedule.Execute(ScrollToBottom);
            }
        }

        /// <summary>
        /// Returns the total entry count from the service. Used by the container
        /// for unread tracking when this tab is not active.
        /// </summary>
        public int GetTotalEntryCount()
        {
            var sim = _uiManager.Simulation;
            return sim?.Chronicle.Entries.Count ?? 0;
        }

        private string GetFilterTarget(GameSimulation sim)
        {
            string selectedRunnerId = _uiManager.SelectedRunnerId;
            switch (_activeFilter)
            {
                case ChronicleFilter.CurrentNode:
                    var runner = selectedRunnerId != null
                        ? sim.CurrentGameState.Runners.Find(r => r.Id == selectedRunnerId)
                        : null;
                    return $"node:{runner?.CurrentNodeId}";
                case ChronicleFilter.SelectedRunner:
                    return $"runner:{selectedRunnerId}";
                case ChronicleFilter.Global:
                default:
                    return "global";
            }
        }

        private List<ChronicleEntry> GetFilteredEntries(GameSimulation sim)
        {
            string selectedRunnerId = _uiManager.SelectedRunnerId;

            switch (_activeFilter)
            {
                case ChronicleFilter.CurrentNode:
                    if (selectedRunnerId != null)
                    {
                        var runner = sim.CurrentGameState.Runners.Find(r => r.Id == selectedRunnerId);
                        if (runner?.CurrentNodeId != null)
                            return sim.Chronicle.GetForNode(runner.CurrentNodeId);
                    }
                    return sim.Chronicle.GetAll();

                case ChronicleFilter.SelectedRunner:
                    if (selectedRunnerId != null)
                        return sim.Chronicle.GetForRunner(selectedRunnerId);
                    return sim.Chronicle.GetAll();

                case ChronicleFilter.Global:
                default:
                    return sim.Chronicle.GetAll();
            }
        }

        private void RebuildEntries(List<ChronicleEntry> entries, int limit)
        {
            // Ensure we have enough rows in the cache
            while (_rowCache.Count < limit)
            {
                var row = new VisualElement();
                row.AddToClassList("chronicle-entry");

                var textLabel = new Label();
                textLabel.AddToClassList("entry-text");
                row.Add(textLabel);

                var repeatLabel = new Label();
                repeatLabel.AddToClassList("entry-repeat");
                row.Add(repeatLabel);

                // Right-click: copy and logbook
                _uiManager.RegisterContextMenu(row, () => new List<(string, System.Action)>
                {
                    ("Copy Text", () => GUIUtility.systemCopyBuffer = textLabel.text),
                    ("Copy to Logbook", () => _uiManager.AppendToLogbook(textLabel.text))
                });

                _rowCache.Add((row, textLabel, repeatLabel));
            }

            _entriesContainer.Clear();

            // Entries are most-recent-first; display oldest-first (chronological)
            for (int i = limit - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var (row, textLabel, repeatLabel) = _rowCache[limit - 1 - i];

                // Update text with timestamp
                string timestamp = TimeFormatHelper.FormatElapsedTime(entry.GameTime);
                textLabel.text = $"[{timestamp}] {entry.Text}";

                // Update repeat count
                if (entry.RepeatCount > 1)
                {
                    repeatLabel.text = $"(x{entry.RepeatCount})";
                    repeatLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    repeatLabel.style.display = DisplayStyle.None;
                }

                // Update category class
                row.RemoveFromClassList("entry-warning");
                row.RemoveFromClassList("entry-production");
                row.RemoveFromClassList("entry-statechange");
                row.RemoveFromClassList("entry-automation");
                row.RemoveFromClassList("entry-lifecycle");

                switch (entry.Category)
                {
                    case EventCategory.Warning:
                        row.AddToClassList("entry-warning");
                        break;
                    case EventCategory.Production:
                        row.AddToClassList("entry-production");
                        break;
                    case EventCategory.StateChange:
                        row.AddToClassList("entry-statechange");
                        break;
                    case EventCategory.Automation:
                        row.AddToClassList("entry-automation");
                        break;
                    case EventCategory.Lifecycle:
                        row.AddToClassList("entry-lifecycle");
                        break;
                }

                _entriesContainer.Add(row);
            }
        }
    }
}
