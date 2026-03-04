using System;
using System.Collections;
using System.Collections.Generic;
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
        /// <summary>
        /// Fired at the end of Start() after the simulation, visuals, and UI are fully initialized.
        /// The LoadingSceneController listens for this to know when to fade out and unload.
        /// When playing directly from SampleScene (no LoadingScene), this fires with no listeners.
        /// </summary>
        public static event Action OnWorldReady;

        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private VisualSyncSystem _visualSyncSystem;
        [SerializeField] private WorldSceneManager _worldSceneManager;
        [SerializeField] private NavMeshTravelPathCache _navMeshPathCache;
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private SceneTransitionOverlay _sceneTransitionOverlay;
        [SerializeField] private UI.UIManager _uiManager;

        private InputAction _clickAction;
        private bool _clickedThisFrame;
        private int _runnerLayerMask;
        private int _bankLayerMask;

        private SaveManager _saveManager;
        private Coroutine _activeWorldBuild;

        public SaveManager SaveManager => _saveManager;

        private void Awake()
        {
            _clickAction = new InputAction("Click", InputActionType.Button,
                binding: "<Mouse>/leftButton");

            int runnerLayer = LayerMask.NameToLayer("Runners");
            if (runnerLayer < 0) Debug.LogError("[GameBootstrapper] 'Runners' layer not found. Add it in Project Settings > Tags and Layers.");
            _runnerLayerMask = 1 << runnerLayer;

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
            if (_worldSceneManager == null)
                _worldSceneManager = FindAnyObjectByType<WorldSceneManager>();
            if (_cameraController == null)
                _cameraController = FindAnyObjectByType<CameraController>();
            if (_uiManager == null)
                _uiManager = FindAnyObjectByType<UI.UIManager>();
            if (_navMeshPathCache == null)
                _navMeshPathCache = FindAnyObjectByType<NavMeshTravelPathCache>();
            if (_sceneTransitionOverlay == null)
                _sceneTransitionOverlay = FindAnyObjectByType<SceneTransitionOverlay>();

            // Ensure SampleScene is the active scene so any instantiated objects
            // (runner visuals, etc.) end up here — not in LoadingScene.
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(gameObject.scene);

            _saveManager = new SaveManager();

            _activeWorldBuild = StartCoroutine(BuildAndRevealWorld(
                () => _simulationRunner.StartNewGame(),
                () => OnWorldReady?.Invoke()));
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
        /// Tears down UI and visuals, then kicks off async rebuild.
        /// Screen stays black (via SceneTransitionOverlay) until scenes are loaded
        /// and visuals are positioned correctly.
        /// </summary>
        private void ReloadWorld(Action loadAction)
        {
            if (_sceneTransitionOverlay != null)
                _sceneTransitionOverlay.Show();

            if (_uiManager != null)
                _uiManager.Teardown();
            if (_worldSceneManager != null)
                _worldSceneManager.ClearAll();
            if (_navMeshPathCache != null)
                _navMeshPathCache.ClearAll();

            if (_activeWorldBuild != null)
                StopCoroutine(_activeWorldBuild);

            _activeWorldBuild = StartCoroutine(BuildAndRevealWorld(
                loadAction,
                () => _sceneTransitionOverlay?.FadeIn()));
        }

        /// <summary>
        /// Shared initialization sequence for both first boot and mid-game reload.
        /// Sets up the simulation, waits for all node scenes to finish loading (async),
        /// then builds visuals at correct positions and calls onReady to reveal the world.
        /// </summary>
        private IEnumerator BuildAndRevealWorld(Action gameStateAction, Action onReady)
        {
            gameStateAction();

            if (_navMeshPathCache != null)
                _simulationRunner.Simulation.PathDistanceProvider = _navMeshPathCache;
            else
                Debug.LogWarning("[GameBootstrapper] No NavMeshTravelPathCache found. Travel distances will use Euclidean fallback.");

            var nodeGeoProvider = FindAnyObjectByType<NodeGeometryProvider>();
            if (nodeGeoProvider != null)
                _simulationRunner.Simulation.NodeGeometryProvider = nodeGeoProvider;

            if (_worldSceneManager != null)
                _worldSceneManager.Initialize();
            if (_navMeshPathCache != null)
                _navMeshPathCache.Initialize();

            // Wait for node scenes to finish loading so runners are placed at
            // correct in-scene positions from the very first rendered frame.
            yield return WaitForNodeScenesToLoad();

            _visualSyncSystem.BuildWorld();
            SelectRunner(0);

            if (_uiManager != null)
                _uiManager.Initialize();

            _activeWorldBuild = null;
            onReady?.Invoke();
        }

        /// <summary>
        /// Kicks off async loading for all node scenes where runners are located,
        /// then yields until every scene has finished loading.
        /// </summary>
        private IEnumerator WaitForNodeScenesToLoad()
        {
            if (_worldSceneManager == null) yield break;
            var sim = _simulationRunner.Simulation;
            if (sim?.CurrentGameState?.Runners == null) yield break;

            var nodesWithRunners = new HashSet<string>();
            foreach (var runner in sim.CurrentGameState.Runners)
            {
                if (runner.State != RunnerState.Traveling && !string.IsNullOrEmpty(runner.CurrentNodeId))
                    nodesWithRunners.Add(runner.CurrentNodeId);
            }

            if (nodesWithRunners.Count == 0) yield break;

            int pendingScenes = nodesWithRunners.Count;
            foreach (var nodeId in nodesWithRunners)
                _worldSceneManager.EnsureNodeSceneLoaded(nodeId, () => pendingScenes--);

            while (pendingScenes > 0)
                yield return null;
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
                    TryPickBank();
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
    }
}
