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

        public string SelectedRunnerId => _selectedRunnerId;
        public GameSimulation Simulation => _simulationRunner?.Simulation;

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
