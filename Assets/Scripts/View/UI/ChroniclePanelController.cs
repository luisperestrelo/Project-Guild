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

    public enum ChronicleDirectionFilter
    {
        Everything,
        Incoming,
        Outgoing,
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
        private readonly Button _dirEverythingBtn;
        private readonly Button _dirIncomingBtn;
        private readonly Button _dirOutgoingBtn;
        private readonly TextField _searchField;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _entriesContainer;
        private readonly Label _newEntriesIndicator;

        // ─── State ───────────────────────────────────────
        private ChronicleFilter _activeFilter = ChronicleFilter.CurrentNode;
        private ChronicleDirectionFilter _directionFilter = ChronicleDirectionFilter.Everything;
        private string _searchText = "";
        private bool _userScrolledUp;
        private int _lastGenerationCounter;
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

            // Direction filters
            _dirEverythingBtn = root.Q<Button>("dir-everything");
            _dirIncomingBtn = root.Q<Button>("dir-incoming");
            _dirOutgoingBtn = root.Q<Button>("dir-outgoing");

            _dirEverythingBtn.clicked += () => SetDirectionFilter(ChronicleDirectionFilter.Everything);
            _dirIncomingBtn.clicked += () => SetDirectionFilter(ChronicleDirectionFilter.Incoming);
            _dirOutgoingBtn.clicked += () => SetDirectionFilter(ChronicleDirectionFilter.Outgoing);

            // Search bar
            _searchField = root.Q<TextField>("chronicle-search");
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchText = evt.newValue ?? "";
                _lastGenerationCounter = -1; // force rebuild
                Refresh();
            });

            // Scroll area
            _scrollView = root.Q<ScrollView>("chronicle-scroll");
            _entriesContainer = root.Q("chronicle-entries");

            // Track user scroll position for auto-scroll behavior
            _scrollView.verticalScroller.valueChanged += OnScrollChanged;

            // New entries indicator
            _newEntriesIndicator = root.Q<Label>("new-entries-indicator");
            _newEntriesIndicator.RegisterCallback<ClickEvent>(_ => ScrollToBottom());

            // Use default filter from player preferences
            var defaultFilter = ChronicleFilter.CurrentNode;
            var prefs = uiManager.Preferences;
            if (prefs != null)
            {
                switch (prefs.ChronicleDefaultScopeFilter)
                {
                    case "SelectedRunner": defaultFilter = ChronicleFilter.SelectedRunner; break;
                    case "Global": defaultFilter = ChronicleFilter.Global; break;
                }
            }
            SetFilter(defaultFilter);
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
            _lastGenerationCounter = -1;
        }

        // ─── Filter ──────────────────────────────────────

        private void SetFilter(ChronicleFilter filter)
        {
            _activeFilter = filter;
            SetFilterActive(_filterNodeBtn, filter == ChronicleFilter.CurrentNode);
            SetFilterActive(_filterRunnerBtn, filter == ChronicleFilter.SelectedRunner);
            SetFilterActive(_filterAllBtn, filter == ChronicleFilter.Global);
            _lastGenerationCounter = -1;
            Refresh();
        }

        private void SetDirectionFilter(ChronicleDirectionFilter filter)
        {
            _directionFilter = filter;
            SetFilterActive(_dirEverythingBtn, filter == ChronicleDirectionFilter.Everything);
            SetFilterActive(_dirIncomingBtn, filter == ChronicleDirectionFilter.Incoming);
            SetFilterActive(_dirOutgoingBtn, filter == ChronicleDirectionFilter.Outgoing);
            _lastGenerationCounter = -1;
            Refresh();
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
                _lastGenerationCounter = -1;
            }

            // Use generation counter to detect changes (works even when buffer is full)
            int currentGen = sim.Chronicle.GenerationCounter;
            if (currentGen == _lastGenerationCounter)
                return;

            bool hadNewEntries = _lastGenerationCounter >= 0;
            _lastGenerationCounter = currentGen;

            var entries = GetFilteredEntries(sim);
            int entryCount = entries.Count;
            int limit = System.Math.Min(entryCount, MaxDisplayedEntries);

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
            string dirTag = _directionFilter.ToString();
            string searchTag = _searchText;
            switch (_activeFilter)
            {
                case ChronicleFilter.CurrentNode:
                    var runner = selectedRunnerId != null
                        ? sim.CurrentGameState.Runners.Find(r => r.Id == selectedRunnerId)
                        : null;
                    return $"node:{runner?.CurrentNodeId}:{dirTag}:{searchTag}";
                case ChronicleFilter.SelectedRunner:
                    return $"runner:{selectedRunnerId}:{dirTag}:{searchTag}";
                case ChronicleFilter.Global:
                default:
                    return $"global:{dirTag}:{searchTag}";
            }
        }

        private List<ChronicleEntry> GetFilteredEntries(GameSimulation sim)
        {
            string selectedRunnerId = _uiManager.SelectedRunnerId;

            // Base scope filter
            List<ChronicleEntry> entries;
            switch (_activeFilter)
            {
                case ChronicleFilter.CurrentNode:
                    if (selectedRunnerId != null)
                    {
                        var runner = sim.CurrentGameState.Runners.Find(r => r.Id == selectedRunnerId);
                        if (runner?.CurrentNodeId != null)
                        {
                            entries = sim.Chronicle.GetForNode(runner.CurrentNodeId);
                            break;
                        }
                    }
                    entries = sim.Chronicle.GetAll();
                    break;

                case ChronicleFilter.SelectedRunner:
                    if (selectedRunnerId != null)
                    {
                        // For runner scope with direction, get all entries involving this runner
                        entries = GetEntriesInvolvingRunner(sim, selectedRunnerId);
                        break;
                    }
                    entries = sim.Chronicle.GetAll();
                    break;

                case ChronicleFilter.Global:
                default:
                    entries = sim.Chronicle.GetAll();
                    break;
            }

            // Direction filter (only meaningful when a runner is selected)
            if (_directionFilter != ChronicleDirectionFilter.Everything && selectedRunnerId != null)
            {
                entries = ApplyDirectionFilter(entries, selectedRunnerId);
            }

            // Search filter
            if (!string.IsNullOrEmpty(_searchText))
            {
                entries = ApplySearchFilter(entries);
            }

            return entries;
        }

        /// <summary>
        /// Get all entries where the runner is the actor OR the affected target.
        /// Returns most-recent-first like other query methods.
        /// </summary>
        private List<ChronicleEntry> GetEntriesInvolvingRunner(GameSimulation sim, string runnerId)
        {
            if (_directionFilter == ChronicleDirectionFilter.Everything)
            {
                // Need entries where RunnerId OR AffectedRunnerId matches
                var all = sim.Chronicle.Entries;
                var result = new List<ChronicleEntry>();
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var e = all[i];
                    if (e.RunnerId == runnerId || e.AffectedRunnerId == runnerId)
                        result.Add(e);
                }
                return result;
            }
            // For directional filtering, get the broader set and let ApplyDirectionFilter narrow it
            var allEntries = sim.Chronicle.Entries;
            var broad = new List<ChronicleEntry>();
            for (int i = allEntries.Count - 1; i >= 0; i--)
            {
                var e = allEntries[i];
                if (e.RunnerId == runnerId || e.AffectedRunnerId == runnerId)
                    broad.Add(e);
            }
            return broad;
        }

        private List<ChronicleEntry> ApplyDirectionFilter(List<ChronicleEntry> entries, string runnerId)
        {
            var result = new List<ChronicleEntry>();
            foreach (var e in entries)
            {
                switch (_directionFilter)
                {
                    case ChronicleDirectionFilter.Incoming:
                        // Things that happened TO this runner:
                        // 1. Entry where RunnerId matches and Direction is Incoming (took damage, died)
                        // 2. Entry where AffectedRunnerId matches and Direction is Outgoing
                        //    (someone else healed this runner)
                        if ((e.RunnerId == runnerId && e.Direction == ChronicleDirection.Incoming)
                            || (e.AffectedRunnerId == runnerId && e.Direction == ChronicleDirection.Outgoing))
                            result.Add(e);
                        break;

                    case ChronicleDirectionFilter.Outgoing:
                        // Things this runner DID: attacks, heals, kills
                        if (e.RunnerId == runnerId && e.Direction == ChronicleDirection.Outgoing)
                            result.Add(e);
                        break;
                }
            }
            return result;
        }

        private List<ChronicleEntry> ApplySearchFilter(List<ChronicleEntry> entries)
        {
            var result = new List<ChronicleEntry>();
            string search = _searchText.ToLowerInvariant();
            foreach (var e in entries)
            {
                if (e.Text != null && e.Text.ToLowerInvariant().Contains(search))
                    result.Add(e);
            }
            return result;
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
