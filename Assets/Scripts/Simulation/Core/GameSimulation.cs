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
        /// Ensure all runners have non-null rulesets. Old saves may have null
        /// if the field didn't exist when the save was created.
        /// </summary>
        private void MigrateRunnerRulesets()
        {
            foreach (var runner in CurrentGameState.Runners)
            {
                runner.MacroRuleset ??= DefaultRulesets.CreateDefaultMacro();
                runner.MicroRuleset ??= DefaultRulesets.CreateDefaultMicro();
            }
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

                // Publish InventoryFull event for visibility (Event Log, macro conditions)
                if (runner.Inventory.IsFull(itemDef))
                {
                    Events.Publish(new InventoryFull { RunnerId = runner.Id });
                }
            }

            // Re-evaluate micro rules every tick — may switch resources, FinishTask,
            // or handle InventoryFull (the default micro ruleset has IF InventoryFull → FinishTask).
            // Runs every tick, not just after item production, so condition changes are caught immediately.
            ReevaluateMicroDuringGathering(runner, node);
        }

        // ─── Macro Layer: Task Sequence + Step Logic ───────────────────

        /// <summary>
        /// Assign a runner to a new task sequence. Cancels current activity,
        /// publishes TaskSequenceChanged, and starts executing the first step.
        /// </summary>
        public void AssignRunner(string runnerId, TaskSequence taskSequence, string reason = "")
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            // Cancel current activity
            runner.Gathering = null;
            runner.Travel = null;
            runner.Depositing = null;
            runner.State = RunnerState.Idle;

            runner.TaskSequence = taskSequence;
            // Don't clear MacroSuspendedUntilLoop here — "Work At" sets it AFTER
            // AssignRunner, and we want it to persist through the first cycle.

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

            var seq = runner.TaskSequence;
            if (seq == null) return;

            var step = seq.CurrentStep;
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
            if (runner.CurrentNodeId == step.TargetNodeId)
            {
                if (seq.AdvanceStep())
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
                if (seq.AdvanceStep())
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
        /// Evaluate the runner's micro ruleset to decide which resource to gather.
        /// Returns:
        ///   >= 0: gatherable index to gather
        ///   -1 (MicroResultFinishTask): FinishTask — advance macro step
        ///   -2 (MicroResultNoMatch): no valid rule matched — runner is stuck, stay idle
        ///
        /// Null or empty MicroRuleset = no valid rule matched = runner is stuck (let it break).
        /// Null rulesets from old saves are migrated to defaults in LoadState.
        /// </summary>
        private int EvaluateMicroRules(Runner runner, World.WorldNode node)
        {
            var ctx = new EvaluationContext(runner, CurrentGameState, Config);
            int ruleIndex = RuleEvaluator.EvaluateRuleset(runner.MicroRuleset, ctx);

            if (ruleIndex >= 0)
            {
                var rule = runner.MicroRuleset.Rules[ruleIndex];
                var action = rule.Action;

                if (action.Type == ActionType.FinishTask)
                {
                    LogDecision(runner, ruleIndex, rule, "MicroEval",
                        "FinishTask", false, DecisionLayer.Micro);
                    return MicroResultFinishTask;
                }

                if (action.Type == ActionType.GatherHere)
                {
                    int index = action.IntParam;
                    if (index >= 0 && index < node.Gatherables.Length)
                    {
                        LogDecision(runner, ruleIndex, rule, "MicroEval",
                            $"GatherHere({index})", false, DecisionLayer.Micro);
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
        /// Re-evaluate micro rules mid-gathering (after each item gathered).
        /// If the result changes resource index, restart gathering with the new resource.
        /// If FinishTask, stop gathering and advance the macro step.
        /// </summary>
        private void ReevaluateMicroDuringGathering(Runner runner, World.WorldNode node)
        {
            if (runner.TaskSequence == null) return;

            int newIndex = EvaluateMicroRules(runner, node);

            // FinishTask — micro says "done gathering"
            if (newIndex == MicroResultFinishTask)
            {
                runner.State = RunnerState.Idle;
                runner.Gathering = null;

                if (runner.TaskSequence.AdvanceStep())
                {
                    PublishStepAdvanced(runner, runner.TaskSequence);
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
            if (runner.TaskSequence != null)
            {
                if (runner.TaskSequence.AdvanceStep())
                {
                    PublishStepAdvanced(runner, runner.TaskSequence);
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
            var step = seq.CurrentStep;
            if (step == null) return;

            // Sequence looped — clear the "Work At" one-cycle suspension
            if (seq.CurrentStepIndex == 0 && runner.MacroSuspendedUntilLoop)
                runner.MacroSuspendedUntilLoop = false;

            Events.Publish(new TaskSequenceStepAdvanced
            {
                RunnerId = runner.Id,
                StepType = step.Type,
                StepIndex = seq.CurrentStepIndex,
            });
        }

        /// <summary>
        /// Handle a non-looping sequence completing (AdvanceStep returned false).
        /// Clears the runner's task sequence, publishes completion event,
        /// and lets macro rules re-evaluate.
        /// </summary>
        private void HandleSequenceCompleted(Runner runner)
        {
            string seqName = runner.TaskSequence?.Name ?? "";
            runner.TaskSequence = null;
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

            if (runner.MacroRuleset == null || runner.MacroRuleset.Rules.Count == 0)
                return false;

            var ctx = new EvaluationContext(runner, CurrentGameState, Config);
            int ruleIndex = RuleEvaluator.EvaluateRuleset(runner.MacroRuleset, ctx);

            if (ruleIndex < 0) return false;

            var rule = runner.MacroRuleset.Rules[ruleIndex];
            var newSeq = ActionToTaskSequence(rule.Action);

            // If the new sequence is the same as current, skip (avoid infinite reassignment).
            // Both null = already idle, rule wants idle → suppress.
            if (newSeq == null && runner.TaskSequence == null)
                return false;

            if (runner.TaskSequence != null && newSeq != null
                && runner.TaskSequence.TargetNodeId == newSeq.TargetNodeId)
                return false;

            // Log the decision
            LogDecision(runner, ruleIndex, rule, triggerReason,
                newSeq != null ? $"Work @ {newSeq.TargetNodeId}" : "Idle",
                rule.FinishCurrentSequence);

            // Deferred: store as pending, apply at sequence boundary.
            // But if there's no active sequence, degrade to Immediately —
            // there's nothing to "finish" so waiting makes no sense.
            if (rule.FinishCurrentSequence && runner.TaskSequence != null)
            {
                runner.PendingTaskSequence = newSeq;
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
                        Name = "Return to Hub",
                        TargetNodeId = hubId,
                        Loop = false,
                        CurrentStepIndex = 0,
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
            if (runner.PendingTaskSequence == null) return false;

            var pending = runner.PendingTaskSequence;
            runner.PendingTaskSequence = null;

            AssignRunner(runner.Id, pending, "deferred macro rule");
            return true;
        }

        private void PublishNoMicroRuleMatched(Runner runner)
        {
            var ruleset = runner.MicroRuleset;
            Events.Publish(new NoMicroRuleMatched
            {
                RunnerId = runner.Id,
                RunnerName = runner.Name,
                NodeId = runner.CurrentNodeId,
                RulesetIsEmpty = ruleset == null || ruleset.Rules == null || ruleset.Rules.Count == 0,
                RuleCount = ruleset?.Rules?.Count ?? 0,
            });
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
