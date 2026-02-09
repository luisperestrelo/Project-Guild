using System.Collections.Generic;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// The top-level simulation object. Owns the GameState, EventBus, and Config,
    /// and orchestrates ticking all sub-systems.
    ///
    /// This class is pure C# — no Unity dependency. The Bridge layer's
    /// SimulationRunner MonoBehaviour calls Tick() at a fixed rate.
    /// </summary>
    public class GameSimulation
    {
        public GameState State { get; private set; }
        public EventBus Events { get; private set; }
        public SimulationConfig Config { get; private set; }

        /// <summary>
        /// Seconds per simulation tick. At 10 ticks/sec this is 0.1.
        /// Passed into sub-systems so they can scale time-based calculations.
        /// </summary>
        public float TickDeltaTime { get; private set; }

        public GameSimulation(SimulationConfig config = null, float tickRate = 10f)
        {
            State = new GameState();
            Events = new EventBus();
            Config = config ?? new SimulationConfig();
            TickDeltaTime = 1f / tickRate;
        }

        /// <summary>
        /// Load an existing game state (for save/load).
        /// </summary>
        public void LoadState(GameState state)
        {
            State = state;
            State.Map?.Initialize();
        }

        /// <summary>
        /// Initialize a new game with hand-tuned starting runners.
        /// Takes an array of RunnerDefinitions for full control over starter balance — no RNG.
        /// </summary>
        public void StartNewGame(RunnerFactory.RunnerDefinition[] starterDefinitions,
            WorldMap map = null, string hubNodeId = "hub")
        {
            State = new GameState();
            State.Map = map ?? WorldMap.CreateStarterMap();

            foreach (var def in starterDefinitions)
            {
                var runner = RunnerFactory.CreateFromDefinition(def, hubNodeId);
                State.Runners.Add(runner);
                Events.Publish(new RunnerCreated
                {
                    RunnerId = runner.Id,
                    RunnerName = runner.Name,
                });
            }
        }

        /// <summary>
        /// Initialize a new game with default placeholder starters (for quick testing).
        /// </summary>
        public void StartNewGame(string hubNodeId = "hub")
        {
            StartNewGame(DefaultStarterDefinitions(), hubNodeId: hubNodeId);
        }

        /// <summary>
        /// Placeholder starting runners. Will be replaced with proper hand-tuned
        /// balance definitions — for now they're simple defaults to keep tests working.
        /// </summary>
        public static RunnerFactory.RunnerDefinition[] DefaultStarterDefinitions()
        {
            return new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Aldric Stormwind" }
                    .WithSkill(SkillType.Melee, 5)
                    .WithSkill(SkillType.Defence, 4)
                    .WithSkill(SkillType.Hitpoints, 5)
                    .WithSkill(SkillType.Athletics, 3),

                new RunnerFactory.RunnerDefinition { Name = "Lyra Foxglove" }
                    .WithSkill(SkillType.Ranged, 4)
                    .WithSkill(SkillType.Hitpoints, 3)
                    .WithSkill(SkillType.Mining, 3)
                    .WithSkill(SkillType.Athletics, 4),

                new RunnerFactory.RunnerDefinition { Name = "Corin Ashford" }
                    .WithSkill(SkillType.Magic, 4)
                    .WithSkill(SkillType.Restoration, 3)
                    .WithSkill(SkillType.Hitpoints, 3)
                    .WithSkill(SkillType.Athletics, 2),
            };
        }

        /// <summary>
        /// Advance the simulation by one tick. Called by the Bridge layer at a fixed rate.
        /// Each tick processes all runners and sub-systems.
        /// </summary>
        public void Tick()
        {
            State.TickCount++;
            State.TotalTimeElapsed += TickDeltaTime;

            // Process all runners
            for (int i = 0; i < State.Runners.Count; i++)
            {
                TickRunner(State.Runners[i]);
            }

            Events.Publish(new SimulationTickCompleted { TickNumber = State.TickCount });
        }

        private void TickRunner(Runner runner)
        {
            switch (runner.State)
            {
                case RunnerState.Idle:
                    // Nothing to do — waiting for task assignment or automation
                    break;

                case RunnerState.Traveling:
                    TickTravel(runner);
                    break;

                // Other states that are TODO
                // case RunnerState.Gathering: TickGathering(runner); break;
                // case RunnerState.Crafting: TickCrafting(runner); break;
                // case RunnerState.Fighting: TickCombat(runner); break;
            }
        }

        private float GetTravelSpeed(Runner runner)
        {
            float athleticsLevel = runner.GetEffectiveLevel(SkillType.Athletics, Config);
            return Config.BaseTravelSpeed + (athleticsLevel - 1) * Config.AthleticsSpeedPerLevel;
        }

        private void TickTravel(Runner runner)
        {
            if (runner.Travel == null) return;

            float speed = GetTravelSpeed(runner);
            runner.Travel.DistanceCovered += speed * TickDeltaTime;

            if (runner.Travel.DistanceCovered >= runner.Travel.TotalDistance)
            {
                // Arrived
                runner.CurrentNodeId = runner.Travel.ToNodeId;
                runner.State = RunnerState.Idle;

                string arrivedNodeId = runner.Travel.ToNodeId;
                runner.Travel = null;

                Events.Publish(new RunnerArrivedAtNode
                {
                    RunnerId = runner.Id,
                    NodeId = arrivedNodeId,
                });
            }
        }

        /// <summary>
        /// Command a runner to travel to a world node. Uses the world map to find
        /// the shortest path and calculate total distance. If the runner is already
        /// at the target node, does nothing.
        /// </summary>
        /// <returns>True if travel was started, false if impossible or unnecessary.</returns>
        public bool CommandTravel(string runnerId, string targetNodeId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null || runner.State == RunnerState.Dead) return false;
            if (runner.CurrentNodeId == targetNodeId) return false;

            float distance = State.Map.FindPath(runner.CurrentNodeId, targetNodeId, out var path);
            if (distance < 0) return false;

            string fromNode = runner.CurrentNodeId;
            runner.State = RunnerState.Traveling;
            runner.Travel = new TravelState
            {
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                TotalDistance = distance,
                DistanceCovered = 0f,
            };

            float speed = GetTravelSpeed(runner);
            float estimatedDuration = distance / speed;

            Events.Publish(new RunnerStartedTravel
            {
                RunnerId = runner.Id,
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                EstimatedDurationSeconds = estimatedDuration,
            });

            return true;
        }

        /// <summary>
        /// Command a runner to travel with an explicit distance (for testing).
        /// The single-argument overload that uses the world map is generally preferred.
        /// </summary>
        public void CommandTravel(string runnerId, string targetNodeId, float distance)
        {
            var runner = FindRunner(runnerId);
            if (runner == null || runner.State == RunnerState.Dead) return;

            string fromNode = runner.CurrentNodeId;
            runner.State = RunnerState.Traveling;
            runner.Travel = new TravelState
            {
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                TotalDistance = distance,
                DistanceCovered = 0f,
            };

            float speed = GetTravelSpeed(runner);
            float estimatedDuration = distance / speed;

            Events.Publish(new RunnerStartedTravel
            {
                RunnerId = runner.Id,
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                EstimatedDurationSeconds = estimatedDuration,
            });
        }

        public Runner FindRunner(string runnerId)
        {
            for (int i = 0; i < State.Runners.Count; i++)
            {
                if (State.Runners[i].Id == runnerId)
                    return State.Runners[i];
            }
            return null;
        }
    }
}
