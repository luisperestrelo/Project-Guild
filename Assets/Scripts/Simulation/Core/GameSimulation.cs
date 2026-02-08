namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// The top-level simulation object. Owns the GameState and EventBus,
    /// and orchestrates ticking all sub-systems.
    ///
    /// This class is pure C# — no Unity dependency. The Bridge layer's
    /// SimulationRunner MonoBehaviour calls Tick() at a fixed rate.
    /// </summary>
    public class GameSimulation
    {
        public GameState State { get; private set; }
        public EventBus Events { get; private set; }

        /// <summary>
        /// Seconds per simulation tick. At 10 ticks/sec this is 0.1.
        /// Passed into sub-systems so they can scale time-based calculations.
        /// </summary>
        public float TickDeltaTime { get; private set; }

        public GameSimulation(float tickRate = 10f)
        {
            State = new GameState();
            Events = new EventBus();
            TickDeltaTime = 1f / tickRate;
        }

        /// <summary>
        /// Load an existing game state (for save/load).
        /// </summary>
        public void LoadState(GameState state)
        {
            State = state;
        }

        /// <summary>
        /// Initialize a new game. Creates starting runners at the hub.
        /// </summary>
        public void StartNewGame(string hubNodeId = "hub")
        {
            State = new GameState();

            var starters = RunnerFactory.CreateStartingRunners(hubNodeId);
            foreach (var runner in starters)
            {
                State.Runners.Add(runner);
                Events.Publish(new RunnerCreated
                {
                    RunnerId = runner.Id,
                    RunnerName = runner.Name,
                });
            }
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

        private void TickTravel(Runner runner)
        {
            if (runner.Travel == null) return;

            // Movement speed is based on Athletics skill
            // Base speed: 1.0 distance units/sec at level 1
            // Each level adds ~5% speed
            float athleticsLevel = runner.GetEffectiveLevel(SkillType.Athletics);
            float speed = 1.0f + (athleticsLevel - 1) * 0.05f;

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
        /// Command a runner to travel to a world node.
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

            float athleticsLevel = runner.GetEffectiveLevel(SkillType.Athletics);
            float speed = 1.0f + (athleticsLevel - 1) * 0.05f;
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
