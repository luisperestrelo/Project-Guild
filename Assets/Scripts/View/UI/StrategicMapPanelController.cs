using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Gathering;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.Tutorial;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Controller for the Strategic Map overlay panel. Shows a 2D top-down view of all
    /// world nodes, edges between them, runner positions (at nodes and traveling), and
    /// provides click-to-send-runner and double-click-to-observe functionality.
    ///
    /// Implements ITickRefreshable to update runner positions and badges every sim tick.
    /// </summary>
    public class StrategicMapPanelController : ITickRefreshable
    {
        private readonly VisualElement _root;
        private readonly VisualElement _viewport;
        private readonly VisualElement _transformContainer;
        private readonly StrategicMapEdgeDrawer _edgeDrawer;
        private readonly UIManager _uiManager;

        // ─── Node popup ─────────────────────────────────
        private readonly VisualElement _nodePopup;
        private readonly Label _popupNodeName;
        private readonly VisualElement _popupGatherablesList;
        private readonly Label _popupTravelInfo;
        private readonly VisualElement _popupRunnersList;
        private readonly Label _popupMacroNote;
        private readonly Button _popupConfirmButton;
        private readonly Button _popupCancelButton;
        private string _popupTargetNodeId;

        // ─── Zoom / Pan ─────────────────────────────────
        private float _zoom = 1.0f;
        private Vector2 _panOffset;
        private bool _isPanning;
        private Vector2 _panStartPointer;
        private Vector2 _panStartOffset;

        private const float MinZoom = 0.3f;
        private const float MaxZoom = 3.0f;

        // ─── Coordinate mapping ─────────────────────────
        // 5000px anchor center, 10px per world unit, Z negated (north = up)
        private const float AnchorCenter = 5000f;
        private const float PixelsPerUnit = 10f;

        // ─── Node elements ──────────────────────────────
        private readonly Dictionary<string, VisualElement> _nodeElements = new();
        private readonly Dictionary<string, VisualElement> _nodeRunnerIndicators = new();
        private readonly Dictionary<string, VisualElement> _nodeRunnerBadges = new();
        private bool _nodesBuilt;

        // ─── Edge drawer hover tracking ──────────────────
        private Vector2 _edgeDrawerLastLocalPointer;

        // ─── Double-click tracking ──────────────────────
        private string _lastClickedNodeId;
        private float _lastClickTime;
        private string _lastClickedDotRunnerId;
        private float _lastDotClickTime;
        private const float DoubleClickThreshold = 0.3f;

        // ─── State ──────────────────────────────────────
        public bool IsOpen { get; private set; }
        private Action _onClosed;

        /// <summary>
        /// Returns the VisualElement for a specific map node, or null if not built.
        /// Used by TutorialController for highlight pulses.
        /// </summary>
        public VisualElement GetNodeElement(string nodeId) =>
            _nodeElements.TryGetValue(nodeId, out var el) ? el : null;

        /// <summary>
        /// Force nodes to be rebuilt on next Open/Refresh.
        /// Called when tutorial state changes and node visibility may have changed.
        /// </summary>
        public void InvalidateNodes()
        {
            if (!_nodesBuilt) return;
            foreach (var el in _nodeElements.Values)
                el.RemoveFromHierarchy();
            _nodeElements.Clear();
            _nodeRunnerIndicators.Clear();
            _nodeRunnerBadges.Clear();
            _nodesBuilt = false;
        }

        public StrategicMapPanelController(VisualElement root, UIManager uiManager, Action onClosed)
        {
            _root = root;
            _uiManager = uiManager;
            _onClosed = onClosed;

            _viewport = root.Q("strategic-map-viewport");
            _transformContainer = root.Q("strategic-map-transform-container");

            // Close button
            root.Q<Button>("btn-close-map").clicked += Close;

            // Node popup
            _nodePopup = root.Q("strategic-map-node-popup");
            _popupNodeName = root.Q<Label>("popup-node-name");
            _popupGatherablesList = root.Q("popup-gatherables-list");
            _popupTravelInfo = root.Q<Label>("popup-travel-info");
            _popupRunnersList = root.Q("popup-runners-list");
            _popupMacroNote = root.Q<Label>("popup-macro-note");
            _popupConfirmButton = root.Q<Button>("btn-popup-confirm");
            _popupCancelButton = root.Q<Button>("btn-popup-cancel");
            _popupCancelButton.clicked += DismissPopup;

            // Edge drawer (custom VisualElement, absolute fill inside transform container)
            _edgeDrawer = new StrategicMapEdgeDrawer();
            _edgeDrawer.style.position = Position.Absolute;
            _edgeDrawer.style.left = 0;
            _edgeDrawer.style.top = 0;
            _edgeDrawer.style.right = 0;
            _edgeDrawer.style.bottom = 0;
            _edgeDrawer.pickingMode = PickingMode.Position;
            _edgeDrawer.OnRunnerDotClicked += OnTravelingRunnerDotClicked;
            _transformContainer.Add(_edgeDrawer);

            // Track pointer position inside edge drawer for tooltip hit-testing
            _edgeDrawer.RegisterCallback<PointerMoveEvent>(evt =>
                _edgeDrawerLastLocalPointer = evt.localPosition);

            // Tooltip on edge drawer for traveling runner dots (registered once, callback reads live data)
            RegisterEdgeDrawerTooltip();

            // Zoom (scroll wheel on viewport)
            _viewport.RegisterCallback<WheelEvent>(OnWheel);

            // Pan (left-drag on background, middle-drag anywhere)
            _viewport.RegisterCallback<PointerDownEvent>(OnViewportPointerDown);
            _viewport.RegisterCallback<PointerMoveEvent>(OnViewportPointerMove);
            _viewport.RegisterCallback<PointerUpEvent>(OnViewportPointerUp);

            // Click background to dismiss popup
            _viewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && _nodePopup.style.display == DisplayStyle.Flex)
                    DismissPopup();
            }, TrickleDown.TrickleDown);

            // Escape to close
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    if (_nodePopup.style.display == DisplayStyle.Flex)
                        DismissPopup();
                    else
                        Close();
                    evt.StopPropagation();
                }
            });

            // Start hidden
            _root.style.display = DisplayStyle.None;

            // Register for tick refreshing
            uiManager.RegisterTickRefreshable(this);
        }

        // ─── Open / Close / Toggle ──────────────────────

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _root.style.display = DisplayStyle.Flex;
            _root.Focus();

            if (!_nodesBuilt)
                BuildNodes();

            RefreshAll();

            // Center after layout resolves viewport dimensions
            _viewport.RegisterCallbackOnce<GeometryChangedEvent>(_ => CenterOnSelectedRunnerNode());
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _root.style.display = DisplayStyle.None;
            DismissPopup();
            _onClosed?.Invoke();
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        /// <summary>
        /// Center the map on a specific runner's current position.
        /// Called when the player clicks a portrait while the map is open.
        /// </summary>
        public void CenterOnRunner(string runnerId)
        {
            if (!IsOpen) return;
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var state = sim.CurrentGameState;
            var map = state.Map;
            if (map == null) return;

            var runner = state.Runners.Find(r => r.Id == runnerId);
            if (runner == null) return;

            var centerUi = GetRunnerMapPosition(runner, map);

            float vpWidth = _viewport.resolvedStyle.width;
            float vpHeight = _viewport.resolvedStyle.height;
            if (float.IsNaN(vpWidth)) vpWidth = Screen.width;
            if (float.IsNaN(vpHeight)) vpHeight = Screen.height - 40;

            _panOffset = new Vector2(
                vpWidth / 2f - centerUi.x * _zoom,
                vpHeight / 2f - centerUi.y * _zoom
            );

            ApplyTransform();
        }

        // ─── ITickRefreshable ───────────────────────────

        public void Refresh()
        {
            if (!IsOpen) return;
            RefreshAll();
        }

        // ─── Coordinate Mapping ─────────────────────────

        private Vector2 WorldToUiPosition(float worldX, float worldZ)
        {
            return new Vector2(
                AnchorCenter + worldX * PixelsPerUnit,
                AnchorCenter - worldZ * PixelsPerUnit
            );
        }

        // ─── Node Building ──────────────────────────────

        private void BuildNodes()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var state = sim.CurrentGameState;
            var map = state.Map;
            if (map == null) return;

            var discoveredNodes = state.Tutorial.DiscoveredNodeIds;
            bool filterNodes = state.Tutorial.IsActive && discoveredNodes.Count > 0;

            foreach (var node in map.Nodes)
            {
                if (filterNodes && !discoveredNodes.Contains(node.Id))
                    continue;

                var nodeEl = BuildNodeElement(node, node.Id == map.HubNodeId);
                var pos = WorldToUiPosition(node.WorldX, node.WorldZ);
                nodeEl.style.left = pos.x;
                nodeEl.style.top = pos.y;
                _transformContainer.Add(nodeEl);
                _nodeElements[node.Id] = nodeEl;
            }

            _nodesBuilt = true;
        }

        private VisualElement BuildNodeElement(WorldNode node, bool isHub)
        {
            var container = new VisualElement();
            container.AddToClassList("strategic-map-node");
            if (isHub)
                container.AddToClassList("strategic-map-node-hub");

            // Icon (clickable)
            var icon = new VisualElement();
            icon.AddToClassList("strategic-map-node-icon");

            // Runner count badge (on icon, hidden initially)
            var badge = new VisualElement();
            badge.AddToClassList("strategic-map-runner-badge");
            badge.style.display = DisplayStyle.None;
            var badgeLabel = new Label("0");
            badgeLabel.AddToClassList("strategic-map-runner-badge-label");
            badge.Add(badgeLabel);
            icon.Add(badge);
            _nodeRunnerBadges[node.Id] = badge;

            container.Add(icon);

            // Name label
            var nameLabel = new Label(node.Name);
            nameLabel.AddToClassList("strategic-map-node-name");
            container.Add(nameLabel);

            // Gatherable dots
            if (node.Gatherables != null && node.Gatherables.Length > 0)
            {
                var dotsRow = new VisualElement();
                dotsRow.AddToClassList("strategic-map-gatherable-dots");
                foreach (var g in node.Gatherables)
                {
                    var dot = new VisualElement();
                    dot.AddToClassList("strategic-map-gatherable-dot");
                    dot.AddToClassList(GetGatherableDotClass(g.RequiredSkill));
                    dotsRow.Add(dot);
                }
                container.Add(dotsRow);
            }

            // Enemy indicator (combat node)
            if (node.EnemySpawns != null && node.EnemySpawns.Length > 0)
            {
                var combatDot = new VisualElement();
                combatDot.AddToClassList("strategic-map-combat-indicator");
                combatDot.pickingMode = PickingMode.Ignore;
                container.Add(combatDot);
            }

            // Runner indicator row (populated on refresh)
            var runnerRow = new VisualElement();
            runnerRow.AddToClassList("strategic-map-runner-indicators");
            container.Add(runnerRow);
            _nodeRunnerIndicators[node.Id] = runnerRow;

            // Register tooltip (normal + ALT-expanded)
            RegisterNodeTooltip(container, node);

            // Click handler for popup / double-click for observe
            icon.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();

                float now = Time.realtimeSinceStartup;
                if (_lastClickedNodeId == node.Id && (now - _lastClickTime) < DoubleClickThreshold)
                {
                    OnNodeDoubleClicked(node.Id);
                    _lastClickedNodeId = null;
                }
                else
                {
                    _lastClickedNodeId = node.Id;
                    _lastClickTime = now;
                    OnNodeClicked(node.Id, icon);
                }
            });

            return container;
        }

        private static string GetGatherableDotClass(SkillType skill)
        {
            return skill switch
            {
                SkillType.Mining => "strategic-map-gatherable-dot-mining",
                SkillType.Woodcutting => "strategic-map-gatherable-dot-woodcutting",
                SkillType.Fishing => "strategic-map-gatherable-dot-fishing",
                SkillType.Foraging => "strategic-map-gatherable-dot-foraging",
                _ => "strategic-map-gatherable-dot-mining"
            };
        }

        // ─── Node Tooltip ───────────────────────────────

        private void RegisterNodeTooltip(VisualElement nodeEl, WorldNode node)
        {
            _uiManager.RegisterTooltip(nodeEl, () =>
            {
                var sim = _uiManager.Simulation;
                if (sim == null) return null;

                var state = sim.CurrentGameState;
                var map = state.Map;
                var lines = new List<string>();

                lines.Add($"<b>{node.Name}</b>");

                // Gatherables
                if (node.Gatherables != null && node.Gatherables.Length > 0)
                {
                    foreach (var g in node.Gatherables)
                    {
                        string itemName = ResolveItemName(sim, g.ProducedItemId);
                        string levelReq = g.MinLevel > 0 ? $", Lv{g.MinLevel}" : "";
                        lines.Add($"  {itemName} ({g.RequiredSkill}{levelReq})");
                    }
                }

                // Enemies
                if (node.EnemySpawns != null && node.EnemySpawns.Length > 0)
                {
                    foreach (var spawn in node.EnemySpawns)
                    {
                        string enemyName = ResolveEnemyName(sim, spawn.EnemyConfigId);
                        int enemyLevel = ResolveEnemyLevel(sim, spawn.EnemyConfigId);
                        string levelStr = enemyLevel > 0 ? $" (Lv{enemyLevel})" : "";
                        lines.Add($"  <color=#CC6666>{enemyName}{levelStr} x{spawn.InitialCount}</color>");
                    }
                }

                // Runner count
                int runnerCount = CountRunnersAtNode(state, node.Id);
                lines.Add($"{runnerCount} runner{(runnerCount != 1 ? "s" : "")}");

                // Distance from hub
                if (node.Id != map.HubNodeId)
                {
                    float dist = map.GetEuclideanDistance(map.HubNodeId, node.Id);
                    lines.Add($"Distance from hub: {dist:F0}m");
                }

                // ALT-expanded details
                bool altHeld = Keyboard.current != null && Keyboard.current.altKey.isPressed;
                if (altHeld && runnerCount > 0)
                {
                    lines.Add("");
                    foreach (var runner in state.Runners)
                    {
                        if (!IsRunnerAtNode(runner, node.Id))
                            continue;
                        string activity = GetRunnerActivityDescription(sim, runner);
                        lines.Add($"  {runner.Name}: {activity}");
                    }
                }

                // ALT: show travel time for selected runner
                if (altHeld && _uiManager.SelectedRunnerId != null && node.Id != map.HubNodeId)
                {
                    var selectedRunner = state.Runners.Find(r => r.Id == _uiManager.SelectedRunnerId);
                    if (selectedRunner != null && !IsRunnerAtNode(selectedRunner, node.Id))
                    {
                        float dist = GetDistanceFromRunner(selectedRunner, node.Id, map);
                        float athleticsLevel = selectedRunner.GetEffectiveLevel(
                            SkillType.Athletics, sim.Config);
                        float speed = sim.Config.BaseTravelSpeed
                            + (athleticsLevel - 1) * sim.Config.AthleticsSpeedPerLevel;
                        float travelTime = speed > 0 ? dist / speed : 0;
                        lines.Add(travelTime > 0
                            ? $"\nTravel time: {FormatTime(travelTime)} for {selectedRunner.Name}"
                            : $"\nDistance: {dist:F0}m from {selectedRunner.Name}");
                    }
                }

                return string.Join("\n", lines);
            });
        }

        // ─── Node Click (Action Popup) ──────────────────

        private void OnNodeClicked(string nodeId, VisualElement iconElement)
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var state = sim.CurrentGameState;
            var node = state.Map.GetNode(nodeId);
            if (node == null) return;

            _popupTargetNodeId = nodeId;
            _popupNodeName.text = node.Name;

            // Gatherables
            _popupGatherablesList.Clear();
            if (node.Gatherables != null)
            {
                foreach (var g in node.Gatherables)
                {
                    var row = new VisualElement();
                    row.AddToClassList("popup-gatherable-row");

                    var dot = new VisualElement();
                    dot.AddToClassList("popup-gatherable-dot");
                    dot.AddToClassList(GetGatherableDotClass(g.RequiredSkill));
                    row.Add(dot);

                    string itemName = ResolveItemName(sim, g.ProducedItemId);
                    string levelReq = g.MinLevel > 0 ? $", Lv{g.MinLevel}" : "";
                    var text = new Label($"{itemName} ({g.RequiredSkill}{levelReq})");
                    text.AddToClassList("popup-gatherable-text");
                    row.Add(text);

                    _popupGatherablesList.Add(row);
                }
            }

            // Travel info for selected runner
            string selectedId = _uiManager.SelectedRunnerId;
            var selectedRunner = selectedId != null
                ? state.Runners.Find(r => r.Id == selectedId)
                : null;

            if (selectedRunner != null)
            {
                float dist = GetDistanceFromRunner(selectedRunner, nodeId, state.Map);
                float athleticsLevel = selectedRunner.GetEffectiveLevel(
                    SkillType.Athletics, sim.Config);
                float speed = sim.Config.BaseTravelSpeed
                    + (athleticsLevel - 1) * sim.Config.AthleticsSpeedPerLevel;
                float travelTime = speed > 0 ? dist / speed : 0;
                _popupTravelInfo.text = travelTime > 0
                    ? $"Distance: {dist:F0}m\nTravel time: {FormatTime(travelTime)} for {selectedRunner.Name}"
                    : $"Distance: {dist:F0}m";
                _popupTravelInfo.style.display = DisplayStyle.Flex;
            }
            else
            {
                float dist = state.Map.GetEuclideanDistance(state.Map.HubNodeId, nodeId);
                _popupTravelInfo.text = $"Distance from hub: {dist:F0}m";
                _popupTravelInfo.style.display = DisplayStyle.Flex;
            }

            // Runners at this node (including exit-phase runners)
            _popupRunnersList.Clear();
            foreach (var runner in state.Runners)
            {
                if (!IsRunnerAtNode(runner, nodeId))
                    continue;

                var row = new VisualElement();
                row.AddToClassList("popup-runner-row");
                string activity = GetRunnerActivityDescription(sim, runner);
                var nameLabel = new Label($"{runner.Name}: {activity}");
                nameLabel.AddToClassList("popup-runner-name");
                row.Add(nameLabel);
                _popupRunnersList.Add(row);
            }

            // Confirm button
            _popupConfirmButton.clicked -= OnPopupConfirm;
            _popupConfirmButton.clicked += OnPopupConfirm;

            bool isHub = nodeId == state.Map.HubNodeId;
            if (selectedRunner != null)
            {
                _popupConfirmButton.text = isHub
                    ? $"Send {selectedRunner.Name} to Guild Hall"
                    : $"Send {selectedRunner.Name}";
                _popupConfirmButton.SetEnabled(true);
                _popupMacroNote.style.display = isHub ? DisplayStyle.None : DisplayStyle.Flex;
            }
            else
            {
                _popupConfirmButton.text = "Select a runner first";
                _popupConfirmButton.SetEnabled(false);
                _popupMacroNote.style.display = DisplayStyle.None;
            }

            // Show popup off-screen so layout resolves, then position properly
            _nodePopup.style.left = -9999;
            _nodePopup.style.display = DisplayStyle.Flex;
            _nodePopup.RegisterCallbackOnce<GeometryChangedEvent>(_ =>
            {
                PositionPopupNearIcon(iconElement);
            });
        }

        private void OnPopupConfirm()
        {
            string selectedId = _uiManager.SelectedRunnerId;
            if (selectedId == null || _popupTargetNodeId == null) return;

            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var hubId = sim.CurrentGameState.Map.HubNodeId;
            if (_popupTargetNodeId == hubId)
                sim.CommandSendToHub(selectedId);
            else
                sim.CommandWorkAtSuspendMacrosForOneCycle(selectedId, _popupTargetNodeId);

            DismissPopup();

            bool closeOnAssignment = _uiManager.Preferences?.MapCloseOnAssignment ?? true;
            if (closeOnAssignment)
                Close();
        }

        /// <summary>
        /// Position the popup near the clicked icon using its worldBound
        /// converted to root-relative coordinates.
        /// </summary>
        private void PositionPopupNearIcon(VisualElement iconElement)
        {
            var iconBound = iconElement.worldBound;
            // Popup's style.top/left are relative to its parent (strategic-map-root),
            // NOT _root (which is the TemplateContainer at 0,0 full screen).
            var parentBound = _nodePopup.parent.worldBound;

            // Convert icon position to popup-parent-relative
            float iconRightX = iconBound.xMax - parentBound.x;
            float iconLeftX = iconBound.x - parentBound.x;
            float iconCenterY = iconBound.center.y - parentBound.y;

            // Estimate popup size
            float popupW = _nodePopup.resolvedStyle.width;
            float popupH = _nodePopup.resolvedStyle.height;
            if (float.IsNaN(popupW) || popupW < 10) popupW = 240;
            if (float.IsNaN(popupH) || popupH < 10) popupH = 180;

            // Place to the right of the icon, vertically centered
            float popupX = iconRightX + 8;
            float popupY = iconCenterY - popupH * 0.5f;

            // Clamp within popup parent bounds
            float rw = parentBound.width;
            float rh = parentBound.height;
            float margin = 16;

            // If overflows right, place to the left of the icon
            if (popupX + popupW > rw - margin)
                popupX = iconLeftX - popupW - 8;
            if (popupX < margin) popupX = margin;
            if (popupX + popupW > rw - margin) popupX = rw - margin - popupW;
            if (popupY < margin) popupY = margin;
            if (popupY + popupH > rh - margin) popupY = rh - margin - popupH;

            _nodePopup.style.left = popupX;
            _nodePopup.style.top = popupY;
        }

        // ─── Node Double-Click (Observe) ────────────────

        private void OnNodeDoubleClicked(string nodeId)
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var state = sim.CurrentGameState;
            int runnerCount = CountRunnersAtNode(state, nodeId);
            if (runnerCount == 0) return;

            // If selected runner is at this node, follow them. Otherwise follow first runner there.
            string selectedId = _uiManager.SelectedRunnerId;
            var selectedRunner = selectedId != null
                ? state.Runners.Find(r => r.Id == selectedId)
                : null;

            if (selectedRunner != null && IsRunnerAtNode(selectedRunner, nodeId))
            {
                Close();
                _uiManager.SelectRunner(selectedRunner.Id);
            }
            else
            {
                var firstAtNode = state.Runners.Find(r => IsRunnerAtNode(r, nodeId));
                if (firstAtNode != null)
                {
                    Close();
                    _uiManager.SelectRunner(firstAtNode.Id);
                }
            }
        }

        // ─── Traveling Runner Dots ──────────────────────

        private void OnTravelingRunnerDotClicked(string runnerId)
        {
            // Single-click: select runner (centers map on them via SelectRunner hook)
            _uiManager.SelectRunner(runnerId);
        }

        // ─── Popup ──────────────────────────────────────

        private void DismissPopup()
        {
            _nodePopup.style.display = DisplayStyle.None;
            _popupTargetNodeId = null;
        }

        // ─── Zoom / Pan ─────────────────────────────────

        private void OnWheel(WheelEvent evt)
        {
            float zoomDelta = -evt.delta.y * 0.1f;
            float oldZoom = _zoom;
            _zoom = Mathf.Clamp(_zoom + zoomDelta * _zoom, MinZoom, MaxZoom);

            if (Mathf.Approximately(oldZoom, _zoom)) return;

            // Zoom toward cursor position
            var localMouse = evt.localMousePosition;
            float ratio = _zoom / oldZoom;
            _panOffset = localMouse - ratio * (localMouse - _panOffset);

            ApplyTransform();
            evt.StopPropagation();
        }

        private void OnViewportPointerDown(PointerDownEvent evt)
        {
            // Left-drag on background or middle-drag anywhere
            if (evt.button == 0 || evt.button == 2)
            {
                _isPanning = true;
                _panStartPointer = evt.position;
                _panStartOffset = _panOffset;
                _viewport.CapturePointer(evt.pointerId);
            }
        }

        private void OnViewportPointerMove(PointerMoveEvent evt)
        {
            if (!_isPanning) return;
            Vector2 delta = (Vector2)evt.position - _panStartPointer;
            _panOffset = _panStartOffset + delta;
            ApplyTransform();
        }

        private void OnViewportPointerUp(PointerUpEvent evt)
        {
            if (!_isPanning) return;
            _isPanning = false;
            if (_viewport.HasPointerCapture(evt.pointerId))
                _viewport.ReleasePointer(evt.pointerId);
        }

        private void ApplyTransform()
        {
            _transformContainer.style.translate =
                new Translate(new Length(_panOffset.x), new Length(_panOffset.y));
            _transformContainer.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
            _transformContainer.style.transformOrigin =
                new TransformOrigin(new Length(0), new Length(0));
        }

        /// <summary>
        /// Center the map on the selected runner's position. For traveling runners,
        /// interpolates between nodes using the same logic as the travel dot.
        /// Falls back to hub if no runner is selected.
        /// </summary>
        private void CenterOnSelectedRunnerNode()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var state = sim.CurrentGameState;
            var map = state.Map;
            if (map == null) return;

            // Default: center on hub
            var hubNode = map.GetNode(map.HubNodeId);
            if (hubNode == null) return;
            var centerUi = WorldToUiPosition(hubNode.WorldX, hubNode.WorldZ);

            bool centerOnRunner = _uiManager.Preferences?.MapCenterOnRunner ?? true;
            if (centerOnRunner)
            {
                string selectedId = _uiManager.SelectedRunnerId;
                if (selectedId != null)
                {
                    var runner = state.Runners.Find(r => r.Id == selectedId);
                    if (runner != null)
                        centerUi = GetRunnerMapPosition(runner, map);
                }
            }

            float vpWidth = _viewport.resolvedStyle.width;
            float vpHeight = _viewport.resolvedStyle.height;
            if (float.IsNaN(vpWidth)) vpWidth = Screen.width;
            if (float.IsNaN(vpHeight)) vpHeight = Screen.height - 40;

            _zoom = 1.0f;
            _panOffset = new Vector2(
                vpWidth / 2f - centerUi.x * _zoom,
                vpHeight / 2f - centerUi.y * _zoom
            );

            ApplyTransform();
        }

        /// <summary>
        /// Get a runner's UI-space position on the map. For travelers, uses the
        /// sim's virtual world position (StartWorldX/Z + progress) — same formula
        /// the sim uses for redirect calculations. Works for normal travel and
        /// mid-travel redirects.
        /// </summary>
        private Vector2 GetRunnerMapPosition(Runner runner, WorldMap map)
        {
            if (runner.State == RunnerState.Traveling && runner.Travel != null)
            {
                var fromNode = map.GetNode(runner.Travel.FromNodeId);
                var toNode = map.GetNode(runner.Travel.ToNodeId);
                if (fromNode != null && toNode != null)
                    return GetTravelDotPosition(runner.Travel, fromNode, toNode);
            }

            var node = map.GetNode(runner.CurrentNodeId);
            if (node != null)
                return WorldToUiPosition(node.WorldX, node.WorldZ);

            var hub = map.GetNode(map.HubNodeId);
            return hub != null
                ? WorldToUiPosition(hub.WorldX, hub.WorldZ)
                : new Vector2(AnchorCenter, AnchorCenter);
        }

        /// <summary>
        /// Compute the UI-space position of a traveling runner using the sim's
        /// StartWorldX/Z (actual world position at travel start, including redirects)
        /// and travel progress. This is the same formula the sim uses internally.
        /// </summary>
        private Vector2 GetTravelDotPosition(TravelState travel, WorldNode fromNode, WorldNode toNode)
        {
            float startX = travel.StartWorldX ?? fromNode.WorldX;
            float startZ = travel.StartWorldZ ?? fromNode.WorldZ;
            float progress = travel.TotalDistance > 0
                ? Mathf.Clamp01(travel.DistanceCovered / travel.TotalDistance)
                : 0f;
            float virtualX = startX + (toNode.WorldX - startX) * progress;
            float virtualZ = startZ + (toNode.WorldZ - startZ) * progress;
            return WorldToUiPosition(virtualX, virtualZ);
        }

        // ─── Refresh ────────────────────────────────────

        private void RefreshAll()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var state = sim.CurrentGameState;
            var map = state.Map;
            if (map == null) return;

            // Rebuild nodes if invalidated (e.g. tutorial state changed)
            if (!_nodesBuilt)
                BuildNodes();

            string selectedRunnerId = _uiManager.SelectedRunnerId;

            // Update runner badges and indicators per node
            foreach (var node in map.Nodes)
            {
                int count = 0;
                if (_nodeRunnerIndicators.TryGetValue(node.Id, out var indicatorRow))
                {
                    indicatorRow.Clear();
                    int shown = 0;
                    foreach (var runner in state.Runners)
                    {
                        if (!IsRunnerAtNode(runner, node.Id)) continue;
                        count++;

                        if (shown < 5)
                        {
                            var dot = new VisualElement();
                            dot.AddToClassList("strategic-map-runner-dot");
                            if (runner.Id == selectedRunnerId)
                                dot.AddToClassList("strategic-map-runner-dot-selected");

                            string runnerId = runner.Id;
                            string activity = GetRunnerActivityDescription(sim, runner);
                            _uiManager.RegisterTooltip(dot, () => $"{runner.Name}: {activity}");

                            // Stop pointer events from bubbling to the node element
                            // (prevents node tooltip flickering when hovering a runner dot)
                            dot.RegisterCallback<PointerEnterEvent>(evt => evt.StopPropagation());
                            dot.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());
                            dot.RegisterCallback<PointerLeaveEvent>(evt => evt.StopPropagation());

                            dot.RegisterCallback<PointerDownEvent>(evt =>
                            {
                                if (evt.button != 0) return;
                                evt.StopPropagation();
                                float now = Time.realtimeSinceStartup;
                                if (_lastClickedDotRunnerId == runnerId
                                    && (now - _lastDotClickTime) < DoubleClickThreshold)
                                {
                                    // Double-click same runner: close map and follow
                                    _lastClickedDotRunnerId = null;
                                    Close();
                                    _uiManager.SelectRunner(runnerId);
                                }
                                else
                                {
                                    // Single-click: select runner (centers map on them)
                                    _lastClickedDotRunnerId = runnerId;
                                    _lastDotClickTime = now;
                                    _uiManager.SelectRunner(runnerId);
                                }
                            });
                            indicatorRow.Add(dot);
                            shown++;
                        }
                    }

                    if (count > 5)
                    {
                        var overflow = new Label($"+{count - 5}");
                        overflow.AddToClassList("strategic-map-runner-overflow");
                        indicatorRow.Add(overflow);
                    }
                }

                // Update badge
                if (_nodeRunnerBadges.TryGetValue(node.Id, out var badge))
                {
                    if (count > 0)
                    {
                        badge.style.display = DisplayStyle.Flex;
                        var badgeLabel = badge.Q<Label>();
                        if (badgeLabel != null)
                            badgeLabel.text = count.ToString();
                    }
                    else
                    {
                        badge.style.display = DisplayStyle.None;
                    }
                }

                // Highlight node with selected runner
                if (_nodeElements.TryGetValue(node.Id, out var nodeEl))
                {
                    bool hasSelected = false;
                    foreach (var runner in state.Runners)
                    {
                        if (runner.Id != selectedRunnerId) continue;
                        bool selectedAtNode;
                        if (runner.State == RunnerState.Traveling && runner.Travel != null
                            && runner.Travel.IsExitingNode)
                            selectedAtNode = runner.Travel.FromNodeId == node.Id;
                        else if (runner.State == RunnerState.Traveling)
                            selectedAtNode = false;
                        else
                            selectedAtNode = runner.CurrentNodeId == node.Id;

                        if (selectedAtNode)
                        {
                            hasSelected = true;
                            break;
                        }
                    }
                    if (hasSelected)
                        nodeEl.AddToClassList("strategic-map-node-selected");
                    else
                        nodeEl.RemoveFromClassList("strategic-map-node-selected");
                }
            }

            // Build edge data and traveling runner dots
            var edges = new List<StrategicMapEdgeDrawer.EdgeData>();
            var travelDots = new List<StrategicMapEdgeDrawer.TravelingRunnerDot>();

            var edgeDiscoveredNodes = state.Tutorial.DiscoveredNodeIds;
            bool edgeFilterNodes = state.Tutorial.IsActive && edgeDiscoveredNodes.Count > 0;

            foreach (var edge in map.Edges)
            {
                var nodeA = map.GetNode(edge.NodeIdA);
                var nodeB = map.GetNode(edge.NodeIdB);
                if (nodeA == null || nodeB == null) continue;

                // Only draw edges between two visible nodes
                if (edgeFilterNodes
                    && (!edgeDiscoveredNodes.Contains(edge.NodeIdA)
                        || !edgeDiscoveredNodes.Contains(edge.NodeIdB)))
                    continue;

                edges.Add(new StrategicMapEdgeDrawer.EdgeData
                {
                    Start = WorldToUiPosition(nodeA.WorldX, nodeA.WorldZ),
                    End = WorldToUiPosition(nodeB.WorldX, nodeB.WorldZ)
                });
            }

            // Traveling runner dots
            // Track per-edge leaving runner count for spreading
            var leavingCountPerEdge = new Dictionary<string, int>();

            foreach (var runner in state.Runners)
            {
                if (runner.State != RunnerState.Traveling || runner.Travel == null)
                    continue;

                var fromNode = map.GetNode(runner.Travel.FromNodeId);
                var toNode = map.GetNode(runner.Travel.ToNodeId);
                if (fromNode == null || toNode == null) continue;

                var fromPos = WorldToUiPosition(fromNode.WorldX, fromNode.WorldZ);
                var toPos = WorldToUiPosition(toNode.WorldX, toNode.WorldZ);

                if (runner.Travel.IsExitingNode)
                {
                    // Exit phase: amber dot near the departure node, offset toward destination
                    // Spread multiple leaving runners perpendicular to the edge direction
                    string edgeKey = $"{runner.Travel.FromNodeId}->{runner.Travel.ToNodeId}";
                    leavingCountPerEdge.TryGetValue(edgeKey, out int leavingIdx);
                    leavingCountPerEdge[edgeKey] = leavingIdx + 1;

                    // Fixed 40px offset along edge from departure node (clears node element bounds)
                    var edgeDir = (toPos - fromPos);
                    float edgeLen = edgeDir.magnitude;
                    var edgeDirNorm = edgeLen > 0 ? edgeDir / edgeLen : Vector2.right;
                    var dotPos = fromPos + edgeDirNorm * 40f;

                    // Perpendicular spread: offset alternating left/right of edge line
                    if (leavingIdx > 0)
                    {
                        var perp = new Vector2(-edgeDirNorm.y, edgeDirNorm.x);
                        float offset = ((leavingIdx + 1) / 2) * 12f * ((leavingIdx % 2 == 0) ? -1f : 1f);
                        dotPos += perp * offset;
                    }

                    travelDots.Add(new StrategicMapEdgeDrawer.TravelingRunnerDot
                    {
                        Position = dotPos,
                        RunnerId = runner.Id,
                        IsSelected = runner.Id == selectedRunnerId,
                        IsLeaving = true
                    });
                }
                else
                {
                    // Overworld phase: compute virtual world position using StartWorldX/Z
                    // (handles mid-travel redirects correctly — StartWorldX/Z stores the
                    // runner's actual position at the moment of redirect)
                    var dotPos = GetTravelDotPosition(runner.Travel, fromNode, toNode);

                    travelDots.Add(new StrategicMapEdgeDrawer.TravelingRunnerDot
                    {
                        Position = dotPos,
                        RunnerId = runner.Id,
                        IsSelected = runner.Id == selectedRunnerId,
                        IsLeaving = false
                    });
                }
            }

            _edgeDrawer.SetData(edges, travelDots);

            // Update popup if it's open (runner list may have changed)
            if (_nodePopup.style.display == DisplayStyle.Flex && _popupTargetNodeId != null)
                RefreshPopupRunnerList();
        }

        private void RegisterEdgeDrawerTooltip()
        {
            _uiManager.RegisterTooltip(_edgeDrawer, () =>
            {
                // Use the tracked local pointer position from PointerMoveEvent
                string hoveredId = _edgeDrawer.GetDotAtPosition(_edgeDrawerLastLocalPointer);
                if (hoveredId == null) return null;

                var sim = _uiManager.Simulation;
                if (sim == null) return null;
                var state = sim.CurrentGameState;
                var runner = state.Runners.Find(r => r.Id == hoveredId);
                if (runner == null || runner.Travel == null) return null;

                var toNode = state.Map.GetNode(runner.Travel.ToNodeId);
                string destName = toNode?.Name ?? runner.Travel.ToNodeId;

                if (runner.Travel.IsExitingNode)
                {
                    return $"<b>{runner.Name}</b>\n" +
                           $"Leaving for {destName}...";
                }

                float athleticsLevel = runner.GetEffectiveLevel(SkillType.Athletics, sim.Config);
                float speed = sim.Config.BaseTravelSpeed
                    + (athleticsLevel - 1) * sim.Config.AthleticsSpeedPerLevel;

                float remaining = runner.Travel.TotalDistance - runner.Travel.DistanceCovered;
                if (remaining < 0) remaining = 0;
                float travelTime = speed > 0 ? remaining / speed : 0;

                return $"<b>{runner.Name}</b>\n" +
                       $"Traveling to {destName}\n" +
                       $"Speed: {speed:F1} m/s\n" +
                       $"Remaining travel: {FormatTime(travelTime)}\n" +
                       $"Athletics: Lv{(int)athleticsLevel}";
            });
        }

        private void RefreshPopupRunnerList()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var state = sim.CurrentGameState;
            _popupRunnersList.Clear();
            foreach (var runner in state.Runners)
            {
                if (!IsRunnerAtNode(runner, _popupTargetNodeId))
                    continue;

                var row = new VisualElement();
                row.AddToClassList("popup-runner-row");
                string activity = GetRunnerActivityDescription(sim, runner);
                var nameLabel = new Label($"{runner.Name}: {activity}");
                nameLabel.AddToClassList("popup-runner-name");
                row.Add(nameLabel);
                _popupRunnersList.Add(row);
            }
        }

        // ─── Helpers ────────────────────────────────────

        /// <summary>
        /// Returns true if the runner should be considered "at" this node on the map.
        /// Traveling runners (including exit phase) are NOT at any node — they show
        /// as dots on edges instead.
        /// </summary>
        /// <summary>
        /// Compute the Euclidean distance from a runner's current position to a target node.
        /// For mid-travel runners, uses their virtual world position (StartWorldX/Z + progress).
        /// </summary>
        private static float GetDistanceFromRunner(Runner runner, string targetNodeId, WorldMap map)
        {
            var targetNode = map.GetNode(targetNodeId);
            if (targetNode == null) return 0f;

            if (runner.State == RunnerState.Traveling && runner.Travel != null)
            {
                var fromNode = map.GetNode(runner.Travel.FromNodeId);
                var toNode = map.GetNode(runner.Travel.ToNodeId);
                if (fromNode != null && toNode != null)
                {
                    float startX = runner.Travel.StartWorldX ?? fromNode.WorldX;
                    float startZ = runner.Travel.StartWorldZ ?? fromNode.WorldZ;
                    float progress = runner.Travel.TotalDistance > 0
                        ? runner.Travel.DistanceCovered / runner.Travel.TotalDistance
                        : 0f;
                    float virtualX = startX + (toNode.WorldX - startX) * progress;
                    float virtualZ = startZ + (toNode.WorldZ - startZ) * progress;
                    float dx = targetNode.WorldX - virtualX;
                    float dz = targetNode.WorldZ - virtualZ;
                    return Mathf.Sqrt(dx * dx + dz * dz);
                }
            }

            string fromNodeId = runner.CurrentNodeId ?? map.HubNodeId;
            return map.GetEuclideanDistance(fromNodeId, targetNodeId);
        }

        private static bool IsRunnerAtNode(Runner runner, string nodeId)
        {
            if (runner.State == RunnerState.Traveling)
                return false;
            return runner.CurrentNodeId == nodeId;
        }

        private static int CountRunnersAtNode(GameState state, string nodeId)
        {
            int count = 0;
            foreach (var runner in state.Runners)
            {
                if (runner.State == RunnerState.Traveling && runner.Travel != null
                    && runner.Travel.IsExitingNode)
                {
                    if (runner.Travel.FromNodeId == nodeId)
                        count++;
                }
                else if (runner.State != RunnerState.Traveling
                    && runner.CurrentNodeId == nodeId)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Returns the runner's current task sequence step description.
        /// Work steps show "Work: {MicroRulesetName}".
        /// TravelTo steps show "TravelTo {NodeName}".
        /// Exit phase shows "Leaving {NodeName}...".
        /// Falls back to state name if no task sequence.
        /// </summary>
        private static string GetRunnerActivityDescription(GameSimulation sim, Runner runner)
        {
            // Exit phase: runner is Traveling but still physically leaving the departure node
            if (runner.State == RunnerState.Traveling && runner.Travel != null
                && runner.Travel.IsExitingNode)
            {
                var fromNode = sim.CurrentGameState.Map.GetNode(runner.Travel.FromNodeId);
                return $"Leaving {fromNode?.Name ?? runner.Travel.FromNodeId}...";
            }

            // Try to describe via current task sequence step
            var seq = sim.GetRunnerTaskSequence(runner);
            if (seq != null && seq.Steps != null
                && runner.TaskSequenceCurrentStepIndex < seq.Steps.Count)
            {
                var step = seq.Steps[runner.TaskSequenceCurrentStepIndex];
                switch (step.Type)
                {
                    case TaskStepType.Work:
                        string microName = ResolveMicroRulesetName(sim, runner, step);
                        return microName != null ? $"Work: {microName}" : "Work";

                    case TaskStepType.TravelTo:
                        var targetNode = sim.CurrentGameState.Map.GetNode(step.TargetNodeId);
                        return $"TravelTo {targetNode?.Name ?? step.TargetNodeId}";

                    case TaskStepType.Deposit:
                        return "Deposit";
                }
            }

            // Fallback: state name
            if (runner.State == RunnerState.Idle) return "Idle";
            return runner.State.ToString();
        }

        private static string ResolveMicroRulesetName(
            GameSimulation sim, Runner runner, TaskStep step)
        {
            // Check runner-level override first, then step's configured micro
            string overrideId = sim.GetRunnerMicroOverrideForStep(
                runner, runner.TaskSequenceCurrentStepIndex);
            string microId = overrideId ?? step.MicroRulesetId;
            if (microId == null) return null;

            var state = sim.CurrentGameState;
            foreach (var ruleset in state.MicroRulesetLibrary)
            {
                if (ruleset.Id == microId)
                    return ruleset.Name;
            }
            return microId;
        }

        private static string ResolveItemName(GameSimulation sim, string itemId)
        {
            var def = sim.ItemRegistry?.Get(itemId);
            return def?.Name ?? FormatItemId(itemId);
        }

        private static string FormatItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "Unknown";
            // Convert "copper_ore" → "Copper Ore"
            var parts = itemId.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join(" ", parts);
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 60)
                return $"{seconds:F1}s";
            int minutes = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            if (secs == 0)
                return $"{minutes}m";
            return $"{minutes}m {secs}s";
        }

        private static string ResolveEnemyName(GameSimulation sim, string enemyConfigId)
        {
            if (string.IsNullOrEmpty(enemyConfigId)) return "Unknown";
            if (sim.Config?.EnemyDefinitions != null)
            {
                foreach (var def in sim.Config.EnemyDefinitions)
                {
                    if (def.Id == enemyConfigId)
                        return def.Name ?? AutomationUIHelpers.HumanizeId(enemyConfigId);
                }
            }
            return AutomationUIHelpers.HumanizeId(enemyConfigId);
        }

        private static int ResolveEnemyLevel(GameSimulation sim, string enemyConfigId)
        {
            if (string.IsNullOrEmpty(enemyConfigId) || sim.Config?.EnemyDefinitions == null)
                return 0;
            foreach (var def in sim.Config.EnemyDefinitions)
            {
                if (def.Id == enemyConfigId)
                    return def.Level;
            }
            return 0;
        }
    }
}
