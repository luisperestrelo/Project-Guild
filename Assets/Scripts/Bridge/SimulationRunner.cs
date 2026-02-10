using UnityEngine;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Data;

namespace ProjectGuild.Bridge
{
    /// <summary>
    /// The single entry point from Unity into the simulation layer.
    /// Ticks the GameSimulation at a fixed rate, independent of frame rate.
    ///
    /// This MonoBehaviour is the bridge between Unity's game loop and our
    /// pure C# simulation. It accumulates real time and ticks the simulation
    /// at the configured rate (default 10 ticks/sec).
    ///
    /// Other MonoBehaviours access the simulation through this component's
    /// public Simulation property.
    /// </summary>
    public class SimulationRunner : MonoBehaviour
    {
        [Header("Simulation Settings")]
        [Tooltip("Number of simulation ticks per second")]
        [SerializeField] private float _tickRate = 10f;

        [Tooltip("Maximum ticks to process per frame (prevents spiral of death)")]
        [SerializeField] private int _maxTicksPerFrame = 3;

        [Tooltip("Drag a SimulationConfig asset here. If left empty, default values are used.")]
        [SerializeField] private SimulationConfigAsset _configAsset;

        [Header("World")]
        [Tooltip("Drag a WorldMap asset here. If left empty, a default starter map is used.")]
        [SerializeField] private WorldMapAsset _worldMapAsset;

        public GameSimulation Simulation { get; private set; }

        /// <summary>
        /// Shorthand access to the EventBus for view-layer subscriptions.
        /// </summary>
        public EventBus Events => Simulation?.Events;

        private float _tickAccumulator;
        private float _tickInterval;

        private void Awake()
        {
            _tickInterval = 1f / _tickRate;
            var config = _configAsset != null ? _configAsset.ToConfig() : new SimulationConfig();
            Simulation = new GameSimulation(config, _tickRate);
        }

        /// <summary>
        /// Call this to start a new game.
        /// </summary>
        public void StartNewGame()
        {
            var map = _worldMapAsset != null ? _worldMapAsset.ToWorldMap() : null;
            string hubNodeId = map != null ? map.HubNodeId : "hub";
            Simulation.StartNewGame(GameSimulation.DefaultStarterDefinitions(), map, hubNodeId);
            Debug.Log($"[SimulationRunner] New game started with {Simulation.CurrentGameState.Runners.Count} runners.");
        }

        /// <summary>
        /// Load an existing game state.
        /// </summary>
        public void LoadGame(GameState state)
        {
            Simulation.LoadState(state);
            Debug.Log($"[SimulationRunner] Game loaded. Tick: {state.TickCount}, Runners: {state.Runners.Count}");
        }

        private void Update()
        {
            if (Simulation == null) return;

            _tickAccumulator += Time.deltaTime;

            int ticksThisFrame = 0;
            while (_tickAccumulator >= _tickInterval && ticksThisFrame < _maxTicksPerFrame)
            {
                Simulation.Tick();
                _tickAccumulator -= _tickInterval;
                ticksThisFrame++;
            }

            // If we hit the max ticks cap, discard remaining accumulated time
            // to prevent the simulation from trying to "catch up" indefinitely
            if (ticksThisFrame >= _maxTicksPerFrame && _tickAccumulator > _tickInterval)
            {
                _tickAccumulator = 0f;
            }
        }
    }
}
