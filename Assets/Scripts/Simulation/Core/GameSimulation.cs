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
    /// The macro layer (TaskSequence + steps) drives the work→deposit→return loop.
    /// Micro rules evaluate within the Work step (e.g. which resource to gather, what to fight).
    /// Macro rules change task sequences based on conditions (e.g. switch nodes when bank threshold hit).
    /// </summary>
    public class GameSimulation
    {
        public GameState CurrentGameState { get; private set; }
        public EventBus Events { get; private set; }
        public SimulationConfig Config { get; private set; }
        public ItemRegistry ItemRegistry { get; private set; }
        public EventLogService EventLog { get; private set; }

        private readonly System.Random _random = new();


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
            EventLog = new EventLogService(Config.EventLogMaxEntries, () => CurrentGameState);
            EventLog.SubscribeAll(Events);
        }

        /// <summary>
        /// Load an existing game state (for save/load).
        /// </summary>
        public void LoadState(GameState state)
        {
            CurrentGameState = state;
            CurrentGameState.Map?.Initialize();
            MigrateRunnerRulesets();
        }

        /// <summary>
        /// Migrate runners from old save format (direct object fields) to new format (ID references).
        /// Also ensures defaults exist for runners with no rulesets.
        /// </summary>
        private void MigrateRunnerRulesets()
        {
            DefaultRulesets.EnsureInLibrary(CurrentGameState);

            foreach (var runner in CurrentGameState.Runners)
            {
                // Migrate legacy MacroRuleset → library + MacroRulesetId
                if (runner.MacroRuleset != null && string.IsNullOrEmpty(runner.MacroRulesetId))
                {
                    if (string.IsNullOrEmpty(runner.MacroRuleset.Id))
                        runner.MacroRuleset.Id = Guid.NewGuid().ToString();
                    if (string.IsNullOrEmpty(runner.MacroRuleset.Name))
                        runner.MacroRuleset.Name = "Migrated Macro";
                    if (FindMacroRulesetInLibrary(runner.MacroRuleset.Id) == null)
                        CurrentGameState.MacroRulesetLibrary.Add(runner.MacroRuleset);
                    runner.MacroRulesetId = runner.MacroRuleset.Id;
                }

                // Migrate legacy MicroRuleset → library + Work step MicroRulesetId
                if (runner.MicroRuleset != null)
                {
                    if (string.IsNullOrEmpty(runner.MicroRuleset.Id))
                        runner.MicroRuleset.Id = Guid.NewGuid().ToString();
                    if (string.IsNullOrEmpty(runner.MicroRuleset.Name))
                        runner.MicroRuleset.Name = "Migrated Micro";
                    runner.MicroRuleset.Category = RulesetCategory.Gathering;
                    if (FindMicroRulesetInLibrary(runner.MicroRuleset.Id) == null)
                        CurrentGameState.MicroRulesetLibrary.Add(runner.MicroRuleset);
                    // Set MicroRulesetId on all Work steps of the runner's task sequence
                    var seq = runner.TaskSequence ?? FindTaskSequenceInLibrary(runner.TaskSequenceId);
                    if (seq?.Steps != null)
                    {
                        foreach (var step in seq.Steps)
                        {
                            if (step.Type == TaskStepType.Work && string.IsNullOrEmpty(step.MicroRulesetId))
                                step.MicroRulesetId = runner.MicroRuleset.Id;
                        }
                    }
                    runner.MicroRuleset = null; // clear legacy field after migration
                }

                // Migrate legacy TaskSequence → library + TaskSequenceId
                if (runner.TaskSequence != null && string.IsNullOrEmpty(runner.TaskSequenceId))
                {
                    if (string.IsNullOrEmpty(runner.TaskSequence.Id))
                        runner.TaskSequence.Id = Guid.NewGuid().ToString();
                    if (FindTaskSequenceInLibrary(runner.TaskSequence.Id) == null)
                        CurrentGameState.TaskSequenceLibrary.Add(runner.TaskSequence);
                    runner.TaskSequenceId = runner.TaskSequence.Id;
                }

                // Migrate old saves that assigned the removed "default-macro" — clear to null
                if (runner.MacroRulesetId == "default-macro")
                    runner.MacroRulesetId = null;
            }

            // Remove the obsolete "default-macro" entry from the library if present
            CurrentGameState.MacroRulesetLibrary.RemoveAll(r => r.Id == "default-macro");
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

            // Ensure default rulesets exist in the library before creating runners
            DefaultRulesets.EnsureInLibrary(CurrentGameState);

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
            // Evaluate macro rules every tick for all active (non-Idle) runners.
            // Idle runners evaluate via AdvanceMacroStep which also handles step execution.
            // An Immediate rule that fires here will call AssignRunner, changing state — skip the rest of the tick.
            if (runner.State != RunnerState.Idle)
            {
                if (EvaluateMacroRules(runner, "Tick"))
                    return;
            }

            switch (runner.State)
            {
                case RunnerState.Idle:
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

                // Evaluate macro rules on arrival
                if (EvaluateMacroRules(runner, "ArrivedAtNode")) return;

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

            bool itemJustProduced = false;
            if (runner.Gathering.TickAccumulator >= ticksRequired)
            {
                runner.Gathering.TickAccumulator -= ticksRequired;
                itemJustProduced = true;

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

                // Publish InventoryFull event for visibility (Event Log, macro conditions)
                if (runner.Inventory.IsFull(itemDef))
                {
                    Events.Publish(new InventoryFull { RunnerId = runner.Id });
                }
            }

            // Re-evaluate micro rules every tick — may switch resources, FinishTask,
            // or handle InventoryFull (the default micro ruleset has IF InventoryFull → FinishTask).
            // Runs every tick, not just after item production, so condition changes are caught immediately.
            // itemJustProduced tells GatherAny whether to re-roll (true) or keep current (false).
            ReevaluateMicroDuringGathering(runner, node, itemJustProduced);
        }

        // ─── Macro Layer: Task Sequence + Step Logic ───────────────────

        /// <summary>
        /// Send a runner to gather at a node with one guaranteed cycle
        /// (macro rules suspended until the sequence loops).
        /// This is the single entry point for the "Work At" player action.
        /// </summary>
        public void CommandWorkAtSuspendMacrosForOneCycle(string runnerId, string nodeId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            var hubId = CurrentGameState.Map.HubNodeId;
            var taskSeq = TaskSequence.CreateLoop(nodeId, hubId);
            runner.MacroSuspendedUntilLoop = true;
            AssignRunner(runnerId, taskSeq, "Work At");
        }

        /// <summary>
        /// Clear a runner's task sequence and resume macro rule evaluation.
        /// This is the single entry point for the "Clear Task" player action.
        /// </summary>
        public void ClearTaskSequence(string runnerId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            runner.MacroSuspendedUntilLoop = false;
            AssignRunner(runnerId, null, "manual clear");
        }

        /// <summary>
        /// Resume macro rule evaluation for a runner whose rules were
        /// suspended (e.g., after "Work At" one-cycle guarantee).
        /// </summary>
        public void ResumeMacroRules(string runnerId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            runner.MacroSuspendedUntilLoop = false;
        }

        /// <summary>
        /// Assign a runner to a new task sequence. Cancels current activity,
        /// publishes TaskSequenceChanged, and starts executing the first step.
        /// </summary>
        public void AssignRunner(string runnerId, TaskSequence taskSequence, string reason = "")
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            // Cancel current activity — if mid-travel, capture virtual position for redirect
            float? redirectWorldX = null;
            float? redirectWorldZ = null;
            if (runner.State == RunnerState.Traveling && runner.Travel != null)
            {
                var map = CurrentGameState.Map;
                var fromNode = map.GetNode(runner.Travel.FromNodeId);
                var toNode = map.GetNode(runner.Travel.ToNodeId);
                if (fromNode != null && toNode != null)
                {
                    float progress = runner.Travel.Progress;
                    float startX = runner.Travel.StartWorldX ?? fromNode.WorldX;
                    float startZ = runner.Travel.StartWorldZ ?? fromNode.WorldZ;
                    redirectWorldX = startX + (toNode.WorldX - startX) * progress;
                    redirectWorldZ = startZ + (toNode.WorldZ - startZ) * progress;
                }
            }

            runner.Gathering = null;
            runner.Travel = null;
            runner.Depositing = null;
            runner.State = RunnerState.Idle;
            runner.RedirectWorldX = redirectWorldX;
            runner.RedirectWorldZ = redirectWorldZ;

            // Register task sequence in library if it has an Id and isn't already there
            if (taskSequence != null)
            {
                if (string.IsNullOrEmpty(taskSequence.Id))
                    taskSequence.Id = Guid.NewGuid().ToString();
                if (FindTaskSequenceInLibrary(taskSequence.Id) == null)
                    CurrentGameState.TaskSequenceLibrary.Add(taskSequence);
            }

            runner.TaskSequenceId = taskSequence?.Id;
            runner.TaskSequence = taskSequence; // keep legacy field in sync for now
            runner.TaskSequenceCurrentStepIndex = 0;
            runner.LastCompletedSequenceTargetNodeId = null;
            // Don't clear MacroSuspendedUntilLoop here — WorkAt() sets it before
            // calling AssignRunner, and we want it to persist through the first cycle.

            Events.Publish(new TaskSequenceChanged
            {
                RunnerId = runner.Id,
                TargetNodeId = taskSequence?.TargetNodeId,
                Reason = reason,
            });

            // Start executing the first step
            AdvanceMacroStep(runner);
        }

        /// <summary>
        /// Execute the current step of the runner's task sequence.
        /// Called when the runner becomes Idle (after arriving, after depositing, etc.).
        /// </summary>
        private void AdvanceMacroStep(Runner runner)
        {
            if (runner.State != RunnerState.Idle) return;

            // Evaluate macro rules before executing the next step.
            // If a rule fires and changes the task sequence, AssignRunner will call
            // AdvanceMacroStep again. Same-sequence suppression prevents infinite loops.
            if (EvaluateMacroRules(runner, "StepAdvance")) return;

            var seq = GetRunnerTaskSequence(runner);
            if (seq == null) return;

            var step = GetCurrentStep(runner, seq);
            if (step == null) return;

            switch (step.Type)
            {
                case TaskStepType.TravelTo:
                    ExecuteTravelStep(runner, step, seq);
                    break;

                case TaskStepType.Work:
                    ExecuteWorkStep(runner, seq);
                    break;

                case TaskStepType.Deposit:
                    ExecuteDepositStep(runner, seq);
                    break;
            }
        }

        private void ExecuteTravelStep(Runner runner, TaskStep step, TaskSequence seq)
        {
            // Already at target? Step is done — advance past it and handle next.
            // But if there's a redirect position, the runner was interrupted mid-travel
            // and isn't actually at CurrentNodeId — they need to travel.
            if (runner.CurrentNodeId == step.TargetNodeId && !runner.RedirectWorldX.HasValue)
            {
                if (AdvanceRunnerStepIndex(runner, seq))
                {
                    PublishStepAdvanced(runner, seq);
                    AdvanceMacroStep(runner); // recurse for next step
                }
                else
                {
                    HandleSequenceCompleted(runner);
                }
                return;
            }

            // Start traveling. Step index stays on TravelTo so the display
            // correctly shows what the runner is doing. When travel completes
            // (Idle → AdvanceMacroStep), it will see "already at target" and advance.
            StartTravelInternal(runner, step.TargetNodeId);
        }

        private void ExecuteWorkStep(Runner runner, TaskSequence seq)
        {
            var node = CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            if (node == null || node.Gatherables.Length == 0)
            {
                // No gatherables at this node — runner stays stuck. "Let it break."
                Events.Publish(new GatheringFailed
                {
                    RunnerId = runner.Id,
                    NodeId = runner.CurrentNodeId,
                    Reason = GatheringFailureReason.NoGatherablesAtNode,
                });
                return;
            }

            // Evaluate micro rules to decide which resource to gather
            int gatherableIndex = EvaluateMicroRules(runner, node);

            // FinishTask signal — micro says "done gathering, advance macro"
            if (gatherableIndex == MicroResultFinishTask)
            {
                if (AdvanceRunnerStepIndex(runner, seq))
                {
                    PublishStepAdvanced(runner, seq);
                    AdvanceMacroStep(runner);
                }
                else
                {
                    HandleSequenceCompleted(runner);
                }
                return;
            }

            // No valid rule matched — runner is stuck. Stay idle at the Work step.
            if (gatherableIndex == MicroResultNoMatch)
            {
                PublishNoMicroRuleMatched(runner);
                return;
            }

            var gatherableConfig = node.Gatherables[gatherableIndex];

            // Check skill level requirement
            if (gatherableConfig.MinLevel > 0)
            {
                var skill = runner.GetSkill(gatherableConfig.RequiredSkill);
                if (skill.Level < gatherableConfig.MinLevel)
                {
                    // Can't gather — runner stays stuck at Work step. "Let it break."
                    Events.Publish(new GatheringFailed
                    {
                        RunnerId = runner.Id,
                        NodeId = runner.CurrentNodeId,
                        ItemId = gatherableConfig.ProducedItemId,
                        Skill = gatherableConfig.RequiredSkill,
                        RequiredLevel = gatherableConfig.MinLevel,
                        CurrentLevel = skill.Level,
                        Reason = GatheringFailureReason.NotEnoughSkill,
                    });
                    return;
                }
            }

            StartGathering(runner, gatherableIndex, gatherableConfig);

            // Step index stays on Work so the display correctly shows what the
            // runner is doing. When gathering completes (inventory full), TickGathering
            // advances past this step, then calls AdvanceMacroStep for the next one.
        }

        private void ExecuteDepositStep(Runner runner, TaskSequence seq)
        {
            // Start the deposit timer — actual deposit happens when it completes
            runner.State = RunnerState.Depositing;
            runner.Depositing = new DepositingState
            {
                TicksRemaining = Config.DepositDurationTicks,
            };
        }

        // ─── Micro Layer: Within-Task Behavior ────────────────────

        private const int MicroResultFinishTask = -1;
        private const int MicroResultNoMatch = -2;

        /// <summary>
        /// Evaluate the micro ruleset for the current Work step to decide which resource to gather.
        /// Reads the micro ruleset from the Work step's MicroRulesetId, falling back to
        /// runner.MicroRuleset for backward compatibility until Step 5 migration.
        /// Returns:
        ///   >= 0: gatherable index to gather
        ///   -1 (MicroResultFinishTask): FinishTask — advance macro step
        ///   -2 (MicroResultNoMatch): no valid rule matched — runner is stuck, stay idle
        ///
        /// Null or empty micro ruleset = no valid rule matched = runner is stuck (let it break).
        /// </summary>
        private int EvaluateMicroRules(Runner runner, World.WorldNode node,
            bool itemJustProduced = false)
        {
            // Resolve micro ruleset: prefer Work step's MicroRulesetId → library lookup,
            // fall back to runner.MicroRuleset for old save compatibility.
            Ruleset microRuleset = null;
            var seq = GetRunnerTaskSequence(runner);
            if (seq != null)
            {
                var step = GetCurrentStep(runner, seq);
                if (step?.MicroRulesetId != null)
                    microRuleset = FindMicroRulesetInLibrary(step.MicroRulesetId);
            }
            microRuleset ??= runner.MicroRuleset;

            if (microRuleset == null)
                return MicroResultNoMatch; // let it break

            var ctx = new EvaluationContext(runner, CurrentGameState, Config);
            int ruleIndex = RuleEvaluator.EvaluateRuleset(microRuleset, ctx);

            if (ruleIndex >= 0)
            {
                var rule = microRuleset.Rules[ruleIndex];
                var action = rule.Action;

                if (action.Type == ActionType.FinishTask)
                {
                    LogDecision(runner, ruleIndex, rule, "MicroEval",
                        "FinishTask", false, DecisionLayer.Micro);
                    return MicroResultFinishTask;
                }

                if (action.Type == ActionType.GatherHere)
                {
                    int index = ResolveGatherHereIndex(action, node, runner, itemJustProduced);
                    if (index >= 0 && index < node.Gatherables.Length)
                    {
                        string actionLabel = action.IntParam == -1
                            ? $"GatherAny→{index}" : $"GatherHere({index})";
                        LogDecision(runner, ruleIndex, rule, "MicroEval",
                            actionLabel, false, DecisionLayer.Micro);
                        return index;
                    }
                }

                // Rule matched but action is invalid (wrong type, out-of-bounds index).
                // This is a broken rule — let it break.
                return MicroResultNoMatch;
            }

            // No rule matched at all (empty ruleset or all conditions false).
            // Player's rules don't cover this case — let it break.
            return MicroResultNoMatch;
        }

        /// <summary>
        /// Resolve GatherHere action to a gatherable index at the given node.
        /// Three modes:
        ///   - StringParam set (e.g., "iron_ore") → find gatherable producing that item. -1 if not found.
        ///   - IntParam == -1 (GatherAny) → random selection, stable mid-gather.
        ///     Only re-rolls when starting a new item (itemJustProduced=true or Gathering==null).
        ///   - IntParam >= 0 → use as positional index directly.
        /// </summary>
        private int ResolveGatherHereIndex(AutomationAction action, World.WorldNode node,
            Runner runner, bool itemJustProduced)
        {
            if (!string.IsNullOrEmpty(action.StringParam))
            {
                // Item-ID resolution: find the gatherable that produces this item
                for (int i = 0; i < node.Gatherables.Length; i++)
                {
                    if (node.Gatherables[i].ProducedItemId == action.StringParam)
                        return i;
                }
                return -1; // item not available at this node — let it break
            }

            // GatherAny: random per item, stable mid-gather
            if (action.IntParam == -1)
            {
                if (node.Gatherables.Length == 0) return -1;

                // Mid-gather and no item just produced → keep current resource
                if (runner.Gathering != null && !itemJustProduced
                    && runner.Gathering.GatherableIndex >= 0
                    && runner.Gathering.GatherableIndex < node.Gatherables.Length)
                {
                    return runner.Gathering.GatherableIndex;
                }

                // Fresh pick: Work step entry (Gathering==null) or item just produced
                return _random.Next(node.Gatherables.Length);
            }

            // Positional index (default behavior)
            return action.IntParam;
        }

        /// <summary>
        /// Re-evaluate micro rules mid-gathering (after each item gathered).
        /// If the result changes resource index, restart gathering with the new resource.
        /// If FinishTask, stop gathering and advance the macro step.
        /// </summary>
        private void ReevaluateMicroDuringGathering(Runner runner, World.WorldNode node,
            bool itemJustProduced = false)
        {
            if (GetRunnerTaskSequence(runner) == null) return;

            int newIndex = EvaluateMicroRules(runner, node, itemJustProduced);

            // FinishTask — micro says "done gathering"
            if (newIndex == MicroResultFinishTask)
            {
                runner.State = RunnerState.Idle;
                runner.Gathering = null;

                var seq = GetRunnerTaskSequence(runner);
                if (seq != null && AdvanceRunnerStepIndex(runner, seq))
                {
                    PublishStepAdvanced(runner, seq);
                    AdvanceMacroStep(runner);
                }
                else
                {
                    HandleSequenceCompleted(runner);
                }
                return;
            }

            // No valid rule matched — stop gathering, runner is stuck at the Gather step
            if (newIndex == MicroResultNoMatch)
            {
                runner.State = RunnerState.Idle;
                runner.Gathering = null;
                PublishNoMicroRuleMatched(runner);
                return;
            }

            // Different resource — switch (reset accumulator for the new resource)
            if (newIndex != runner.Gathering.GatherableIndex)
            {
                var gatherableConfig = node.Gatherables[newIndex];
                runner.Gathering.GatherableIndex = newIndex;
                runner.Gathering.TickAccumulator = 0f;
                runner.Gathering.TicksRequired = CalculateTicksRequired(runner, gatherableConfig);
            }
            // Same resource — keep going, accumulator rolls over naturally
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
            var depositSeq = GetRunnerTaskSequence(runner);
            if (depositSeq != null)
            {
                if (AdvanceRunnerStepIndex(runner, depositSeq))
                {
                    PublishStepAdvanced(runner, depositSeq);
                }
                else
                {
                    // Non-looping sequence completed
                    HandleSequenceCompleted(runner);
                    return;
                }
            }

            // Sequence boundary — apply pending task sequence from earlier in the cycle
            if (ApplyPendingTaskSequence(runner)) return;

            // Evaluate macro rules at deposit completion
            if (EvaluateMacroRules(runner, "DepositCompleted")) return;

            // Apply pending set by the macro eval above (deferred rule that fired at sequence boundary)
            if (ApplyPendingTaskSequence(runner)) return;

            AdvanceMacroStep(runner);
        }

        private void PublishStepAdvanced(Runner runner, TaskSequence seq)
        {
            var step = GetCurrentStep(runner, seq);
            if (step == null) return;

            // Sequence looped — clear the "Work At" one-cycle suspension
            if (runner.TaskSequenceCurrentStepIndex == 0 && runner.MacroSuspendedUntilLoop)
                runner.MacroSuspendedUntilLoop = false;

            Events.Publish(new TaskSequenceStepAdvanced
            {
                RunnerId = runner.Id,
                StepType = step.Type,
                StepIndex = runner.TaskSequenceCurrentStepIndex,
            });
        }

        /// <summary>
        /// Handle a non-looping sequence completing (AdvanceStep returned false).
        /// Clears the runner's task sequence, publishes completion event,
        /// and lets macro rules re-evaluate.
        /// </summary>
        private void HandleSequenceCompleted(Runner runner)
        {
            var completedSeq = GetRunnerTaskSequence(runner);
            string seqName = completedSeq?.Name ?? "";
            runner.LastCompletedSequenceTargetNodeId = completedSeq?.TargetNodeId;
            runner.TaskSequenceId = null;
            runner.TaskSequence = null; // legacy sync
            runner.State = RunnerState.Idle;

            Events.Publish(new TaskSequenceCompleted
            {
                RunnerId = runner.Id,
                SequenceName = seqName,
            });

            // Macro rules get a chance to assign a new sequence.
            // If a rule fires, AssignRunner calls AdvanceMacroStep internally.
            // If no rule fires, runner stays idle — next tick picks it up.
            EvaluateMacroRules(runner, "SequenceCompleted");
        }

        // ─── Macro Layer: Rule Evaluation ────────────────────────────

        private int _macroEvalDepth;

        /// <summary>
        /// Evaluate the runner's macro ruleset. If a rule matches, its action maps
        /// to a new task sequence. FinishCurrentSequence defers via PendingTaskSequence.
        /// Returns true if the runner's task sequence was changed (immediate), meaning
        /// the caller should NOT proceed with normal step advancement.
        /// </summary>
        private bool EvaluateMacroRules(Runner runner, string triggerReason)
        {
            // Safety valve: prevent infinite recursion from instantly-completing sequences
            // (e.g., ReturnToHub when already at hub → completes → re-evaluates → same rule fires).
            if (_macroEvalDepth > 3) return false;
            _macroEvalDepth++;
            try
            {
                return EvaluateMacroRulesInternal(runner, triggerReason);
            }
            finally
            {
                _macroEvalDepth--;
            }
        }

        private bool EvaluateMacroRulesInternal(Runner runner, string triggerReason)
        {
            if (runner.MacroSuspendedUntilLoop)
                return false;

            var macroRuleset = GetRunnerMacroRuleset(runner);
            if (macroRuleset == null || macroRuleset.Rules.Count == 0)
                return false;

            var ctx = new EvaluationContext(runner, CurrentGameState, Config);
            int ruleIndex = RuleEvaluator.EvaluateRuleset(macroRuleset, ctx);

            if (ruleIndex < 0) return false;

            var rule = macroRuleset.Rules[ruleIndex];
            var newSeq = ActionToTaskSequence(rule.Action);

            var currentSeq = GetRunnerTaskSequence(runner);

            // If the new sequence is the same as current, skip (avoid infinite reassignment).
            // Both null = already idle, rule wants idle → suppress.
            if (newSeq == null && currentSeq == null)
                return false;

            if (currentSeq != null && newSeq != null
                && currentSeq.TargetNodeId == newSeq.TargetNodeId)
                return false;

            // Also suppress if the sequence just completed with the same target
            // (e.g., ReturnToHub completed → macro fires ReturnToHub again → suppress).
            if (currentSeq == null && newSeq != null
                && runner.LastCompletedSequenceTargetNodeId == newSeq.TargetNodeId)
                return false;

            // Log the decision
            LogDecision(runner, ruleIndex, rule, triggerReason,
                newSeq != null ? $"Work @ {newSeq.TargetNodeId}" : "Idle",
                rule.FinishCurrentSequence);

            // Deferred: store as pending, apply at sequence boundary.
            // But if there's no active sequence, degrade to Immediately —
            // there's nothing to "finish" so waiting makes no sense.
            if (rule.FinishCurrentSequence && currentSeq != null)
            {
                // Register pending in library too
                if (newSeq != null)
                {
                    if (string.IsNullOrEmpty(newSeq.Id))
                        newSeq.Id = Guid.NewGuid().ToString();
                    if (FindTaskSequenceInLibrary(newSeq.Id) == null)
                        CurrentGameState.TaskSequenceLibrary.Add(newSeq);
                }
                runner.PendingTaskSequenceId = newSeq?.Id;
                runner.PendingTaskSequence = newSeq; // legacy sync
                return false;
            }

            // Immediate: change task sequence now
            AssignRunner(runner.Id, newSeq, $"macro rule: {rule.Label}");
            return true;
        }

        /// <summary>
        /// Map a macro rule's action to a TaskSequence.
        /// Returns null for Idle (clear task sequence).
        /// </summary>
        private TaskSequence ActionToTaskSequence(AutomationAction action)
        {
            string hubId = CurrentGameState.Map?.HubNodeId ?? "hub";

            switch (action.Type)
            {
                case ActionType.WorkAt:
                    string nodeId = string.IsNullOrEmpty(action.StringParam) ? null : action.StringParam;
                    if (nodeId == null) return null;
                    return TaskSequence.CreateLoop(nodeId, hubId);

                case ActionType.ReturnToHub:
                    return new TaskSequence
                    {
                        Id = "return-to-hub",
                        Name = "Return to Hub",
                        TargetNodeId = hubId,
                        Loop = false,
                        Steps = new System.Collections.Generic.List<TaskStep>
                        {
                            new TaskStep(TaskStepType.TravelTo, hubId),
                        },
                    };

                case ActionType.Idle:
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Check and apply PendingTaskSequence at sequence boundaries.
        /// Called after a step advances and wraps to step 0 (start of a new loop cycle).
        /// </summary>
        private bool ApplyPendingTaskSequence(Runner runner)
        {
            var pending = GetRunnerPendingTaskSequence(runner);
            if (pending == null) return false;

            runner.PendingTaskSequenceId = null;
            runner.PendingTaskSequence = null; // legacy sync

            AssignRunner(runner.Id, pending, "deferred macro rule");
            return true;
        }

        private void PublishNoMicroRuleMatched(Runner runner)
        {
            // Resolve micro ruleset from Work step, falling back to runner for compat
            Ruleset ruleset = null;
            var noMicroSeq = GetRunnerTaskSequence(runner);
            if (noMicroSeq != null)
            {
                var step = GetCurrentStep(runner, noMicroSeq);
                if (step?.MicroRulesetId != null)
                    ruleset = FindMicroRulesetInLibrary(step.MicroRulesetId);
            }
            ruleset ??= runner.MicroRuleset;

            Events.Publish(new NoMicroRuleMatched
            {
                RunnerId = runner.Id,
                RunnerName = runner.Name,
                NodeId = runner.CurrentNodeId,
                RulesetIsEmpty = ruleset == null || ruleset.Rules == null || ruleset.Rules.Count == 0,
                RuleCount = ruleset?.Rules?.Count ?? 0,
            });
        }

        // ─── Library Lookup Helpers ──────────────────────────────────

        /// <summary>Find a task sequence in the global library by Id.</summary>
        public TaskSequence FindTaskSequenceInLibrary(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var lib = CurrentGameState.TaskSequenceLibrary;
            for (int i = 0; i < lib.Count; i++)
                if (lib[i].Id == id) return lib[i];
            return null;
        }

        /// <summary>Find a macro ruleset in the global library by Id.</summary>
        public Ruleset FindMacroRulesetInLibrary(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var lib = CurrentGameState.MacroRulesetLibrary;
            for (int i = 0; i < lib.Count; i++)
                if (lib[i].Id == id) return lib[i];
            return null;
        }

        /// <summary>Find a micro ruleset in the global library by Id.</summary>
        public Ruleset FindMicroRulesetInLibrary(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var lib = CurrentGameState.MicroRulesetLibrary;
            for (int i = 0; i < lib.Count; i++)
                if (lib[i].Id == id) return lib[i];
            return null;
        }

        // ─── Runner-Level Resolvers ─────────────────────────────────

        /// <summary>Get the runner's active task sequence from the library.</summary>
        public TaskSequence GetRunnerTaskSequence(Runner runner) =>
            FindTaskSequenceInLibrary(runner.TaskSequenceId) ?? runner.TaskSequence;

        /// <summary>Get the runner's pending task sequence from the library.</summary>
        public TaskSequence GetRunnerPendingTaskSequence(Runner runner) =>
            FindTaskSequenceInLibrary(runner.PendingTaskSequenceId) ?? runner.PendingTaskSequence;

        /// <summary>Get the runner's macro ruleset from the library.</summary>
        public Ruleset GetRunnerMacroRuleset(Runner runner) =>
            FindMacroRulesetInLibrary(runner.MacroRulesetId) ?? runner.MacroRuleset;

        // ─── Runner Commands ──────────────────────────────────────────

        public void CommandRenameRunner(string runnerId, string newName)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;
            runner.Name = newName?.Trim() ?? "";
        }

        // ─── Library CRUD Commands ──────────────────────────────────

        /// <summary>Register a task sequence in the library. Returns its Id.</summary>
        public string CommandCreateTaskSequence(TaskSequence template)
        {
            if (string.IsNullOrEmpty(template.Id))
                template.Id = Guid.NewGuid().ToString();
            CurrentGameState.TaskSequenceLibrary.Add(template);
            return template.Id;
        }

        /// <summary>Register a macro ruleset in the library. Returns its Id.</summary>
        public string CommandCreateMacroRuleset(Ruleset template)
        {
            if (string.IsNullOrEmpty(template.Id))
                template.Id = Guid.NewGuid().ToString();
            CurrentGameState.MacroRulesetLibrary.Add(template);
            return template.Id;
        }

        /// <summary>Register a micro ruleset in the library. Returns its Id.</summary>
        public string CommandCreateMicroRuleset(Ruleset template)
        {
            if (string.IsNullOrEmpty(template.Id))
                template.Id = Guid.NewGuid().ToString();
            CurrentGameState.MicroRulesetLibrary.Add(template);
            return template.Id;
        }

        /// <summary>Remove a task sequence from the library. Clears runner refs that point to it.</summary>
        public void CommandDeleteTaskSequence(string id)
        {
            CurrentGameState.TaskSequenceLibrary.RemoveAll(ts => ts.Id == id);
            foreach (var runner in CurrentGameState.Runners)
            {
                if (runner.TaskSequenceId == id)
                {
                    runner.TaskSequenceId = null;
                    runner.TaskSequence = null; // legacy sync
                    runner.TaskSequenceCurrentStepIndex = 0;
                    runner.State = RunnerState.Idle;
                }
                if (runner.PendingTaskSequenceId == id)
                {
                    runner.PendingTaskSequenceId = null;
                    runner.PendingTaskSequence = null; // legacy sync
                }
            }
        }

        /// <summary>Remove a macro ruleset from the library. Sets runner refs to null.</summary>
        public void CommandDeleteMacroRuleset(string id)
        {
            CurrentGameState.MacroRulesetLibrary.RemoveAll(r => r.Id == id);
            foreach (var runner in CurrentGameState.Runners)
            {
                if (runner.MacroRulesetId == id)
                {
                    runner.MacroRulesetId = null;
                    runner.MacroRuleset = null; // legacy sync
                }
            }
        }

        /// <summary>Remove a micro ruleset from the library. Clears Work step refs.</summary>
        public void CommandDeleteMicroRuleset(string id)
        {
            CurrentGameState.MicroRulesetLibrary.RemoveAll(r => r.Id == id);
            // Clear references in all task sequences' Work steps
            foreach (var seq in CurrentGameState.TaskSequenceLibrary)
            {
                if (seq.Steps == null) continue;
                foreach (var step in seq.Steps)
                {
                    if (step.MicroRulesetId == id)
                        step.MicroRulesetId = null;
                }
            }
        }

        /// <summary>Assign a task sequence to a runner by ID.</summary>
        public void CommandAssignTaskSequenceToRunner(string runnerId, string taskSequenceId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;
            var seq = FindTaskSequenceInLibrary(taskSequenceId);
            AssignRunner(runnerId, seq, "library assign");
        }

        /// <summary>Assign a macro ruleset to a runner by ID.</summary>
        public void CommandAssignMacroRulesetToRunner(string runnerId, string rulesetId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;
            runner.MacroRulesetId = rulesetId;
            runner.MacroRuleset = FindMacroRulesetInLibrary(rulesetId); // legacy sync
        }

        /// <summary>Deep-copy a runner's macro ruleset into a new library entry, assign to runner.</summary>
        public string CommandCloneMacroRulesetForRunner(string runnerId)
        {
            var runner = FindRunner(runnerId);
            var macroRuleset = GetRunnerMacroRuleset(runner);
            if (macroRuleset == null) return null;
            var clone = macroRuleset.DeepCopy();
            clone.Name = (macroRuleset.Name ?? "Macro") + " (copy)";
            CurrentGameState.MacroRulesetLibrary.Add(clone);
            runner.MacroRulesetId = clone.Id;
            runner.MacroRuleset = clone; // legacy sync
            return clone.Id;
        }

        /// <summary>Deep-copy a micro ruleset into a new library entry. Returns new Id.</summary>
        public string CommandCloneMicroRuleset(string sourceRulesetId)
        {
            var source = FindMicroRulesetInLibrary(sourceRulesetId);
            if (source == null) return null;
            var clone = source.DeepCopy();
            clone.Name = (source.Name ?? "Micro") + " (copy)";
            CurrentGameState.MicroRulesetLibrary.Add(clone);
            return clone.Id;
        }

        // ─── Ruleset Mutation Commands ──────────────────────────────

        /// <summary>
        /// Find a ruleset in either the macro or micro library by ID.
        /// Returns (ruleset, isMacro) or (null, false) if not found.
        /// </summary>
        public (Ruleset ruleset, bool isMacro) FindRulesetInAnyLibrary(string id)
        {
            if (string.IsNullOrEmpty(id)) return (null, false);
            var macro = FindMacroRulesetInLibrary(id);
            if (macro != null) return (macro, true);
            var micro = FindMicroRulesetInLibrary(id);
            if (micro != null) return (micro, false);
            return (null, false);
        }

        /// <summary>Add a rule to a ruleset at the given index (-1 = append).</summary>
        public void CommandAddRuleToRuleset(string rulesetId, Rule rule, int insertIndex = -1)
        {
            var (ruleset, _) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;

            if (insertIndex < 0 || insertIndex >= ruleset.Rules.Count)
                ruleset.Rules.Add(rule);
            else
                ruleset.Rules.Insert(insertIndex, rule);
        }

        /// <summary>Remove a rule from a ruleset by index.</summary>
        public void CommandRemoveRuleFromRuleset(string rulesetId, int ruleIndex)
        {
            var (ruleset, _) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;
            if (ruleIndex < 0 || ruleIndex >= ruleset.Rules.Count) return;

            ruleset.Rules.RemoveAt(ruleIndex);
        }

        /// <summary>Move a rule within a ruleset (reorder).</summary>
        public void CommandMoveRuleInRuleset(string rulesetId, int fromIndex, int toIndex)
        {
            var (ruleset, _) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;
            if (fromIndex < 0 || fromIndex >= ruleset.Rules.Count) return;
            if (toIndex < 0 || toIndex >= ruleset.Rules.Count) return;
            if (fromIndex == toIndex) return;

            var rule = ruleset.Rules[fromIndex];
            ruleset.Rules.RemoveAt(fromIndex);
            ruleset.Rules.Insert(toIndex, rule);
        }

        /// <summary>Toggle a rule's Enabled flag.</summary>
        public void CommandToggleRuleEnabled(string rulesetId, int ruleIndex)
        {
            var (ruleset, _) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;
            if (ruleIndex < 0 || ruleIndex >= ruleset.Rules.Count) return;

            ruleset.Rules[ruleIndex].Enabled = !ruleset.Rules[ruleIndex].Enabled;
        }

        /// <summary>Replace a rule at the given index with an updated version.</summary>
        public void CommandUpdateRule(string rulesetId, int ruleIndex, Rule updatedRule)
        {
            var (ruleset, _) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;
            if (ruleIndex < 0 || ruleIndex >= ruleset.Rules.Count) return;

            ruleset.Rules[ruleIndex] = updatedRule;
        }

        /// <summary>Reset a ruleset to the default rules for its type (macro or micro).</summary>
        public void CommandResetRulesetToDefault(string rulesetId)
        {
            var (ruleset, isMacro) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;

            ruleset.Rules.Clear();
            // Macro default is empty (0 rules). Micro default has InventoryFull→FinishTask + Always→GatherHere.
            if (!isMacro)
            {
                var defaults = DefaultRulesets.CreateDefaultMicro();
                ruleset.Rules.AddRange(defaults.Rules);
            }
        }

        /// <summary>Rename a ruleset.</summary>
        public void CommandRenameRuleset(string rulesetId, string newName)
        {
            var (ruleset, _) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;
            ruleset.Name = newName?.Trim() ?? "";
        }

        // ─── Task Sequence Mutation Commands ─────────────────────────

        /// <summary>
        /// Add a step to a task sequence at the given index (-1 = append).
        /// Adjusts runner step indices for runners using this sequence.
        /// </summary>
        public void CommandAddStepToTaskSequence(string seqId, TaskStep step, int insertIndex = -1)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;

            int actualIndex;
            if (insertIndex < 0 || insertIndex >= seq.Steps.Count)
            {
                actualIndex = seq.Steps.Count;
                seq.Steps.Add(step);
            }
            else
            {
                actualIndex = insertIndex;
                seq.Steps.Insert(insertIndex, step);
            }

            // Adjust runner step indices: insert before current → increment
            foreach (var runner in CurrentGameState.Runners)
            {
                if (runner.TaskSequenceId != seqId) continue;
                if (actualIndex <= runner.TaskSequenceCurrentStepIndex)
                    runner.TaskSequenceCurrentStepIndex++;
            }
        }

        /// <summary>
        /// Remove a step from a task sequence by index.
        /// Adjusts runner step indices for runners using this sequence.
        /// If the sequence becomes empty, runners using it go idle.
        /// </summary>
        public void CommandRemoveStepFromTaskSequence(string seqId, int stepIndex)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;
            if (stepIndex < 0 || stepIndex >= seq.Steps.Count) return;

            seq.Steps.RemoveAt(stepIndex);

            foreach (var runner in CurrentGameState.Runners)
            {
                if (runner.TaskSequenceId != seqId) continue;

                if (seq.Steps.Count == 0)
                {
                    // Sequence is now empty — runner goes idle
                    runner.TaskSequenceId = null;
                    runner.TaskSequence = null;
                    runner.TaskSequenceCurrentStepIndex = 0;
                    runner.State = RunnerState.Idle;
                    runner.Gathering = null;
                    runner.Travel = null;
                    runner.Depositing = null;
                }
                else if (stepIndex < runner.TaskSequenceCurrentStepIndex)
                {
                    // Removed before current → decrement
                    runner.TaskSequenceCurrentStepIndex--;
                }
                else if (stepIndex == runner.TaskSequenceCurrentStepIndex)
                {
                    // Removed AT current → next step slides into position.
                    // If was the last step, clamp to new last index.
                    if (runner.TaskSequenceCurrentStepIndex >= seq.Steps.Count)
                        runner.TaskSequenceCurrentStepIndex = seq.Steps.Count - 1;
                }
                // stepIndex > current → no change needed
            }
        }

        /// <summary>
        /// Move a step within a task sequence (reorder).
        /// Adjusts runner step indices to track where their current step moved.
        /// </summary>
        public void CommandMoveStepInTaskSequence(string seqId, int fromIndex, int toIndex)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;
            if (fromIndex < 0 || fromIndex >= seq.Steps.Count) return;
            if (toIndex < 0 || toIndex >= seq.Steps.Count) return;
            if (fromIndex == toIndex) return;

            var step = seq.Steps[fromIndex];
            seq.Steps.RemoveAt(fromIndex);
            seq.Steps.Insert(toIndex, step);

            // Adjust runner step indices to follow the step they were on
            foreach (var runner in CurrentGameState.Runners)
            {
                if (runner.TaskSequenceId != seqId) continue;

                int current = runner.TaskSequenceCurrentStepIndex;
                if (current == fromIndex)
                {
                    // Runner was on the moved step — follow it
                    runner.TaskSequenceCurrentStepIndex = toIndex;
                }
                else if (fromIndex < current && toIndex >= current)
                {
                    // Step moved from before to after current → current shifts down
                    runner.TaskSequenceCurrentStepIndex--;
                }
                else if (fromIndex > current && toIndex <= current)
                {
                    // Step moved from after to before current → current shifts up
                    runner.TaskSequenceCurrentStepIndex++;
                }
            }
        }

        /// <summary>Set the Loop flag on a task sequence.</summary>
        public void CommandSetTaskSequenceLoop(string seqId, bool loop)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;
            seq.Loop = loop;
        }

        /// <summary>Set the micro ruleset on a Work step within a task sequence.</summary>
        public void CommandSetWorkStepMicroRuleset(string seqId, int stepIndex, string microRulesetId)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;
            if (stepIndex < 0 || stepIndex >= seq.Steps.Count) return;
            if (seq.Steps[stepIndex].Type != TaskStepType.Work) return;
            seq.Steps[stepIndex].MicroRulesetId = microRulesetId;
        }

        /// <summary>Set the target node on a TravelTo step within a task sequence.</summary>
        public void CommandSetStepTargetNode(string seqId, int stepIndex, string targetNodeId)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;
            if (stepIndex < 0 || stepIndex >= seq.Steps.Count) return;
            if (seq.Steps[stepIndex].Type != TaskStepType.TravelTo) return;
            seq.Steps[stepIndex].TargetNodeId = targetNodeId;
        }

        /// <summary>Rename a task sequence.</summary>
        public void CommandRenameTaskSequence(string seqId, string newName)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;
            seq.Name = newName?.Trim() ?? "";
        }

        // ─── Query Helpers ──────────────────────────────────────────

        /// <summary>Count runners currently assigned to a task sequence.</summary>
        public int CountRunnersUsingTaskSequence(string seqId)
        {
            int count = 0;
            foreach (var runner in CurrentGameState.Runners)
                if (runner.TaskSequenceId == seqId) count++;
            return count;
        }

        /// <summary>Count runners currently assigned to a macro ruleset.</summary>
        public int CountRunnersUsingMacroRuleset(string rulesetId)
        {
            int count = 0;
            foreach (var runner in CurrentGameState.Runners)
                if (runner.MacroRulesetId == rulesetId) count++;
            return count;
        }

        /// <summary>
        /// Count runners using a micro ruleset (searches Work steps in all active task sequences).
        /// </summary>
        public int CountRunnersUsingMicroRuleset(string microId)
        {
            if (string.IsNullOrEmpty(microId)) return 0;
            int count = 0;
            foreach (var runner in CurrentGameState.Runners)
            {
                var seq = FindTaskSequenceInLibrary(runner.TaskSequenceId);
                if (seq?.Steps == null) continue;
                foreach (var step in seq.Steps)
                {
                    if (step.Type == TaskStepType.Work && step.MicroRulesetId == microId)
                    {
                        count++;
                        break; // count runner once even if multiple Work steps use it
                    }
                }
            }
            return count;
        }

        /// <summary>Get names of runners using a task sequence.</summary>
        public List<string> GetRunnerNamesUsingTaskSequence(string seqId)
        {
            var names = new List<string>();
            foreach (var runner in CurrentGameState.Runners)
                if (runner.TaskSequenceId == seqId) names.Add(runner.Name);
            return names;
        }

        /// <summary>Get names of runners using a macro ruleset.</summary>
        public List<string> GetRunnerNamesUsingMacroRuleset(string rulesetId)
        {
            var names = new List<string>();
            foreach (var runner in CurrentGameState.Runners)
                if (runner.MacroRulesetId == rulesetId) names.Add(runner.Name);
            return names;
        }

        /// <summary>Get names of runners using a micro ruleset (via Work steps).</summary>
        public List<string> GetRunnerNamesUsingMicroRuleset(string microId)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(microId)) return names;
            foreach (var runner in CurrentGameState.Runners)
            {
                var seq = FindTaskSequenceInLibrary(runner.TaskSequenceId);
                if (seq?.Steps == null) continue;
                foreach (var step in seq.Steps)
                {
                    if (step.Type == TaskStepType.Work && step.MicroRulesetId == microId)
                    {
                        names.Add(runner.Name);
                        break;
                    }
                }
            }
            return names;
        }

        /// <summary>
        /// Count task sequences that reference a micro ruleset in their Work steps.
        /// </summary>
        public int CountSequencesUsingMicroRuleset(string microId)
        {
            if (string.IsNullOrEmpty(microId)) return 0;
            int count = 0;
            foreach (var seq in CurrentGameState.TaskSequenceLibrary)
            {
                if (seq.Steps == null) continue;
                foreach (var step in seq.Steps)
                {
                    if (step.Type == TaskStepType.Work && step.MicroRulesetId == microId)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        // ─── Step Index Helpers ──────────────────────────────────────

        /// <summary>
        /// Resolve the current step from the runner's progress index + the template's step list.
        /// </summary>
        private TaskStep GetCurrentStep(Runner runner, TaskSequence seq)
        {
            if (seq?.Steps == null) return null;
            int idx = runner.TaskSequenceCurrentStepIndex;
            return idx >= 0 && idx < seq.Steps.Count ? seq.Steps[idx] : null;
        }

        /// <summary>
        /// Advance the runner's step index. Returns true if there is a next step.
        /// Returns false if the sequence is done (non-looping, past the end).
        /// </summary>
        private bool AdvanceRunnerStepIndex(Runner runner, TaskSequence seq)
        {
            if (seq?.Steps == null || seq.Steps.Count == 0) return false;

            runner.TaskSequenceCurrentStepIndex++;
            if (runner.TaskSequenceCurrentStepIndex >= seq.Steps.Count)
            {
                if (seq.Loop)
                {
                    runner.TaskSequenceCurrentStepIndex = 0;
                    return true;
                }

                runner.TaskSequenceCurrentStepIndex = seq.Steps.Count; // park past end
                return false;
            }
            return true;
        }

        // ─── Internal Helpers ──────────────────────────────────────

        private void LogDecision(Runner runner, int ruleIndex, Rule rule,
            string triggerReason, string actionDetail, bool wasDeferred,
            DecisionLayer layer = DecisionLayer.Macro)
        {
            CurrentGameState.DecisionLog.Add(new DecisionLogEntry
            {
                TickNumber = CurrentGameState.TickCount,
                GameTime = CurrentGameState.TotalTimeElapsed,
                RunnerId = runner.Id,
                RunnerName = runner.Name,
                Layer = layer,
                RuleIndex = ruleIndex,
                RuleLabel = !string.IsNullOrEmpty(rule.Label) ? rule.Label : $"Rule #{ruleIndex}",
                TriggerReason = triggerReason,
                ActionType = rule.Action.Type,
                ActionDetail = actionDetail,
                WasDeferred = wasDeferred,
                ConditionSnapshot = FormatConditionSnapshot(rule, runner),
            });
        }

        private string FormatConditionSnapshot(Rule rule, Runner runner)
        {
            if (rule.Conditions == null || rule.Conditions.Count == 0)
                return "Always";

            var parts = new System.Collections.Generic.List<string>();
            foreach (var c in rule.Conditions)
            {
                string val = c.Type switch
                {
                    ConditionType.InventoryContains =>
                        $"Inv({c.StringParam})={runner.Inventory.CountItem(c.StringParam)}",
                    ConditionType.BankContains =>
                        $"Bank({c.StringParam})={CurrentGameState.Bank.CountItem(c.StringParam)}",
                    ConditionType.SkillLevel =>
                        $"{(SkillType)c.IntParam}={runner.GetSkill((SkillType)c.IntParam).Level}",
                    ConditionType.InventorySlots =>
                        $"FreeSlots={runner.Inventory.FreeSlots}",
                    _ => c.Type.ToString(),
                };
                parts.Add(val);
            }
            return string.Join(", ", parts);
        }

        private void StartTravelInternal(Runner runner, string targetNodeId)
        {
            string fromNode = runner.CurrentNodeId;
            float? startX = runner.RedirectWorldX;
            float? startZ = runner.RedirectWorldZ;
            runner.RedirectWorldX = null;
            runner.RedirectWorldZ = null;

            // If redirecting mid-travel, calculate distance from virtual position
            float distance;
            if (startX.HasValue && startZ.HasValue)
            {
                var targetNode = CurrentGameState.Map.GetNode(targetNodeId);
                if (targetNode == null) return;
                float dx = targetNode.WorldX - startX.Value;
                float dz = targetNode.WorldZ - startZ.Value;
                distance = (float)Math.Sqrt(dx * dx + dz * dz) * CurrentGameState.Map.TravelDistanceScale;
            }
            else
            {
                distance = CurrentGameState.Map.FindPath(fromNode, targetNodeId, out _);
                if (distance < 0) return;
            }

            runner.State = RunnerState.Traveling;
            runner.Travel = new TravelState
            {
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                TotalDistance = distance,
                DistanceCovered = 0f,
                StartWorldX = startX,
                StartWorldZ = startZ,
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
