using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Automation;
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
    ///
    /// The macro layer (Assignment + task steps) drives the gather→deposit→return loop.
    /// Micro rules evaluate within task steps (e.g. which resource to gather).
    /// Macro rules (Batch C) will change assignments based on conditions.
    /// </summary>
    public class GameSimulation
    {
        public GameState CurrentGameState { get; private set; }
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
            CurrentGameState = new GameState();
            Events = new EventBus();
            Config = config ?? new SimulationConfig();
            TickDeltaTime = 1f / tickRate;
        }

        /// <summary>
        /// Load an existing game state (for save/load).
        /// </summary>
        public void LoadState(GameState state)
        {
            CurrentGameState = state;
            CurrentGameState.Map?.Initialize();
        }

        /// <summary>
        /// Initialize a new game with hand-tuned starting runners.
        /// Takes an array of RunnerDefinitions for full control over starter balance — no RNG.
        /// </summary>
        public void StartNewGame(RunnerFactory.RunnerDefinition[] starterDefinitions,
            WorldMap map = null, string hubNodeId = "hub")
        {
            CurrentGameState = new GameState();
            CurrentGameState.Map = map ?? WorldMap.CreateStarterMap();

            // Populate item registry from config
            ItemRegistry = new ItemRegistry();
            foreach (var itemDef in Config.ItemDefinitions)
                ItemRegistry.Register(itemDef);

            // Initialize decision log max entries from config
            CurrentGameState.DecisionLog.SetMaxEntries(Config.DecisionLogMaxEntries);

            var rng = new Random();
            foreach (var def in starterDefinitions)
            {
                var runner = RunnerFactory.CreateFromDefinition(def, hubNodeId, Config.InventorySize, rng, Config);
                CurrentGameState.Runners.Add(runner);
                Events.Publish(new RunnerCreated
                {
                    RunnerId = runner.Id,
                    RunnerName = runner.Name,
                });
            }
        }

        /// <summary>
        /// Add a runner to the simulation at runtime. Publishes RunnerCreated.
        /// </summary>
        public void AddRunner(Runner runner)
        {
            CurrentGameState.Runners.Add(runner);
            Events.Publish(new RunnerCreated
            {
                RunnerId = runner.Id,
                RunnerName = runner.Name,
            });
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
                // Pawn 1: tanky melee fighter (passions: Defence, Melee)
                new RunnerFactory.RunnerDefinition()
                    .WithSkill(SkillType.Melee, 5, true)
                    .WithSkill(SkillType.Defence, 4, true)
                    .WithSkill(SkillType.Hitpoints, 5)
                    .WithSkill(SkillType.Athletics, 3),

                // Pawn 2: agile mage (passions: Magic, Athletics)
                new RunnerFactory.RunnerDefinition()
                    .WithSkill(SkillType.Magic, 4, true)
                    .WithSkill(SkillType.Athletics, 4, true)
                    .WithSkill(SkillType.Hitpoints, 3)
                    .WithSkill(SkillType.Ranged, 2),

                // Pawn 3: healer/precision (passions: Restoration, Execution)
                new RunnerFactory.RunnerDefinition()
                    .WithSkill(SkillType.Restoration, 3, true)
                    .WithSkill(SkillType.Execution, 3, true)
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
            CurrentGameState.TickCount++;
            CurrentGameState.TotalTimeElapsed += TickDeltaTime;

            for (int i = 0; i < CurrentGameState.Runners.Count; i++)
            {
                TickRunner(CurrentGameState.Runners[i]);
            }

            Events.Publish(new SimulationTickCompleted { TickNumber = CurrentGameState.TickCount });
        }

        private void TickRunner(Runner runner)
        {
            switch (runner.State)
            {
                case RunnerState.Idle:
                    // Idle runner with an assignment → advance macro step
                    AdvanceMacroStep(runner);
                    break;

                case RunnerState.Traveling:
                    TickTravel(runner);
                    break;

                case RunnerState.Gathering:
                    TickGathering(runner);
                    break;

                case RunnerState.Depositing:
                    TickDepositing(runner);
                    break;
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

            // Award athletics XP every tick while traveling
            var athletics = runner.GetSkill(SkillType.Athletics);
            bool leveledUp = athletics.AddXp(Config.AthleticsXpPerTick, Config);

            if (leveledUp)
            {
                Events.Publish(new RunnerSkillLeveledUp
                {
                    RunnerId = runner.Id,
                    Skill = SkillType.Athletics,
                    NewLevel = athletics.Level,
                });
            }

            float speed = GetTravelSpeed(runner);
            runner.Travel.DistanceCovered += speed * TickDeltaTime;

            if (runner.Travel.DistanceCovered >= runner.Travel.TotalDistance)
            {
                // Arrived — set idle, macro step will handle what's next
                runner.CurrentNodeId = runner.Travel.ToNodeId;
                runner.State = RunnerState.Idle;

                string arrivedNodeId = runner.Travel.ToNodeId;
                runner.Travel = null;

                Events.Publish(new RunnerArrivedAtNode
                {
                    RunnerId = runner.Id,
                    NodeId = arrivedNodeId,
                });

                // Immediately try to advance macro step now that we're idle
                AdvanceMacroStep(runner);
            }
        }

        /// <summary>
        /// Command a runner to travel to a world node. Uses the world map to find
        /// the shortest path and calculate total distance. If the runner is already
        /// at the target node, does nothing.
        /// </summary>
        public bool CommandTravel(string runnerId, string targetNodeId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null || runner.State == RunnerState.Dead) return false;

            // ─── Redirect: runner is already traveling, change destination mid-travel ───
            if (runner.State == RunnerState.Traveling && runner.Travel != null)
            {
                if (targetNodeId == runner.Travel.ToNodeId) return false;

                var map = CurrentGameState.Map;
                var fromNode = map.GetNode(runner.Travel.FromNodeId);
                var toNode = map.GetNode(runner.Travel.ToNodeId);
                var newTarget = map.GetNode(targetNodeId);
                if (fromNode == null || toNode == null || newTarget == null) return false;

                float progress = runner.Travel.Progress;
                float startX = runner.Travel.StartWorldX ?? fromNode.WorldX;
                float startZ = runner.Travel.StartWorldZ ?? fromNode.WorldZ;
                float virtualX = startX + (toNode.WorldX - startX) * progress;
                float virtualZ = startZ + (toNode.WorldZ - startZ) * progress;

                float dx = newTarget.WorldX - virtualX;
                float dz = newTarget.WorldZ - virtualZ;
                float distToNewTarget = (float)Math.Sqrt(dx * dx + dz * dz) * map.TravelDistanceScale;

                runner.Travel = new TravelState
                {
                    FromNodeId = runner.Travel.FromNodeId,
                    ToNodeId = targetNodeId,
                    TotalDistance = distToNewTarget,
                    DistanceCovered = 0f,
                    StartWorldX = virtualX,
                    StartWorldZ = virtualZ,
                };

                float speed = GetTravelSpeed(runner);
                Events.Publish(new RunnerStartedTravel
                {
                    RunnerId = runner.Id,
                    FromNodeId = runner.Travel.FromNodeId,
                    ToNodeId = targetNodeId,
                    EstimatedDurationSeconds = distToNewTarget / speed,
                });

                return true;
            }

            // ─── Normal travel ───
            if (runner.CurrentNodeId == targetNodeId) return false;

            float distance = CurrentGameState.Map.FindPath(runner.CurrentNodeId, targetNodeId, out var path);
            if (distance < 0) return false;

            string fromNodeId = runner.CurrentNodeId;
            runner.State = RunnerState.Traveling;
            runner.Travel = new TravelState
            {
                FromNodeId = fromNodeId,
                ToNodeId = targetNodeId,
                TotalDistance = distance,
                DistanceCovered = 0f,
            };

            float travelSpeed = GetTravelSpeed(runner);
            float estimatedDuration = distance / travelSpeed;

            Events.Publish(new RunnerStartedTravel
            {
                RunnerId = runner.Id,
                FromNodeId = fromNodeId,
                ToNodeId = targetNodeId,
                EstimatedDurationSeconds = estimatedDuration,
            });

            return true;
        }

        /// <summary>
        /// Command a runner to travel with an explicit distance (for testing).
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
            Events.Publish(new RunnerStartedTravel
            {
                RunnerId = runner.Id,
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                EstimatedDurationSeconds = distance / speed,
            });
        }

        // ─── Gathering ─────────────────────────────────────────────

        /// <summary>
        /// Command a runner to start gathering at their current node.
        /// Runner must be Idle and standing at a node with gatherables.
        /// gatherableIndex selects which gatherable to work on (default 0 = first).
        /// </summary>
        public bool CommandGather(string runnerId, int gatherableIndex = 0)
        {
            var runner = FindRunner(runnerId);
            if (runner == null || runner.State != RunnerState.Idle) return false;

            var node = CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            if (node == null || node.Gatherables.Length == 0) return false;
            if (gatherableIndex < 0 || gatherableIndex >= node.Gatherables.Length) return false;

            var gatherableConfig = node.Gatherables[gatherableIndex];

            if (gatherableConfig.MinLevel > 0)
            {
                var skill = runner.GetSkill(gatherableConfig.RequiredSkill);
                if (skill.Level < gatherableConfig.MinLevel)
                {
                    Events.Publish(new GatheringFailed
                    {
                        RunnerId = runner.Id,
                        NodeId = runner.CurrentNodeId,
                        ItemId = gatherableConfig.ProducedItemId,
                        Skill = gatherableConfig.RequiredSkill,
                        RequiredLevel = gatherableConfig.MinLevel,
                        CurrentLevel = skill.Level,
                    });
                    return false;
                }
            }

            // If no assignment exists, create a default gather assignment for backward compat
            // (debug UI "Gather" button or direct commands). This ensures the auto-return
            // loop works even without an explicit AssignRunner call.
            if (runner.Assignment == null)
            {
                string hubNodeId = CurrentGameState.Map?.HubNodeId;
                if (hubNodeId != null)
                {
                    runner.Assignment = Assignment.CreateGatherLoop(runner.CurrentNodeId, hubNodeId, gatherableIndex);
                    // Set to step 1 (Gather) — we're about to start gathering.
                    // When inventory fills, TickGathering will advance past it.
                    runner.Assignment.CurrentStepIndex = 1;
                }
            }

            StartGathering(runner, gatherableIndex, gatherableConfig);
            return true;
        }

        private void StartGathering(Runner runner, int gatherableIndex, GatherableConfig gatherableConfig)
        {
            float ticksRequired = CalculateTicksRequired(runner, gatherableConfig);

            runner.State = RunnerState.Gathering;
            runner.Gathering = new GatheringState
            {
                NodeId = runner.CurrentNodeId,
                GatherableIndex = gatherableIndex,
                TickAccumulator = 0f,
                TicksRequired = ticksRequired,
            };

            Events.Publish(new GatheringStarted
            {
                RunnerId = runner.Id,
                NodeId = runner.CurrentNodeId,
                ItemId = gatherableConfig.ProducedItemId,
                Skill = gatherableConfig.RequiredSkill,
            });
        }

        private float CalculateTicksRequired(Runner runner, GatherableConfig gatherable)
        {
            float effectiveLevel = runner.GetEffectiveLevel(gatherable.RequiredSkill, Config);
            float baseTicks = Config.GlobalGatheringSpeedMultiplier * gatherable.BaseTicksToGather;

            float speedMultiplier = Config.GatheringFormula switch
            {
                GatheringSpeedFormula.PowerCurve =>
                    (float)Math.Pow(effectiveLevel, Config.GatheringSpeedExponent),

                GatheringSpeedFormula.Hyperbolic =>
                    1f + (effectiveLevel - 1f) * Config.HyperbolicSpeedPerLevel,

                _ => 1f,
            };

            return baseTicks / Math.Max(speedMultiplier, 0.01f);
        }

        private void TickGathering(Runner runner)
        {
            if (runner.Gathering == null) return;

            var node = CurrentGameState.Map.GetNode(runner.Gathering.NodeId);
            if (node == null || runner.Gathering.GatherableIndex >= node.Gatherables.Length) return;

            var gatherableConfig = node.Gatherables[runner.Gathering.GatherableIndex];

            // Award XP every tick while gathering
            var skill = runner.GetSkill(gatherableConfig.RequiredSkill);
            bool leveledUp = skill.AddXp(gatherableConfig.XpPerTick, Config);

            if (leveledUp)
            {
                Events.Publish(new RunnerSkillLeveledUp
                {
                    RunnerId = runner.Id,
                    Skill = gatherableConfig.RequiredSkill,
                    NewLevel = skill.Level,
                });
            }

            // Always compute from current stats
            float ticksRequired = CalculateTicksRequired(runner, gatherableConfig);
            runner.Gathering.TicksRequired = ticksRequired;
            runner.Gathering.TickAccumulator += 1f;

            if (runner.Gathering.TickAccumulator >= ticksRequired)
            {
                runner.Gathering.TickAccumulator -= ticksRequired;

                var itemDef = ItemRegistry.Get(gatherableConfig.ProducedItemId);
                bool added = runner.Inventory.TryAdd(itemDef, 1);

                if (added)
                {
                    Events.Publish(new ItemGathered
                    {
                        RunnerId = runner.Id,
                        ItemId = gatherableConfig.ProducedItemId,
                        InventoryFreeSlots = runner.Inventory.FreeSlots,
                    });
                }

                // Inventory full — stop gathering, go idle. Advance past Gather step.
                if (runner.Inventory.IsFull(itemDef))
                {
                    Events.Publish(new InventoryFull { RunnerId = runner.Id });

                    runner.State = RunnerState.Idle;
                    runner.Gathering = null;

                    // Advance past the Gather step so AdvanceMacroStep picks up the next one
                    if (runner.Assignment != null)
                    {
                        runner.Assignment.AdvanceStep();
                        PublishStepAdvanced(runner, runner.Assignment);
                    }

                    AdvanceMacroStep(runner);
                }
            }
        }

        // ─── Macro Layer: Assignment + Step Logic ───────────────────

        /// <summary>
        /// Assign a runner to a new task sequence. Cancels current activity,
        /// publishes AssignmentChanged, and starts executing the first step.
        /// </summary>
        public void AssignRunner(string runnerId, Assignment assignment, string reason = "")
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            // Cancel current activity
            runner.Gathering = null;
            runner.Travel = null;
            runner.Depositing = null;
            runner.State = RunnerState.Idle;

            runner.Assignment = assignment;

            Events.Publish(new AssignmentChanged
            {
                RunnerId = runner.Id,
                NewType = assignment?.Type ?? AssignmentType.Idle,
                TargetNodeId = assignment?.TargetNodeId,
                Reason = reason,
            });

            // Start executing the first step
            AdvanceMacroStep(runner);
        }

        /// <summary>
        /// Execute the current step of the runner's assignment.
        /// Called when the runner becomes Idle (after arriving, after depositing, etc.).
        /// </summary>
        private void AdvanceMacroStep(Runner runner)
        {
            if (runner.State != RunnerState.Idle) return;

            var assignment = runner.Assignment;
            if (assignment == null) return;

            var step = assignment.CurrentStep;
            if (step == null) return;

            switch (step.Type)
            {
                case TaskStepType.TravelTo:
                    ExecuteTravelStep(runner, step, assignment);
                    break;

                case TaskStepType.Gather:
                    ExecuteGatherStep(runner, assignment);
                    break;

                case TaskStepType.Deposit:
                    ExecuteDepositStep(runner, assignment);
                    break;
            }
        }

        private void ExecuteTravelStep(Runner runner, TaskStep step, Assignment assignment)
        {
            // Already at target? Step is done — advance past it and handle next.
            if (runner.CurrentNodeId == step.TargetNodeId)
            {
                if (assignment.AdvanceStep())
                {
                    PublishStepAdvanced(runner, assignment);
                    AdvanceMacroStep(runner); // recurse for next step
                }
                return;
            }

            // Start traveling. Step index stays on TravelTo so the display
            // correctly shows what the runner is doing. When travel completes
            // (Idle → AdvanceMacroStep), it will see "already at target" and advance.
            StartTravelInternal(runner, step.TargetNodeId);
        }

        private void ExecuteGatherStep(Runner runner, Assignment assignment)
        {
            var node = CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            if (node == null || node.Gatherables.Length == 0)
            {
                // Can't gather here — skip step
                if (assignment.AdvanceStep())
                {
                    PublishStepAdvanced(runner, assignment);
                    AdvanceMacroStep(runner);
                }
                return;
            }

            // Use the assignment's default gatherable index
            // (Batch B will add micro rule evaluation here)
            int gatherableIndex = assignment.GatherableIndex;
            if (gatherableIndex < 0 || gatherableIndex >= node.Gatherables.Length)
                gatherableIndex = 0;

            var gatherableConfig = node.Gatherables[gatherableIndex];

            // Check skill level requirement
            if (gatherableConfig.MinLevel > 0)
            {
                var skill = runner.GetSkill(gatherableConfig.RequiredSkill);
                if (skill.Level < gatherableConfig.MinLevel)
                {
                    // Can't gather — skip
                    if (assignment.AdvanceStep())
                    {
                        PublishStepAdvanced(runner, assignment);
                        AdvanceMacroStep(runner);
                    }
                    return;
                }
            }

            StartGathering(runner, gatherableIndex, gatherableConfig);

            // Step index stays on Gather so the display correctly shows what the
            // runner is doing. When gathering completes (inventory full), TickGathering
            // advances past this step, then calls AdvanceMacroStep for the next one.
        }

        private void ExecuteDepositStep(Runner runner, Assignment assignment)
        {
            // Start the deposit timer — actual deposit happens when it completes
            runner.State = RunnerState.Depositing;
            runner.Depositing = new DepositingState
            {
                TicksRemaining = Config.DepositDurationTicks,
            };
        }

        private void TickDepositing(Runner runner)
        {
            if (runner.Depositing == null) return;

            runner.Depositing.TicksRemaining--;
            if (runner.Depositing.TicksRemaining > 0) return;

            // Timer done — execute the actual deposit
            int itemCount = runner.Inventory.Slots.Count;
            CurrentGameState.Bank.DepositAll(runner.Inventory);

            if (itemCount > 0)
            {
                Events.Publish(new RunnerDeposited
                {
                    RunnerId = runner.Id,
                    ItemsDeposited = itemCount,
                });
            }

            runner.State = RunnerState.Idle;
            runner.Depositing = null;

            // Advance past the Deposit step
            if (runner.Assignment != null && runner.Assignment.AdvanceStep())
            {
                PublishStepAdvanced(runner, runner.Assignment);
            }

            AdvanceMacroStep(runner);
        }

        private void PublishStepAdvanced(Runner runner, Assignment assignment)
        {
            var step = assignment.CurrentStep;
            if (step == null) return;

            Events.Publish(new AssignmentStepAdvanced
            {
                RunnerId = runner.Id,
                StepType = step.Type,
                StepIndex = assignment.CurrentStepIndex,
            });
        }

        // ─── Internal Helpers ──────────────────────────────────────

        private void StartTravelInternal(Runner runner, string targetNodeId)
        {
            float distance = CurrentGameState.Map.FindPath(runner.CurrentNodeId, targetNodeId, out _);
            if (distance < 0) return;

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

        public bool CancelGathering(string runnerId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return false;
            if (runner.Gathering == null) return false;

            runner.Gathering = null;
            runner.State = RunnerState.Idle;
            runner.Travel = null;
            return true;
        }

        public Runner FindRunner(string runnerId)
        {
            for (int i = 0; i < CurrentGameState.Runners.Count; i++)
            {
                if (CurrentGameState.Runners[i].Id == runnerId)
                    return CurrentGameState.Runners[i];
            }
            return null;
        }
    }
}
