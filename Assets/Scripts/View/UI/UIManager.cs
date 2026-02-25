using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Top-level coordinator for the real UI (UI Toolkit).
    /// Owns the UIDocument, instantiates controllers for portrait bar and details panel,
    /// manages runner selection state, and refreshes UI data every simulation tick.
    ///
    /// Called by GameBootstrapper.Initialize() after the simulation and visual world are ready.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private VisualSyncSystem _visualSyncSystem;
        [SerializeField] private CameraController _cameraController;

        [Header("UI Config")]
        [Tooltip("How long (seconds) a skill XP bar stays visible after the skill last gained XP.")]
        [SerializeField] private float _liveStatsXpDisplayWindowSeconds = 5.0f;
        [Tooltip("Maximum number of skill XP progress bars shown simultaneously.")]
        [SerializeField] private int _liveStatsMaxSkillXpBars = 4;

        [Header("UI Assets")]
        [SerializeField] private VisualTreeAsset _mainLayoutAsset;
        [SerializeField] private VisualTreeAsset _runnerPortraitAsset;
        [SerializeField] private VisualTreeAsset _runnerDetailsPanelAsset;
        [SerializeField] private VisualTreeAsset _automationTabAsset;
        [SerializeField] private VisualTreeAsset _automationPanelAsset;
        [SerializeField] private VisualTreeAsset _bankPanelAsset;
        [SerializeField] private VisualTreeAsset _chroniclePanelAsset;
        [SerializeField] private VisualTreeAsset _logPanelContainerAsset;
        [SerializeField] private VisualTreeAsset _decisionLogPanelAsset;
        [SerializeField] private VisualTreeAsset _logbookPanelAsset;
        [SerializeField] private PanelSettings _panelSettings;

        private UIDocument _uiDocument;
        private RunnerPortraitBarController _portraitBarController;
        private RunnerDetailsPanelController _detailsPanelController;
        private AutomationPanelController _automationPanelController;
        private BankPanelController _bankPanelController;
        private ResourceBarController _resourceBarController;
        private LogPanelContainerController _logPanelContainerController;
        private LogbookPanelController _logbookPanelController;

        private string _selectedRunnerId;
        private bool _initialized;

        // ─── Auto-refresh registry ────────────────────────
        // Controllers that implement ITickRefreshable register themselves here
        // via RegisterTickRefreshable() in their constructors. OnSimulationTick
        // iterates this list — no manual per-controller wiring needed.
        private readonly List<ITickRefreshable> _tickRefreshables = new();

        // ─── Tooltip ─────────────────────────────────────
        private VisualElement _tooltip;
        private Label _tooltipLabel;

        // ─── Context Menu (right-click) ─────────────────
        private VisualElement _contextMenu;
        private VisualElement _contextMenuItems;

        // ─── Pointer-over-UI tracking ───────────────────
        // Uses PointerEnter/LeaveEvent instead of manual ScreenToPanel + panel.Pick(),
        // which had coordinate mismatch issues (blocking zone shifted relative to visual panel).
        // Event-driven tracking uses UI Toolkit's own hit testing — no coordinate transforms needed.
        private bool _isPointerOverDetailsPanel;
        private bool _isPointerOverPortraitBar;
        private bool _isPointerOverLogPanel;
        private bool _isPointerOverLogbook;

        // ─── Node-click confirmation popup ────────────────
        private VisualElement _nodeClickPopup;

        public string SelectedRunnerId => _selectedRunnerId;
        public GameSimulation Simulation => _simulationRunner?.Simulation;
        public float LiveStatsXpDisplayWindowSeconds => _liveStatsXpDisplayWindowSeconds;
        public int LiveStatsMaxSkillXpBars => _liveStatsMaxSkillXpBars;

        /// <summary>
        /// Returns true if the mouse pointer is over any interactive UI panel.
        /// Used by CameraController to block zoom/orbit when hovering over UI.
        /// </summary>
        public bool IsPointerOverUI()
        {
            if (_isPointerOverDetailsPanel) return true;
            if (_isPointerOverPortraitBar) return true;
            if (_isPointerOverLogPanel) return true;
            if (_isPointerOverLogbook) return true;
            if (_automationPanelController?.IsOpen == true) return true;
            if (_bankPanelController?.IsOpen == true) return true;
            return false;
        }

        /// <summary>
        /// Register a controller for automatic per-tick refresh.
        /// Called by controllers in their constructors.
        /// </summary>
        public void RegisterTickRefreshable(ITickRefreshable refreshable)
        {
            _tickRefreshables.Add(refreshable);
        }

        /// <summary>
        /// Get the icon sprite for an item by its ID. Returns null if no icon is assigned.
        /// </summary>
        public Sprite GetItemIcon(string itemId)
        {
            if (_simulationRunner?.ItemIcons == null) return null;
            return _simulationRunner.ItemIcons.TryGetValue(itemId, out var icon) ? icon : null;
        }

        /// <summary>
        /// Initialize the UI after the simulation and visual world are ready.
        /// Called by GameBootstrapper at the end of Start().
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            // Set up UIDocument
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null)
                _uiDocument = gameObject.AddComponent<UIDocument>();

            _uiDocument.panelSettings = _panelSettings;
            _uiDocument.visualTreeAsset = _mainLayoutAsset;

            var root = _uiDocument.rootVisualElement;

            // Portrait bar
            var portraitContainer = root.Q("portrait-bar-container");
            _portraitBarController = new RunnerPortraitBarController(
                portraitContainer, _runnerPortraitAsset, this);

            // Track pointer over portrait bar for click-through blocking
            portraitContainer.RegisterCallback<PointerEnterEvent>(_ => _isPointerOverPortraitBar = true);
            portraitContainer.RegisterCallback<PointerLeaveEvent>(_ => _isPointerOverPortraitBar = false);

            // Details panel — instantiate the template into the container
            var detailsContainer = root.Q("details-panel-container");
            var detailsInstance = _runnerDetailsPanelAsset.Instantiate();
            // TemplateContainer must fill the absolute-positioned container
            detailsInstance.style.flexGrow = 1;
            detailsContainer.Add(detailsInstance);
            _detailsPanelController = new RunnerDetailsPanelController(detailsInstance, this, _automationTabAsset);

            // Track pointer over details panel for zoom/orbit blocking.
            // Using PointerEnter/Leave events (UI Toolkit's own hit testing) instead of
            // manual ScreenToPanel + panel.Pick() which had coordinate mismatch issues.
            detailsContainer.RegisterCallback<PointerEnterEvent>(_ => _isPointerOverDetailsPanel = true);
            detailsContainer.RegisterCallback<PointerLeaveEvent>(_ => _isPointerOverDetailsPanel = false);

            // Automation panel (overlay)
            if (_automationPanelAsset != null)
            {
                var panelInstance = _automationPanelAsset.Instantiate();
                // TemplateContainer must fill the root so the absolute-positioned
                // .panel-root child can anchor correctly (left/top/right/bottom offsets).
                panelInstance.style.position = Position.Absolute;
                panelInstance.style.left = 0;
                panelInstance.style.top = 0;
                panelInstance.style.right = 0;
                panelInstance.style.bottom = 0;
                panelInstance.pickingMode = PickingMode.Ignore;
                root.Add(panelInstance);
                _automationPanelController = new AutomationPanelController(panelInstance, this);

                // Toggle button (top-left)
                var toggleBtn = new Button(() => _automationPanelController.Toggle());
                toggleBtn.text = "Automation";
                toggleBtn.AddToClassList("automation-toggle-button");
                root.Add(toggleBtn);
            }

            // Bank panel (overlay)
            if (_bankPanelAsset != null)
            {
                var bankInstance = _bankPanelAsset.Instantiate();
                bankInstance.style.position = Position.Absolute;
                bankInstance.style.left = 0;
                bankInstance.style.top = 0;
                bankInstance.style.right = 0;
                bankInstance.style.bottom = 0;
                bankInstance.pickingMode = PickingMode.Ignore;
                root.Add(bankInstance);
                _bankPanelController = new BankPanelController(bankInstance, this);
            }

            // Log panel container (Activity + Decisions tabs, bottom-left)
            var logPanelContainer = root.Q("log-panel-container");
            if (logPanelContainer != null && _logPanelContainerAsset != null)
            {
                var containerInstance = _logPanelContainerAsset.Instantiate();
                containerInstance.style.flexGrow = 1;
                logPanelContainer.Add(containerInstance);
                _logPanelContainerController = new LogPanelContainerController(
                    containerInstance, this, _chroniclePanelAsset, _decisionLogPanelAsset);

                logPanelContainer.RegisterCallback<PointerEnterEvent>(_ => _isPointerOverLogPanel = true);
                logPanelContainer.RegisterCallback<PointerLeaveEvent>(_ => _isPointerOverLogPanel = false);
            }

            // Logbook panel (bottom-center, player scratchpad)
            var logbookContainer = root.Q("logbook-panel-container");
            if (logbookContainer != null && _logbookPanelAsset != null)
            {
                var logbookInstance = _logbookPanelAsset.Instantiate();
                logbookInstance.style.flexGrow = 1;
                logbookContainer.Add(logbookInstance);
                _logbookPanelController = new LogbookPanelController(logbookInstance, this);

                logbookContainer.RegisterCallback<PointerEnterEvent>(_ => _isPointerOverLogbook = true);
                logbookContainer.RegisterCallback<PointerLeaveEvent>(_ => _isPointerOverLogbook = false);
            }

            // Resource overview bar (left side)
            var resourceBarContainer = root.Q("resource-bar-container");
            if (resourceBarContainer != null)
                _resourceBarController = new ResourceBarController(resourceBarContainer, this);

            // Populate portraits for runners that already exist
            foreach (var runner in Simulation.CurrentGameState.Runners)
                _portraitBarController.AddPortrait(runner.Id);

            // Subscribe to events
            _simulationRunner.Events.Subscribe<SimulationTickCompleted>(OnSimulationTick);
            _simulationRunner.Events.Subscribe<RunnerCreated>(OnRunnerCreated);

            // Build tooltip element (shared across all UI)
            BuildTooltip(root);
            BuildContextMenu(root);

            // Select first runner
            var runners = Simulation.CurrentGameState.Runners;
            if (runners.Count > 0)
                SelectRunner(runners[0].Id);

            _initialized = true;
        }

        public void SelectRunner(string runnerId)
        {
            _selectedRunnerId = runnerId;
            _portraitBarController?.SetSelectedRunner(runnerId);
            _detailsPanelController?.ShowRunner(runnerId);
            _logbookPanelController?.OnRunnerSelected(runnerId);

            // Move camera to selected runner's visual
            if (_visualSyncSystem != null && _cameraController != null)
            {
                var visual = _visualSyncSystem.GetRunnerVisual(runnerId);
                if (visual != null)
                    _cameraController.SetTarget(visual);
            }
        }

        private void Update()
        {
            if (_contextMenu != null && _contextMenu.style.display != DisplayStyle.None)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    DismissContextMenu();
            }
        }

        private void OnSimulationTick(SimulationTickCompleted evt)
        {
            for (int i = 0; i < _tickRefreshables.Count; i++)
                _tickRefreshables[i].Refresh();
        }

        private void OnRunnerCreated(RunnerCreated evt)
        {
            _portraitBarController?.AddPortrait(evt.RunnerId);
        }

        /// <summary>
        /// Open the Automation panel and navigate to a specific item.
        /// Called by AutomationTabController when [Edit in Library] is clicked.
        /// </summary>
        public void OpenAutomationPanelToItem(string tabType, string itemId)
        {
            _automationPanelController?.OpenToItem(tabType, itemId);
        }

        /// <summary>
        /// Open the Automation panel to a specific item from a runner context.
        /// Shows shared template warning if applicable.
        /// </summary>
        public void OpenAutomationPanelToItemFromRunner(string tabType, string itemId, string runnerId)
        {
            _automationPanelController?.OpenToItemFromRunner(tabType, itemId, runnerId);
        }

        // ─── Logbook ─────────────────────────────────────────

        /// <summary>
        /// Appends text to the current logbook page. Used by Chronicle and Decision Log
        /// "Copy to Logbook" context menu actions.
        /// </summary>
        public void AppendToLogbook(string text)
        {
            _logbookPanelController?.AppendToCurrentPage(text);
        }

        // ─── Bank Panel ─────────────────────────────────────

        public void ToggleBankPanel() => _bankPanelController?.Toggle();
        public void OpenBankPanel() => _bankPanelController?.Open();

        // ─── Tooltip ─────────────────────────────────────

        private void BuildTooltip(VisualElement root)
        {
            _tooltip = new VisualElement();
            _tooltip.pickingMode = PickingMode.Ignore;
            _tooltip.style.position = Position.Absolute;
            _tooltip.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.12f, 0.95f));
            _tooltip.style.borderTopWidth = _tooltip.style.borderBottomWidth =
                _tooltip.style.borderLeftWidth = _tooltip.style.borderRightWidth = 1;
            _tooltip.style.borderTopColor = _tooltip.style.borderBottomColor =
                _tooltip.style.borderLeftColor = _tooltip.style.borderRightColor =
                    new StyleColor(new Color(0.4f, 0.4f, 0.5f));
            _tooltip.style.borderTopLeftRadius = _tooltip.style.borderTopRightRadius =
                _tooltip.style.borderBottomLeftRadius = _tooltip.style.borderBottomRightRadius = 3;
            _tooltip.style.paddingLeft = _tooltip.style.paddingRight = 8;
            _tooltip.style.paddingTop = _tooltip.style.paddingBottom = 4;
            _tooltip.style.display = DisplayStyle.None;

            _tooltipLabel = new Label();
            _tooltipLabel.pickingMode = PickingMode.Ignore;
            _tooltipLabel.enableRichText = true;
            _tooltipLabel.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.95f));
            _tooltipLabel.style.fontSize = 12;
            _tooltipLabel.style.whiteSpace = WhiteSpace.Normal;
            _tooltip.Add(_tooltipLabel);

            root.Add(_tooltip);
        }

        /// <summary>
        /// Register an element to show a tooltip on hover. The text callback
        /// is evaluated on each pointer enter so it stays current.
        /// </summary>
        public void RegisterTooltip(VisualElement element, System.Func<string> getText)
        {
            element.RegisterCallback<PointerEnterEvent>(evt =>
            {
                string text = getText();
                if (string.IsNullOrEmpty(text)) return;
                _tooltipLabel.text = text;
                _tooltip.style.display = DisplayStyle.Flex;
            });

            element.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                _tooltip.style.display = DisplayStyle.None;
            });

            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_tooltip.style.display == DisplayStyle.None) return;
                PositionTooltip(evt.position.x, evt.position.y);
            });
        }

        private void PositionTooltip(float pointerX, float pointerY)
        {
            float tooltipWidth = _tooltip.resolvedStyle.width;
            float tooltipHeight = _tooltip.resolvedStyle.height;

            // If tooltip hasn't been laid out yet, use default offset
            if (float.IsNaN(tooltipWidth)) tooltipWidth = 0;
            if (float.IsNaN(tooltipHeight)) tooltipHeight = 0;

            var panelRoot = _tooltip.panel?.visualTree;
            float panelWidth = panelRoot?.resolvedStyle.width ?? 1920;
            float panelHeight = panelRoot?.resolvedStyle.height ?? 1080;

            // Default: below-right of cursor
            float x = pointerX + 12;
            float y = pointerY + 16;

            // Flip left if overflowing right edge
            if (x + tooltipWidth > panelWidth)
                x = pointerX - tooltipWidth - 4;

            // Flip above if overflowing bottom edge
            if (y + tooltipHeight > panelHeight)
                y = pointerY - tooltipHeight - 4;

            // Final clamp to prevent going off-screen
            if (x < 0) x = 0;
            if (y < 0) y = 0;

            _tooltip.style.left = x;
            _tooltip.style.top = y;
        }

        // ─── Context Menu (right-click popup) ───────────

        private void BuildContextMenu(VisualElement root)
        {
            _contextMenu = new VisualElement();
            _contextMenu.style.position = Position.Absolute;
            _contextMenu.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.15f, 0.97f));
            _contextMenu.style.borderTopWidth = _contextMenu.style.borderBottomWidth =
                _contextMenu.style.borderLeftWidth = _contextMenu.style.borderRightWidth = 1;
            _contextMenu.style.borderTopColor = _contextMenu.style.borderBottomColor =
                _contextMenu.style.borderLeftColor = _contextMenu.style.borderRightColor =
                    new StyleColor(new Color(0.4f, 0.4f, 0.5f));
            _contextMenu.style.borderTopLeftRadius = _contextMenu.style.borderTopRightRadius =
                _contextMenu.style.borderBottomLeftRadius = _contextMenu.style.borderBottomRightRadius = 3;
            _contextMenu.style.paddingTop = _contextMenu.style.paddingBottom = 2;
            _contextMenu.style.display = DisplayStyle.None;
            _contextMenu.style.minWidth = 120;

            _contextMenuItems = new VisualElement();
            _contextMenu.Add(_contextMenuItems);

            root.Add(_contextMenu);
        }

        /// <summary>
        /// Register a right-click context menu on an element.
        /// Actions: list of (label, callback) pairs shown as menu items.
        /// </summary>
        public void RegisterContextMenu(VisualElement element, System.Func<System.Collections.Generic.List<(string label, System.Action action)>> getActions)
        {
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                // Right mouse button = button index 1
                if (evt.button != 1) return;
                evt.StopPropagation();

                var actions = getActions();
                if (actions == null || actions.Count == 0) return;

                ShowContextMenu(evt.position.x, evt.position.y, actions);
            });
        }

        public void ShowContextMenu(float x, float y, System.Collections.Generic.List<(string label, System.Action action)> actions)
        {
            _contextMenuItems.Clear();

            foreach (var (label, action) in actions)
            {
                var item = new Label(label);
                item.style.paddingLeft = item.style.paddingRight = 10;
                item.style.paddingTop = item.style.paddingBottom = 4;
                item.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.9f));
                item.style.fontSize = 12;
                item.style.cursor = new StyleCursor(new UnityEngine.UIElements.Cursor());
                item.RegisterCallback<PointerEnterEvent>(_ =>
                    item.style.backgroundColor = new StyleColor(new Color(0.25f, 0.25f, 0.35f)));
                item.RegisterCallback<PointerLeaveEvent>(_ =>
                    item.style.backgroundColor = StyleKeyword.None);
                item.RegisterCallback<PointerDownEvent>(evt =>
                {
                    evt.StopPropagation();
                    action?.Invoke();
                    DismissContextMenu();
                });
                _contextMenuItems.Add(item);
            }

            // Position the menu
            var panelRoot = _contextMenu.panel?.visualTree;
            float panelWidth = panelRoot?.resolvedStyle.width ?? 1920;
            float panelHeight = panelRoot?.resolvedStyle.height ?? 1080;

            // Default: below-right of cursor
            float menuX = x;
            float menuY = y;

            // Adjust if going off-screen (estimate 140px width, 30px per item height)
            float estWidth = 140;
            float estHeight = actions.Count * 28 + 4;
            if (menuX + estWidth > panelWidth) menuX = x - estWidth;
            if (menuY + estHeight > panelHeight) menuY = y - estHeight;
            if (menuX < 0) menuX = 0;
            if (menuY < 0) menuY = 0;

            _contextMenu.style.left = menuX;
            _contextMenu.style.top = menuY;
            _contextMenu.style.display = DisplayStyle.Flex;
        }

        public void DismissContextMenu()
        {
            _contextMenu.style.display = DisplayStyle.None;
            _contextMenuItems.Clear();
        }

        // ─── Node-Click Confirmation Popup ──────────────

        /// <summary>
        /// Shows a centered confirmation popup: "Send [Runner] to work at [Node]?"
        /// Confirm calls CommandWorkAtSuspendMacrosForOneCycle. Cancel dismisses.
        /// Only one popup can be open at a time.
        /// </summary>
        public void ShowNodeClickConfirmation(string runnerId, string nodeId)
        {
            DismissNodeClickConfirmation();

            var sim = Simulation;
            if (sim == null) return;

            var runner = sim.CurrentGameState.Runners.Find(r => r.Id == runnerId);
            var node = sim.CurrentGameState.Map.GetNode(nodeId);
            if (runner == null || node == null) return;

            string runnerName = runner.Name ?? runnerId;
            string nodeName = node.Name ?? nodeId;

            _nodeClickPopup = new VisualElement();
            _nodeClickPopup.name = "node-click-popup";
            _nodeClickPopup.AddToClassList("node-click-popup");

            var message = new Label($"Send {runnerName} to work at {nodeName}?");
            message.AddToClassList("node-click-popup-message");
            _nodeClickPopup.Add(message);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("node-click-popup-buttons");

            var confirmBtn = new Button(() =>
            {
                sim.CommandWorkAtSuspendMacrosForOneCycle(runnerId, nodeId);
                DismissNodeClickConfirmation();
            });
            confirmBtn.text = "Confirm";
            confirmBtn.AddToClassList("node-click-popup-confirm");
            buttonRow.Add(confirmBtn);

            var cancelBtn = new Button(DismissNodeClickConfirmation);
            cancelBtn.text = "Cancel";
            cancelBtn.AddToClassList("node-click-popup-cancel");
            buttonRow.Add(cancelBtn);

            _nodeClickPopup.Add(buttonRow);
            _uiDocument.rootVisualElement.Add(_nodeClickPopup);
        }

        public void DismissNodeClickConfirmation()
        {
            if (_nodeClickPopup != null)
            {
                _nodeClickPopup.RemoveFromHierarchy();
                _nodeClickPopup = null;
            }
        }

        private void OnDestroy()
        {
            if (_simulationRunner?.Events != null)
            {
                _simulationRunner.Events.Unsubscribe<SimulationTickCompleted>(OnSimulationTick);
                _simulationRunner.Events.Unsubscribe<RunnerCreated>(OnRunnerCreated);
            }
        }
    }
}
