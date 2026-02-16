using UnityEngine;
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

        [Header("UI Assets")]
        [SerializeField] private VisualTreeAsset _mainLayoutAsset;
        [SerializeField] private VisualTreeAsset _runnerPortraitAsset;
        [SerializeField] private VisualTreeAsset _runnerDetailsPanelAsset;
        [SerializeField] private PanelSettings _panelSettings;

        private UIDocument _uiDocument;
        private RunnerPortraitBarController _portraitBarController;
        private RunnerDetailsPanelController _detailsPanelController;

        private string _selectedRunnerId;
        private bool _initialized;

        // ─── Tooltip ─────────────────────────────────────
        private VisualElement _tooltip;
        private Label _tooltipLabel;

        public string SelectedRunnerId => _selectedRunnerId;
        public GameSimulation Simulation => _simulationRunner?.Simulation;

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

            // Details panel — instantiate the template into the container
            var detailsContainer = root.Q("details-panel-container");
            var detailsInstance = _runnerDetailsPanelAsset.Instantiate();
            detailsContainer.Add(detailsInstance);
            _detailsPanelController = new RunnerDetailsPanelController(detailsInstance, this);

            // Populate portraits for runners that already exist
            foreach (var runner in Simulation.CurrentGameState.Runners)
                _portraitBarController.AddPortrait(runner.Id);

            // Subscribe to events
            _simulationRunner.Events.Subscribe<SimulationTickCompleted>(OnSimulationTick);
            _simulationRunner.Events.Subscribe<RunnerCreated>(OnRunnerCreated);

            // Build tooltip element (shared across all UI)
            BuildTooltip(root);

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

            // Move camera to selected runner's visual
            if (_visualSyncSystem != null && _cameraController != null)
            {
                var visual = _visualSyncSystem.GetRunnerVisual(runnerId);
                if (visual != null)
                    _cameraController.SetTarget(visual);
            }
        }

        private void OnSimulationTick(SimulationTickCompleted evt)
        {
            _portraitBarController?.Refresh();
            _detailsPanelController?.Refresh();
        }

        private void OnRunnerCreated(RunnerCreated evt)
        {
            _portraitBarController?.AddPortrait(evt.RunnerId);
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
                // Position tooltip offset from pointer (below-right)
                _tooltip.style.left = evt.position.x + 12;
                _tooltip.style.top = evt.position.y + 16;
            });
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
