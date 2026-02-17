using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Manages the runner portrait bar at the top of the screen.
    /// Groups portraits by node (hub pinned left, other nodes in first-seen order).
    /// Within each group, runners appear in player's preferred order (drag-to-reorder).
    /// Default order is creation order. Refreshes state labels every tick.
    ///
    /// Plain C# class (not MonoBehaviour) — idiomatic for UI Toolkit controllers.
    /// </summary>
    public class RunnerPortraitBarController
    {
        private const float DragThresholdPixels = 5f;

        private readonly VisualElement _container;
        private readonly VisualTreeAsset _portraitTemplate;
        private readonly UIManager _uiManager;

        // Portrait instances keyed by runner ID
        private readonly Dictionary<string, VisualElement> _portraits = new();
        private readonly List<VisualElement> _separators = new();

        // Runner IDs in player's preferred order. Modified by drag-to-reorder.
        // Default = creation order (order AddPortrait was called).
        private readonly List<string> _runnerSortOrder = new();

        // Node IDs in first-seen order. Hub is always index 0.
        private readonly List<string> _groupFirstSeenOrder = new();

        // Grouping cache to avoid unnecessary DOM rebuilds
        private string _lastGroupingKey = "";

        // ─── Drag state ──────────────────────────────────
        private bool _isDragPending;   // PointerDown fired, waiting for threshold
        private bool _isDragActive;    // Threshold exceeded, actively dragging
        private string _dragRunnerId;
        private Vector2 _dragStartPosition;
        private Vector2 _lastDragPosition;
        private int _dragPointerId = -1;
        private VisualElement _dragCaptureElement;
        private float _dragGhostHalfWidth;
        private float _dragGhostHalfHeight;
        private VisualElement _dragGhost;
        private VisualElement _dropIndicator;

        public RunnerPortraitBarController(
            VisualElement container,
            VisualTreeAsset portraitTemplate,
            UIManager uiManager)
        {
            _container = container;
            _portraitTemplate = portraitTemplate;
            _uiManager = uiManager;
        }

        // ─── Public API ──────────────────────────────────

        public void AddPortrait(string runnerId)
        {
            if (_portraits.ContainsKey(runnerId)) return;

            var instance = _portraitTemplate.Instantiate();
            var portraitRoot = instance.Q("portrait-root");

            var runner = _uiManager.Simulation.FindRunner(runnerId);
            if (runner == null) return;

            instance.Q<Label>("portrait-name").text = runner.Name;
            instance.Q<Label>("portrait-state").text = FormatShortState(runner);

            // Pointer events handle both click-to-select and drag-to-reorder.
            // We capture on PointerDown, then decide in PointerUp whether it was
            // a click (< threshold) or a drag (>= threshold).
            portraitRoot.RegisterCallback<PointerDownEvent>(evt =>
                OnPortraitPointerDown(evt, runnerId, portraitRoot));
            portraitRoot.RegisterCallback<PointerMoveEvent>(evt =>
                OnPortraitPointerMove(evt));
            portraitRoot.RegisterCallback<PointerUpEvent>(evt =>
                OnPortraitPointerUp(evt, runnerId));
            portraitRoot.RegisterCallback<PointerCaptureOutEvent>(evt =>
                OnPointerCaptureOut());

            // Warning badge tooltip
            var warningBadge = instance.Q("portrait-warning-badge");
            if (warningBadge != null)
            {
                string capturedId = runnerId;
                _uiManager.RegisterTooltip(warningBadge, () =>
                {
                    var r = _uiManager.Simulation?.FindRunner(capturedId);
                    return r?.ActiveWarning;
                });
            }

            _portraits[runnerId] = instance;
            _runnerSortOrder.Add(runnerId);

            TrackNodeGroupOrder(runner.CurrentNodeId);
            _lastGroupingKey = "";
        }

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

        public void Refresh()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            foreach (var kvp in _portraits)
            {
                var runner = sim.FindRunner(kvp.Key);
                if (runner == null) continue;

                var nameLabel = kvp.Value.Q<Label>("portrait-name");
                if (nameLabel != null)
                    nameLabel.text = runner.Name;

                var stateLabel = kvp.Value.Q<Label>("portrait-state");
                if (stateLabel != null)
                {
                    stateLabel.text = FormatShortState(runner);
                    ApplyStateClass(stateLabel, runner.State);
                }

                var warningBadge = kvp.Value.Q("portrait-warning-badge");
                if (warningBadge != null)
                {
                    bool hasWarning = runner.ActiveWarning != null;
                    warningBadge.style.display = hasWarning ? DisplayStyle.Flex : DisplayStyle.None;
                    warningBadge.pickingMode = hasWarning ? PickingMode.Position : PickingMode.Ignore;
                }
            }

            // Don't rebuild while dragging — avoids visual disruption
            if (!_isDragActive)
                RebuildGroupingIfNeeded(sim);
        }

        // ─── Grouping ────────────────────────────────────

        private void TrackNodeGroupOrder(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (_groupFirstSeenOrder.Contains(nodeId)) return;

            string hubNodeId = _uiManager.Simulation?.CurrentGameState?.Map?.HubNodeId;

            if (nodeId == hubNodeId)
            {
                // Hub always goes at index 0
                _groupFirstSeenOrder.Insert(0, nodeId);
            }
            else
            {
                // Ensure hub is present at index 0 before adding others
                if (hubNodeId != null && !_groupFirstSeenOrder.Contains(hubNodeId))
                    _groupFirstSeenOrder.Insert(0, hubNodeId);
                _groupFirstSeenOrder.Add(nodeId);
            }
        }

        private void RebuildGroupingIfNeeded(GameSimulation sim)
        {
            var runners = sim.CurrentGameState.Runners;
            string hubNodeId = sim.CurrentGameState.Map?.HubNodeId ?? "hub";

            // Discover any new node groups
            foreach (var runner in runners)
            {
                if (_portraits.ContainsKey(runner.Id))
                    TrackNodeGroupOrder(runner.CurrentNodeId);
            }

            // Sort: group by node (hub first, then first-seen order),
            // within each group by player's preferred order
            var sorted = runners
                .Where(r => _portraits.ContainsKey(r.Id))
                .OrderBy(r => GetGroupSortIndex(r.CurrentNodeId, hubNodeId))
                .ThenBy(r => GetRunnerSortIndex(r.Id))
                .ToList();

            string groupingKey = string.Join("|", sorted.Select(r => $"{r.CurrentNodeId}:{r.Id}"));
            if (groupingKey == _lastGroupingKey) return;
            _lastGroupingKey = groupingKey;

            // Rebuild container
            _container.Clear();
            _separators.Clear();

            string lastNodeId = null;
            bool isFirstGroup = true;
            foreach (var runner in sorted)
            {
                if (runner.CurrentNodeId != lastNodeId)
                {
                    var separator = CreateNodeSeparator(sim, runner.CurrentNodeId);
                    if (isFirstGroup)
                        separator.AddToClassList("portrait-node-separator-first");
                    isFirstGroup = false;
                    _container.Add(separator);
                    _separators.Add(separator);
                }

                _container.Add(_portraits[runner.Id]);
                lastNodeId = runner.CurrentNodeId;
            }
        }

        private int GetGroupSortIndex(string nodeId, string hubNodeId)
        {
            if (nodeId == hubNodeId) return 0;
            int idx = _groupFirstSeenOrder.IndexOf(nodeId);
            return idx >= 0 ? idx : _groupFirstSeenOrder.Count;
        }

        private int GetRunnerSortIndex(string runnerId)
        {
            int idx = _runnerSortOrder.IndexOf(runnerId);
            return idx >= 0 ? idx : _runnerSortOrder.Count;
        }

        // ─── Drag-to-reorder ─────────────────────────────

        private void OnPortraitPointerDown(PointerDownEvent evt, string runnerId, VisualElement portraitRoot)
        {
            if (evt.button != 0) return;

            _isDragPending = true;
            _isDragActive = false;
            _dragRunnerId = runnerId;
            _dragStartPosition = evt.position;
            _lastDragPosition = evt.position;
            _dragPointerId = evt.pointerId;
            _dragCaptureElement = portraitRoot;

            portraitRoot.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPortraitPointerMove(PointerMoveEvent evt)
        {
            if (!_isDragPending && !_isDragActive) return;

            Vector2 currentPos = evt.position;
            _lastDragPosition = currentPos;

            if (_isDragPending && !_isDragActive)
            {
                if (Vector2.Distance(_dragStartPosition, currentPos) < DragThresholdPixels)
                    return;

                BeginDrag();
            }

            if (_isDragActive)
                UpdateDrag(currentPos);
        }

        private void OnPortraitPointerUp(PointerUpEvent evt, string runnerId)
        {
            if (evt.pointerId != _dragPointerId) return;

            _dragCaptureElement?.ReleasePointer(evt.pointerId);

            if (_isDragActive)
            {
                FinalizeDrag();
            }
            else if (_isDragPending)
            {
                // Pointer didn't move past threshold — treat as click
                _uiManager.SelectRunner(runnerId);
            }

            ResetDragState();
        }

        private void OnPointerCaptureOut()
        {
            if (_isDragActive)
            {
                CleanupDragVisuals();
                _lastGroupingKey = "";
            }
            ResetDragState();
        }

        private void ResetDragState()
        {
            _isDragPending = false;
            _isDragActive = false;
            _dragRunnerId = null;
            _dragPointerId = -1;
            _dragCaptureElement = null;
        }

        private void BeginDrag()
        {
            _isDragActive = true;
            _isDragPending = false;

            if (!_portraits.TryGetValue(_dragRunnerId, out var portrait)) return;
            var portraitRoot = portrait.Q("portrait-root");
            if (portraitRoot == null) return;

            // Dim the original in place
            portraitRoot.AddToClassList("dragging");

            // Create a ghost copy that follows the pointer
            var bounds = portraitRoot.worldBound;
            _dragGhostHalfWidth = bounds.width / 2;
            _dragGhostHalfHeight = bounds.height / 2;

            _dragGhost = _portraitTemplate.Instantiate();
            SetPickingModeRecursive(_dragGhost, PickingMode.Ignore);
            _dragGhost.style.position = Position.Absolute;
            _dragGhost.style.width = bounds.width;
            _dragGhost.style.height = bounds.height;

            var ghostRoot = _dragGhost.Q("portrait-root");
            if (ghostRoot != null)
                ghostRoot.style.opacity = 0.85f;

            var runner = _uiManager.Simulation?.FindRunner(_dragRunnerId);
            if (runner != null)
            {
                var nameLabel = _dragGhost.Q<Label>("portrait-name");
                if (nameLabel != null) nameLabel.text = runner.Name;
                var stateLabel = _dragGhost.Q<Label>("portrait-state");
                if (stateLabel != null) stateLabel.text = FormatShortState(runner);
            }

            // Gold drop indicator line
            _dropIndicator = new VisualElement();
            _dropIndicator.pickingMode = PickingMode.Ignore;
            _dropIndicator.style.position = Position.Absolute;
            _dropIndicator.style.width = 3;
            _dropIndicator.style.height = bounds.height;
            _dropIndicator.style.backgroundColor = new StyleColor(new Color(0.86f, 0.7f, 0.23f));
            _dropIndicator.style.borderTopLeftRadius = 2;
            _dropIndicator.style.borderTopRightRadius = 2;
            _dropIndicator.style.borderBottomLeftRadius = 2;
            _dropIndicator.style.borderBottomRightRadius = 2;
            _dropIndicator.style.display = DisplayStyle.None;

            // Add ghost and indicator to panel root so they float above everything
            var panelRoot = _container.panel.visualTree;
            panelRoot.Add(_dragGhost);
            panelRoot.Add(_dropIndicator);

            _dragGhost.style.left = _dragStartPosition.x - _dragGhostHalfWidth;
            _dragGhost.style.top = _dragStartPosition.y - _dragGhostHalfHeight;
        }

        private void UpdateDrag(Vector2 pointerPosition)
        {
            if (_dragGhost == null) return;

            _dragGhost.style.left = pointerPosition.x - _dragGhostHalfWidth;
            _dragGhost.style.top = pointerPosition.y - _dragGhostHalfHeight;

            UpdateDropIndicator(pointerPosition);
        }

        private void UpdateDropIndicator(Vector2 pointerPosition)
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var draggedRunner = sim.FindRunner(_dragRunnerId);
            if (draggedRunner == null) return;
            string draggedNodeId = draggedRunner.CurrentNodeId;

            // Only show drop positions among same-node runners (can't reorder across groups)
            var sameNodePortraits = GetSameNodePortraitsInVisualOrder(sim, draggedNodeId);

            if (sameNodePortraits.Count == 0)
            {
                _dropIndicator.style.display = DisplayStyle.None;
                return;
            }

            // Find which gap the pointer falls into
            int insertBefore = sameNodePortraits.Count;
            for (int i = 0; i < sameNodePortraits.Count; i++)
            {
                if (pointerPosition.x < sameNodePortraits[i].bounds.center.x)
                {
                    insertBefore = i;
                    break;
                }
            }

            // Position the indicator at the gap
            float indicatorX;
            float indicatorY = sameNodePortraits[0].bounds.y;

            if (insertBefore == 0)
                indicatorX = sameNodePortraits[0].bounds.xMin - 4;
            else if (insertBefore >= sameNodePortraits.Count)
                indicatorX = sameNodePortraits[sameNodePortraits.Count - 1].bounds.xMax + 1;
            else
                indicatorX = (sameNodePortraits[insertBefore - 1].bounds.xMax +
                             sameNodePortraits[insertBefore].bounds.xMin) / 2;

            _dropIndicator.style.display = DisplayStyle.Flex;
            _dropIndicator.style.left = indicatorX;
            _dropIndicator.style.top = indicatorY;
        }

        private void FinalizeDrag()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) { CleanupDragVisuals(); return; }

            var draggedRunner = sim.FindRunner(_dragRunnerId);
            if (draggedRunner == null) { CleanupDragVisuals(); return; }

            var sameNodePortraits = GetSameNodePortraitsInVisualOrder(sim, draggedRunner.CurrentNodeId);

            // Determine insertion point
            int insertBefore = sameNodePortraits.Count;
            for (int i = 0; i < sameNodePortraits.Count; i++)
            {
                if (_lastDragPosition.x < _portraits[sameNodePortraits[i].runnerId].worldBound.center.x)
                {
                    insertBefore = i;
                    break;
                }
            }

            // Update the master sort order
            _runnerSortOrder.Remove(_dragRunnerId);

            if (sameNodePortraits.Count == 0 || insertBefore >= sameNodePortraits.Count)
            {
                // Insert after the last same-node runner
                if (sameNodePortraits.Count > 0)
                {
                    string lastId = sameNodePortraits[sameNodePortraits.Count - 1].runnerId;
                    int afterIdx = _runnerSortOrder.IndexOf(lastId);
                    _runnerSortOrder.Insert(afterIdx + 1, _dragRunnerId);
                }
                else
                {
                    _runnerSortOrder.Add(_dragRunnerId);
                }
            }
            else
            {
                string beforeId = sameNodePortraits[insertBefore].runnerId;
                int beforeIdx = _runnerSortOrder.IndexOf(beforeId);
                _runnerSortOrder.Insert(beforeIdx, _dragRunnerId);
            }

            CleanupDragVisuals();
            _lastGroupingKey = "";
        }

        /// <summary>
        /// Returns same-node portraits (excluding the dragged one) in their current
        /// visual order within the container.
        /// </summary>
        private List<(string runnerId, Rect bounds)> GetSameNodePortraitsInVisualOrder(
            GameSimulation sim, string nodeId)
        {
            var result = new List<(string runnerId, Rect bounds)>();
            foreach (var child in _container.Children())
            {
                if (_separators.Contains(child)) continue;

                string runnerId = null;
                foreach (var kvp in _portraits)
                {
                    if (kvp.Value == child) { runnerId = kvp.Key; break; }
                }
                if (runnerId == null || runnerId == _dragRunnerId) continue;

                var runner = sim.FindRunner(runnerId);
                if (runner?.CurrentNodeId != nodeId) continue;

                result.Add((runnerId, child.worldBound));
            }
            return result;
        }

        private void CleanupDragVisuals()
        {
            _dragGhost?.RemoveFromHierarchy();
            _dragGhost = null;

            _dropIndicator?.RemoveFromHierarchy();
            _dropIndicator = null;

            if (_dragRunnerId != null && _portraits.TryGetValue(_dragRunnerId, out var portrait))
                portrait.Q("portrait-root")?.RemoveFromClassList("dragging");
        }

        private static void SetPickingModeRecursive(VisualElement element, PickingMode mode)
        {
            element.pickingMode = mode;
            foreach (var child in element.Children())
                SetPickingModeRecursive(child, mode);
        }

        // ─── Node separator ──────────────────────────────

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

        // ─── State display helpers ───────────────────────

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
