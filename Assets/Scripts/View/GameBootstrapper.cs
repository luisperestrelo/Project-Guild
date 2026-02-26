using System;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;
using ProjectGuild.View.Runners;

namespace ProjectGuild.View
{
    /// <summary>
    /// Entry point that wires up the simulation, visuals, and 3D picking.
    /// Attach to a GameObject in the scene along with SimulationRunner
    /// and VisualSyncSystem.
    /// </summary>
    public class GameBootstrapper : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private VisualSyncSystem _visualSyncSystem;
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private UI.UIManager _uiManager;

        private InputAction _clickAction;
        private bool _clickedThisFrame;
        private int _runnerLayerMask;
        private int _nodeLayerMask;
        private int _bankLayerMask;

        private SaveManager _saveManager;

        public SaveManager SaveManager => _saveManager;

        private void Awake()
        {
            _clickAction = new InputAction("Click", InputActionType.Button,
                binding: "<Mouse>/leftButton");

            int runnerLayer = LayerMask.NameToLayer("Runners");
            if (runnerLayer < 0) Debug.LogError("[GameBootstrapper] 'Runners' layer not found. Add it in Project Settings > Tags and Layers.");
            _runnerLayerMask = 1 << runnerLayer;

            int nodeLayer = LayerMask.NameToLayer("Nodes");
            if (nodeLayer < 0) Debug.LogError("[GameBootstrapper] 'Nodes' layer not found. Add it in Project Settings > Tags and Layers.");
            _nodeLayerMask = 1 << nodeLayer;

            int bankLayer = LayerMask.NameToLayer("Bank");
            if (bankLayer < 0) Debug.LogError("[GameBootstrapper] 'Bank' layer not found. Add it in Project Settings > Tags and Layers.");
            _bankLayerMask = 1 << bankLayer;
        }

        private void OnEnable() => _clickAction.Enable();
        private void OnDisable() => _clickAction.Disable();

        private void Start()
        {
            if (_simulationRunner == null)
                _simulationRunner = GetComponent<SimulationRunner>();
            if (_visualSyncSystem == null)
                _visualSyncSystem = GetComponent<VisualSyncSystem>();
            if (_cameraController == null)
                _cameraController = FindAnyObjectByType<CameraController>();
            if (_uiManager == null)
                _uiManager = FindAnyObjectByType<UI.UIManager>();

            _saveManager = new SaveManager();

            // Start a new game
            _simulationRunner.StartNewGame();

            // Build the visual world
            _visualSyncSystem.BuildWorld();

            // Point camera at first runner
            SelectRunner(0);

            // Initialize real UI (after sim + visuals are ready)
            if (_uiManager != null)
                _uiManager.Initialize();
        }

        // ─── Runner Selection + Camera ───────────────────────────────

        private int _selectedRunnerIndex = 0;

        private void SelectRunner(int index)
        {
            var sim = _simulationRunner.Simulation;
            if (sim == null || index < 0 || index >= sim.CurrentGameState.Runners.Count) return;

            _selectedRunnerIndex = index;
            var runner = sim.CurrentGameState.Runners[index];

            // Update real UI (portrait selection, details panel, camera)
            if (_uiManager != null)
            {
                _uiManager.SelectRunner(runner.Id);
            }
            else
            {
                // Fallback if UIManager not present
                var visual = _visualSyncSystem.GetRunnerVisual(runner.Id);
                if (_cameraController != null && visual != null)
                    _cameraController.SetTarget(visual);
            }
        }

        // ─── Save / Load / New Game ─────────────────────────────────

        /// <summary>
        /// Save current game state to the default save slot.
        /// </summary>
        public void SaveGame()
        {
            if (_saveManager == null || _simulationRunner?.Simulation == null) return;
            _saveManager.Save(_simulationRunner.Simulation.CurrentGameState, "save1");
        }

        /// <summary>
        /// Load a saved game. Tears down the current visual world and UI,
        /// loads state, rebuilds everything.
        /// </summary>
        public void LoadSavedGame()
        {
            if (_saveManager == null || !_saveManager.SaveExists("save1")) return;

            var state = _saveManager.Load("save1");
            if (state == null) return;

            ReloadWorld(() => _simulationRunner.LoadGame(state));
        }

        /// <summary>
        /// Start a fresh new game. Tears down the current visual world and UI,
        /// creates a new game, rebuilds everything.
        /// </summary>
        public void StartNewGameFromOptions()
        {
            ReloadWorld(() => _simulationRunner.StartNewGame());
        }

        /// <summary>
        /// Tears down UI and visuals, runs the provided game-state action
        /// (LoadGame or StartNewGame), then rebuilds visuals and re-initializes UI.
        /// </summary>
        private void ReloadWorld(Action loadAction)
        {
            // Tear down UI first (unsubscribes from sim events)
            if (_uiManager != null)
                _uiManager.Teardown();

            // Load or create new game state
            loadAction();

            // Rebuild visual world
            _visualSyncSystem.BuildWorld();

            // Point camera at first runner
            SelectRunner(0);

            // Re-initialize UI
            if (_uiManager != null)
                _uiManager.Initialize();
        }

        // ─── Input + 3D Picking ─────────────────────────────────────

        private void Update()
        {
            if (_clickAction.WasPressedThisFrame())
                _clickedThisFrame = true;
        }

        private void LateUpdate()
        {
            if (_clickedThisFrame)
            {
                _clickedThisFrame = false;

                if (_uiManager != null && _uiManager.IsPointerOverUI()) return;

                if (!TryPickRunner())
                    if (!TryPickBank())
                        TryPickNode();
            }
        }

        private bool TryPickRunner()
        {
            var sim = _simulationRunner.Simulation;
            if (sim == null || Camera.main == null) return false;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _runnerLayerMask))
                return false;

            var visual = hit.collider.GetComponentInParent<RunnerVisual>();
            if (visual == null) return false;
            // Find the runner index by ID
            for (int i = 0; i < sim.CurrentGameState.Runners.Count; i++)
            {
                if (sim.CurrentGameState.Runners[i].Id == visual.RunnerId)
                {
                    SelectRunner(i);
                    return true;
                }
            }
            return false;
        }

        private bool TryPickBank()
        {
            if (Camera.main == null || _uiManager == null) return false;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _bankLayerMask))
                return false;

            var marker = hit.collider.GetComponentInParent<BankMarker>();
            if (marker == null) return false;

            _uiManager.ToggleBankPanel();
            return true;
        }

        private void TryPickNode()
        {
            var sim = _simulationRunner.Simulation;
            if (sim == null || Camera.main == null) return;
            if (_uiManager == null || _uiManager.SelectedRunnerId == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _nodeLayerMask))
                return;

            var nodeMarker = hit.collider.GetComponentInParent<NodeMarker>();
            if (nodeMarker == null) return;

            string nodeId = nodeMarker.NodeId;
            string hubId = sim.CurrentGameState.Map.HubNodeId;

            // Skip hub node (no Work At on hub)
            if (nodeId == hubId) return;

            _uiManager.ShowNodeClickConfirmation(
                _uiManager.SelectedRunnerId, nodeId);
        }
    }
}
