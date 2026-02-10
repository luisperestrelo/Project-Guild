using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Gathering;
using ProjectGuild.Simulation.Items;
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
        public ItemRegistry ItemRegistry { get; private set; }

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

            // Populate item registry from config
            ItemRegistry = new ItemRegistry();
            foreach (var itemDef in Config.ItemDefinitions)
                ItemRegistry.Register(itemDef);

            foreach (var def in starterDefinitions)
            {
                var runner = RunnerFactory.CreateFromDefinition(def, hubNodeId, Config.InventorySize);
                State.Runners.Add(runner);
                Events.Publish(new RunnerCreated
                {
                    RunnerId = runner.Id,
                    RunnerName = runner.Name,
                });
            }

            //Random rnd = new Random();
            //var newRunner = RunnerFactory.Create(rnd, Config);
            //State.Runners.Add(newRunner);
            //Events.Publish(new RunnerCreated
            //{
            //    RunnerId = newRunner.Id,
            //    RunnerName = newRunner.Name,
            //});

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
                //new RunnerFactory.RunnerDefinition{ Name = "Bob"}.WithSkill(SkillType.Athletics, 17, true).WithSkill(SkillType.Magic, 17),
                //new RunnerFactory.RunnerDefinition{ Name = RunnerFactory.GenerateName(new Random(123), Config) } //TODO: do this another way, make GenerateName private

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

                case RunnerState.Gathering:
                    TickGathering(runner);
                    break;

                // Other states that are TODO
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

                // Check if this runner is mid-gathering auto-return loop
                HandleGatheringArrival(runner, arrivedNodeId);
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

        // ─── Gathering ─────────────────────────────────────────────

        /// <summary>
        /// Command a runner to start gathering at their current node.
        /// Runner must be Idle and standing at a gathering-type node.
        /// </summary>
        /// <returns>True if gathering started, false if invalid.</returns>
        public bool CommandGather(string runnerId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null || runner.State != RunnerState.Idle) return false;

            var node = State.Map.GetNode(runner.CurrentNodeId);
            if (node == null) return false;

            var gatherableConfig = Config.GetGatherableConfig(node.Type);
            if (gatherableConfig == null) return false;

            float ticksRequired = CalculateTicksRequired(runner, gatherableConfig);

            runner.State = RunnerState.Gathering;
            runner.Gathering = new GatheringState
            {
                NodeId = runner.CurrentNodeId,
                TickAccumulator = 0f,
                TicksRequired = ticksRequired,
                SubState = GatheringSubState.Gathering,
            };

            Events.Publish(new GatheringStarted
            {
                RunnerId = runner.Id,
                NodeId = runner.CurrentNodeId,
                ItemId = gatherableConfig.ProducedItemId,
                Skill = gatherableConfig.RequiredSkill,
            });

            return true;
        }

        /// <summary>
        /// Calculate how many ticks it takes for a runner to gather one item.
        /// Formula: (GlobalGatheringSpeedMultiplier * BaseTicksToGather) / (1 + (effectiveLevel - 1) * GatheringSkillSpeedPerLevel)
        /// </summary>
        private float CalculateTicksRequired(Runner runner, GatherableConfig gatherable)
        {
            float effectiveLevel = runner.GetEffectiveLevel(gatherable.RequiredSkill, Config);
            float divisor = 1f + (effectiveLevel - 1f) * Config.GatheringSkillSpeedPerLevel;
            return (Config.GlobalGatheringSpeedMultiplier * gatherable.BaseTicksToGather) / divisor;
        }

        private void TickGathering(Runner runner)
        {
            if (runner.Gathering == null || runner.Gathering.SubState != GatheringSubState.Gathering)
                return;

            var node = State.Map.GetNode(runner.Gathering.NodeId);
            if (node == null) return;

            var gatherableConfig = Config.GetGatherableConfig(node.Type);
            if (gatherableConfig == null) return;

            // Always compute from current stats — buffs, level-ups, gear changes
            // are reflected immediately without explicit recalculation calls.
            // TicksRequired is also stored on GatheringState for UI progress display.
            float ticksRequired = CalculateTicksRequired(runner, gatherableConfig);
            runner.Gathering.TicksRequired = ticksRequired;
            runner.Gathering.TickAccumulator += 1f;

            if (runner.Gathering.TickAccumulator >= ticksRequired)
            {
                runner.Gathering.TickAccumulator -= ticksRequired;

                // Produce item
                var itemDef = ItemRegistry.Get(gatherableConfig.ProducedItemId);
                bool added = runner.Inventory.TryAdd(itemDef, 1);

                // Award XP (even if inventory was full — the work was still done)
                var skill = runner.GetSkill(gatherableConfig.RequiredSkill);
                bool leveledUp = skill.AddXp(gatherableConfig.BaseXpPerGather, Config);

                if (leveledUp)
                {
                    Events.Publish(new RunnerSkillLeveledUp
                    {
                        RunnerId = runner.Id,
                        Skill = gatherableConfig.RequiredSkill,
                        NewLevel = skill.Level,
                    });
                }

                if (added)
                {
                    Events.Publish(new ItemGathered
                    {
                        RunnerId = runner.Id,
                        ItemId = gatherableConfig.ProducedItemId,
                        InventoryFreeSlots = runner.Inventory.FreeSlots,
                    });
                }

                // Check if inventory is now full — begin auto-return to hub
                if (runner.Inventory.IsFull(itemDef))
                {
                    Events.Publish(new InventoryFull { RunnerId = runner.Id });
                    BeginAutoReturn(runner);
                }
            }
        }

        // ─── Auto-Return Loop ────────────────────────────────────────

        /// <summary>
        /// Start a runner traveling without the guard clauses of CommandTravel.
        /// Used internally for auto-return travel during gathering loops.
        /// </summary>
        private void StartTravelInternal(Runner runner, string targetNodeId)
        {
            float distance = State.Map.FindPath(runner.CurrentNodeId, targetNodeId, out _);
            if (distance < 0) return; // shouldn't happen, but safety

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
            Events.Publish(new RunnerStartedTravel
            {
                RunnerId = runner.Id,
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                EstimatedDurationSeconds = distance / speed,
            });
        }

        /// <summary>
        /// Inventory is full — send the runner to hub to deposit.
        /// GatheringState is preserved so the runner knows where to return.
        /// </summary>
        private void BeginAutoReturn(Runner runner)
        {
            runner.Gathering.SubState = GatheringSubState.TravelingToBank;

            // Find the hub node (first Hub-type node in the map)
            string hubNodeId = null;
            for (int i = 0; i < State.Map.Nodes.Count; i++)
            {
                if (State.Map.Nodes[i].Type == NodeType.Hub)
                {
                    hubNodeId = State.Map.Nodes[i].Id;
                    break;
                }
            }

            if (hubNodeId == null || runner.CurrentNodeId == hubNodeId)
            {
                // Already at hub (edge case) — deposit immediately and resume
                DepositAndReturn(runner);
                return;
            }

            StartTravelInternal(runner, hubNodeId);
        }

        /// <summary>
        /// Called when a runner arrives at a node and has an active gathering loop.
        /// Handles deposit at hub and return-to-node transitions.
        /// </summary>
        private void HandleGatheringArrival(Runner runner, string arrivedNodeId)
        {
            if (runner.Gathering == null) return;

            if (runner.Gathering.SubState == GatheringSubState.TravelingToBank)
            {
                DepositAndReturn(runner);
            }
            else if (runner.Gathering.SubState == GatheringSubState.TravelingToNode)
            {
                ResumeGathering(runner);
            }
        }

        /// <summary>
        /// Deposit all items at the hub bank, then start traveling back to the gathering node.
        /// </summary>
        private void DepositAndReturn(Runner runner)
        {
            int itemCount = runner.Inventory.Slots.Count;
            State.Bank.DepositAll(runner.Inventory);

            Events.Publish(new RunnerDeposited
            {
                RunnerId = runner.Id,
                ItemsDeposited = itemCount,
            });

            runner.Gathering.SubState = GatheringSubState.TravelingToNode;

            if (runner.CurrentNodeId == runner.Gathering.NodeId)
            {
                // Already at the gathering node (shouldn't normally happen, but handle it)
                ResumeGathering(runner);
                return;
            }

            StartTravelInternal(runner, runner.Gathering.NodeId);
        }

        /// <summary>
        /// Runner has returned to the gathering node — resume gathering.
        /// Recalculates ticks required in case the skill leveled up during the trip.
        /// </summary>
        private void ResumeGathering(Runner runner)
        {
            var node = State.Map.GetNode(runner.Gathering.NodeId);
            var gatherableConfig = Config.GetGatherableConfig(node.Type);

            runner.State = RunnerState.Gathering;
            runner.Gathering.SubState = GatheringSubState.Gathering;
            runner.Gathering.TickAccumulator = 0f;
            runner.Gathering.TicksRequired = CalculateTicksRequired(runner, gatherableConfig);

            Events.Publish(new GatheringStarted
            {
                RunnerId = runner.Id,
                NodeId = runner.Gathering.NodeId,
                ItemId = gatherableConfig.ProducedItemId,
                Skill = gatherableConfig.RequiredSkill,
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
