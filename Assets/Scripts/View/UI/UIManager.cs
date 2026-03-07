using System;
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
        [SerializeField] private VisualTreeAsset _optionsPanelAsset;
        [SerializeField] private VisualTreeAsset _strategicMapPanelAsset;
        [SerializeField] private VisualTreeAsset _abilitiesPanelAsset;
        [SerializeField] private VisualTreeAsset _tutorialOverlayAsset;
        [SerializeField] private PanelSettings _panelSettings;

        [Header("Scene References (Optional)")]
        [SerializeField] private GameBootstrapper _gameBootstrapper;

        private UIDocument _uiDocument;
        private RunnerPortraitBarController _portraitBarController;
        private RunnerDetailsPanelController _detailsPanelController;
        private AutomationPanelController _automationPanelController;
        private BankPanelController _bankPanelController;
        private CraftingPanelController _craftingPanelController;
        private OptionsPanelController _optionsPanelController;
        private ResourceBarController _resourceBarController;
        private LogPanelContainerController _logPanelContainerController;
        private LogbookPanelController _logbookPanelController;
        private StrategicMapPanelController _strategicMapPanelController;
        private AbilitiesPanelController _abilitiesPanelController;
        private TutorialController _tutorialController;

        private Button _worldButton;

        private PlayerPreferences _preferences;

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
        private System.Func<string> _activeTooltipGetText;

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
        public PlayerPreferences Preferences => _preferences;
        public SaveManager SaveManager => _gameBootstrapper?.SaveManager;

        /// <summary>
        /// Returns true if a text field currently has keyboard focus.
        /// Used to suppress hotkeys (like H for Guild Hall) while the player is typing.
        /// </summary>
        public bool IsTextFieldFocused()
        {
            if (_uiDocument == null) return false;
            var focused = _uiDocument.rootVisualElement?.focusController?.focusedElement;
            return focused is TextField;
        }

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
            if (_craftingPanelController?.IsOpen == true) return true;
            if (_optionsPanelController?.IsOpen == true) return true;
            if (_strategicMapPanelController?.IsOpen == true) return true;
            if (_abilitiesPanelController?.IsOpen == true) return true;
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
        /// Get the icon sprite for an ability by its ID. Returns null if no icon is assigned.
        /// </summary>
        public Sprite GetAbilityIcon(string abilityId)
        {
            if (_simulationRunner?.AbilityIcons == null) return null;
            return _simulationRunner.AbilityIcons.TryGetValue(abilityId, out var icon) ? icon : null;
        }

        /// <summary>
        /// Initialize the UI after the simulation and visual world are ready.
        /// Called by GameBootstrapper at the end of Start().
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            // Load player preferences (UI-layer, not game state)
            _preferences ??= PlayerPreferences.Load();

            // Resolve GameBootstrapper if not wired in Inspector
            if (_gameBootstrapper == null)
                _gameBootstrapper = FindAnyObjectByType<GameBootstrapper>();

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

            // Crafting panel (overlay, built programmatically)
            _craftingPanelController = new CraftingPanelController(root, this);

            // Options panel (overlay)
            if (_optionsPanelAsset != null)
            {
                var optionsInstance = _optionsPanelAsset.Instantiate();
                optionsInstance.style.position = Position.Absolute;
                optionsInstance.style.left = 0;
                optionsInstance.style.top = 0;
                optionsInstance.style.right = 0;
                optionsInstance.style.bottom = 0;
                optionsInstance.pickingMode = PickingMode.Ignore;
                root.Add(optionsInstance);
                _optionsPanelController = new OptionsPanelController(optionsInstance, this);

                // Toggle button (top-left, next to Automation)
                var optionsBtn = new Button(() => _optionsPanelController.Toggle());
                optionsBtn.text = "Options";
                optionsBtn.AddToClassList("options-toggle-button");
                root.Add(optionsBtn);
            }

            // Guild Hall button (top-left, next to Options)
            {
                var guildHallBtn = new Button(() => JumpToGuildHall());
                guildHallBtn.text = "Guild Hall";
                guildHallBtn.AddToClassList("guildhall-toggle-button");
                root.Add(guildHallBtn);
            }

            // Crafting button (top-left, next to Guild Hall)
            {
                var craftingBtn = new Button(() => ToggleCraftingPanel());
                craftingBtn.text = "Crafting";
                craftingBtn.AddToClassList("guildhall-toggle-button");
                craftingBtn.style.left = 420;
                root.Add(craftingBtn);
            }

            // Abilities panel (overlay)
            if (_abilitiesPanelAsset != null)
            {
                var abilitiesInstance = _abilitiesPanelAsset.Instantiate();
                abilitiesInstance.style.position = Position.Absolute;
                abilitiesInstance.style.left = 0;
                abilitiesInstance.style.top = 0;
                abilitiesInstance.style.right = 0;
                abilitiesInstance.style.bottom = 0;
                abilitiesInstance.pickingMode = PickingMode.Ignore;
                root.Add(abilitiesInstance);
                _abilitiesPanelController = new AbilitiesPanelController(abilitiesInstance, this);
            }

            // Strategic Map panel (overlay)
            if (_strategicMapPanelAsset != null)
            {
                var mapInstance = _strategicMapPanelAsset.Instantiate();
                mapInstance.style.position = Position.Absolute;
                mapInstance.style.left = 0;
                mapInstance.style.top = 0;
                mapInstance.style.right = 0;
                mapInstance.style.bottom = 0;
                mapInstance.pickingMode = PickingMode.Ignore;
                root.Add(mapInstance);
                _strategicMapPanelController = new StrategicMapPanelController(
                    mapInstance, this, () =>
                    {
                        SetNormalGameUiVisible(true);
                        var pb = _uiDocument?.rootVisualElement?.Q("portrait-bar-container");
                        if (pb != null) pb.style.backgroundColor = StyleKeyword.Null;
                        _tutorialController?.OnStrategicMapClosed();
                    });

                // Toggle button (top-left, next to Guild Hall)
                var mapBtn = new Button(() => ToggleStrategicMap());
                mapBtn.text = "World";
                mapBtn.AddToClassList("strategicmap-toggle-button");
                root.Add(mapBtn);
                _worldButton = mapBtn;
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

            // Tutorial overlay (on top of everything)
            if (_tutorialOverlayAsset != null)
            {
                var tutorialInstance = _tutorialOverlayAsset.Instantiate();
                tutorialInstance.style.position = Position.Absolute;
                tutorialInstance.style.left = 0;
                tutorialInstance.style.top = 0;
                tutorialInstance.style.right = 0;
                tutorialInstance.style.bottom = 0;
                tutorialInstance.pickingMode = PickingMode.Ignore;
                root.Add(tutorialInstance);
                _tutorialController = new TutorialController(tutorialInstance, this);
            }

            // Select first runner
            var runners = Simulation.CurrentGameState.Runners;
            if (runners.Count > 0)
                SelectRunner(runners[0].Id);

            _initialized = true;
        }

        public event System.Action<string> OnRunnerSelected;

        public void SelectRunner(string runnerId)
        {
            _selectedRunnerId = runnerId;
            OnRunnerSelected?.Invoke(runnerId);
            _portraitBarController?.SetSelectedRunner(runnerId);
            _detailsPanelController?.ShowRunner(runnerId);
            _logbookPanelController?.OnRunnerSelected(runnerId);

            // If strategic map is open, center on the runner's dot
            if (_strategicMapPanelController?.IsOpen == true)
                _strategicMapPanelController.CenterOnRunner(runnerId);

            // Move camera to selected runner's visual (even with map open, so closing reveals the right view)
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

        /// <summary>
        /// Open the Automation panel for a newly created item that should be assigned
        /// to a runner when the user finishes editing. Shows an "Assign to [Runner]" button.
        /// </summary>
        public void OpenAutomationPanelToItemForNewAssignment(string tabType, string itemId, string runnerId)
        {
            _automationPanelController?.OpenToItemForNewAssignment(tabType, itemId, runnerId);
        }

        /// <summary>
        /// Open the Automation panel in list/browse mode with "Assign to [Runner]" context.
        /// Used by CHANGE button — player wants to pick a different item from the library.
        /// </summary>
        public void OpenAutomationPanelForChangeAssignment(string tabType, string runnerId)
        {
            _automationPanelController?.OpenForChangeAssignment(tabType, runnerId);
        }

        // ─── Abilities Panel ─────────────────────────────────

        /// <summary>
        /// Open the Abilities panel for the currently selected runner.
        /// </summary>
        public void OpenAbilitiesPanel()
        {
            _abilitiesPanelController?.Open(_selectedRunnerId);
        }

        /// <summary>
        /// Open the Abilities panel in pick mode. Clicking an ability invokes the callback.
        /// </summary>
        public void OpenAbilitiesPanelPickMode(Action<string> onPick)
        {
            _abilitiesPanelController?.OpenPickMode(onPick, _selectedRunnerId);
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

        // ─── Guild Hall (Hub Scene Camera) ─────────────────

        /// <summary>
        /// Jump the camera to the Guild Hall scene. Called by the Guild Hall button
        /// and H hotkey. Enters hub-scene camera mode without needing a runner there.
        /// </summary>
        public void JumpToGuildHall()
        {
            var hubNodeId = Simulation?.CurrentGameState?.Map?.HubNodeId;
            if (hubNodeId == null || _cameraController == null) return;

            _cameraController.EnterHubSceneMode(hubNodeId);
        }

        // ─── Bank Panel ─────────────────────────────────────

        public void ToggleBankPanel() => _bankPanelController?.Toggle();
        public void OpenBankPanel() => _bankPanelController?.Open();

        // ─── Crafting Panel ─────────────────────────────────

        public void ToggleCraftingPanel() => _craftingPanelController?.Toggle();
        public void OpenCraftingPanel() => _craftingPanelController?.Open();

        public Runner GetSelectedRunner()
        {
            if (_selectedRunnerId == null) return null;
            return Simulation?.FindRunner(_selectedRunnerId);
        }

        // ─── Options Panel ──────────────────────────────────

        public void ToggleOptionsPanel() => _optionsPanelController?.Toggle();

        // ─── Automation Panel ────────────────────────────

        public void ToggleAutomationPanel() => _automationPanelController?.Toggle();

        // ─── Strategic Map ──────────────────────────────

        public bool IsStrategicMapOpen => _strategicMapPanelController?.IsOpen == true;

        /// <summary>
        /// Returns the World (map toggle) button element for tutorial highlighting.
        /// </summary>
        public VisualElement GetWorldButtonElement() => _worldButton;

        public VisualElement GetPortraitElement(string runnerId) =>
            _portraitBarController?.GetPortraitElement(runnerId);

        public VisualElement GetAutomationTabButton() => _detailsPanelController?.GetAutomationTabButton();

        public void SelectRunnerAndOpenTab(string runnerId, string tab)
        {
            SelectRunner(runnerId);
            _detailsPanelController?.SwitchToTab(tab);
        }

        /// <summary>
        /// Returns a strategic map node element by node ID for tutorial highlighting.
        /// </summary>
        public VisualElement GetStrategicMapNodeElement(string nodeId) =>
            _strategicMapPanelController?.GetNodeElement(nodeId);

        /// <summary>
        /// Force the strategic map to rebuild its nodes (e.g. when tutorial node gating changes).
        /// </summary>
        public void InvalidateStrategicMapNodes() =>
            _strategicMapPanelController?.InvalidateNodes();

        public void ToggleStrategicMap()
        {
            if (_strategicMapPanelController == null) return;

            if (_strategicMapPanelController.IsOpen)
            {
                _strategicMapPanelController.Close();
                // onClosed callback restores UI via SetNormalGameUiVisible(true) + tutorial notification
            }
            else
            {
                // Close other overlay panels first
                _automationPanelController?.Close();
                _bankPanelController?.Close();
                _optionsPanelController?.Close();

                SetNormalGameUiVisible(false);
                _strategicMapPanelController.Open();
                _tutorialController?.OnStrategicMapOpened();

                // Dark bg on portrait bar so the 3D scene doesn't peek through
                var portraitBar = _uiDocument.rootVisualElement.Q("portrait-bar-container");
                if (portraitBar != null)
                    portraitBar.style.backgroundColor =
                        new StyleColor(new Color(0.03f, 0.04f, 0.07f, 1f));
            }
        }

        /// <summary>
        /// Show or hide normal game UI panels. Portrait bar stays visible (needed for
        /// runner selection while the strategic map is open).
        /// </summary>
        private void SetNormalGameUiVisible(bool visible)
        {
            if (_uiDocument == null) return;
            var root = _uiDocument.rootVisualElement;
            if (root == null) return;

            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            var detailsContainer = root.Q("details-panel-container");
            if (detailsContainer != null) detailsContainer.style.display = display;

            var logContainer = root.Q("log-panel-container");
            if (logContainer != null) logContainer.style.display = display;

            var logbookContainer = root.Q("logbook-panel-container");
            if (logbookContainer != null) logbookContainer.style.display = display;

            var resourceBar = root.Q("resource-bar-container");
            if (resourceBar != null) resourceBar.style.display = display;

            // Toggle buttons (hide when map open, except the Map button itself)
            foreach (var btn in root.Query(className: "automation-toggle-button").ToList())
                btn.style.display = display;
            foreach (var btn in root.Query(className: "options-toggle-button").ToList())
                btn.style.display = display;
            foreach (var btn in root.Query(className: "guildhall-toggle-button").ToList())
                btn.style.display = display;
            foreach (var btn in root.Query(className: "strategicmap-toggle-button").ToList())
                btn.style.display = display;
        }

        // ─── Save / Load delegation ─────────────────────────

        public void RequestSaveGame() => _gameBootstrapper?.SaveGame();
        public void RequestLoadGame() => _gameBootstrapper?.LoadSavedGame();
        public void RequestNewGame() => _gameBootstrapper?.StartNewGameFromOptions();

        // ─── Teardown (for reload) ──────────────────────────

        /// <summary>
        /// Unsubscribes from sim events, clears the visual tree, and resets state
        /// so Initialize() can be called again after a game reload.
        /// </summary>
        public void Teardown()
        {
            if (!_initialized) return;

            // Unsubscribe from events
            if (_simulationRunner?.Events != null)
            {
                _simulationRunner.Events.Unsubscribe<SimulationTickCompleted>(OnSimulationTick);
                _simulationRunner.Events.Unsubscribe<RunnerCreated>(OnRunnerCreated);
            }

            // Clear all controller references
            _portraitBarController = null;
            _detailsPanelController = null;
            _automationPanelController = null;
            _bankPanelController = null;
            _craftingPanelController = null;
            _optionsPanelController = null;
            _resourceBarController = null;
            _logPanelContainerController = null;
            _logbookPanelController = null;
            _strategicMapPanelController = null;
            _tutorialController?.Dispose();
            _tutorialController = null;
            _tickRefreshables.Clear();

            // Reset pointer tracking
            _isPointerOverDetailsPanel = false;
            _isPointerOverPortraitBar = false;
            _isPointerOverLogPanel = false;
            _isPointerOverLogbook = false;

            // Clear tooltip/context menu refs (will be rebuilt in Initialize)
            _tooltip = null;
            _tooltipLabel = null;
            _contextMenu = null;
            _contextMenuItems = null;
            DismissNodeClickConfirmation();

            // Clear the visual tree so Initialize rebuilds it fresh.
            // Reset visualTreeAsset to force UIDocument to re-instantiate on next assignment.
            if (_uiDocument != null && _uiDocument.rootVisualElement != null)
            {
                _uiDocument.rootVisualElement.Clear();
                _uiDocument.visualTreeAsset = null;
            }

            _selectedRunnerId = null;
            _initialized = false;
        }

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

            // ALT key listener: re-evaluate tooltip text when ALT is pressed/released
            // so ALT-expanded tooltips update instantly without requiring mouse movement.
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt)
                    RefreshActiveTooltip();
            });
            root.RegisterCallback<KeyUpEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt)
                    RefreshActiveTooltip();
            });
        }

        /// <summary>
        /// Register an element to show a tooltip on hover. The text callback
        /// is evaluated on each pointer enter so it stays current.
        /// </summary>
        public void RegisterTooltip(VisualElement element, System.Func<string> getText)
        {
            element.RegisterCallback<PointerEnterEvent>(evt =>
            {
                _activeTooltipGetText = getText;
                string text = getText();
                if (string.IsNullOrEmpty(text)) return;
                _tooltipLabel.text = text;
                _tooltip.style.display = DisplayStyle.Flex;
                PositionTooltip(evt.position.x, evt.position.y);
            });

            element.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                _tooltip.style.display = DisplayStyle.None;
                if (_activeTooltipGetText == getText)
                    _activeTooltipGetText = null;
            });

            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                _activeTooltipGetText = getText;
                string moveText = getText();
                if (string.IsNullOrEmpty(moveText))
                {
                    _tooltip.style.display = DisplayStyle.None;
                    return;
                }
                _tooltipLabel.text = moveText;
                _tooltip.style.display = DisplayStyle.Flex;
                PositionTooltip(evt.position.x, evt.position.y);
            });
        }

        /// <summary>
        /// Called when ALT key state changes. Re-evaluates the active tooltip text
        /// so ALT-expanded content appears/disappears immediately (PoE-style).
        /// </summary>
        private void RefreshActiveTooltip()
        {
            if (_activeTooltipGetText == null || _tooltip.style.display == DisplayStyle.None) return;
            string text = _activeTooltipGetText();
            if (string.IsNullOrEmpty(text))
            {
                _tooltip.style.display = DisplayStyle.None;
                return;
            }
            _tooltipLabel.text = text;
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
            Teardown();
        }
    }
}
