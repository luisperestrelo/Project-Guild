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
    /// The macro layer (Assignment + task steps) drives the work→deposit→return loop.
    /// Micro rules evaluate within the Work step (e.g. which resource to gather, what to fight).
    /// Macro rules change assignments based on conditions (e.g. switch nodes when bank threshold hit).
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

                    // Evaluate macro rules on inventory full
                    if (!EvaluateMacroRules(runner, "InventoryFull"))
                        AdvanceMacroStep(runner);
                }
                else
                {
                    // Re-evaluate micro rules after each item — may switch resources or FinishTask
                    ReevaluateMicroDuringGathering(runner, node);
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

            // Evaluate macro rules before executing the next step.
            // If a rule fires and changes the assignment, AssignRunner will call
            // AdvanceMacroStep again. Same-assignment suppression prevents infinite loops.
            if (EvaluateMacroRules(runner, "StepAdvance")) return;

            var assignment = runner.Assignment;
            if (assignment == null) return;

            var step = assignment.CurrentStep;
            if (step == null) return;

            switch (step.Type)
            {
                case TaskStepType.TravelTo:
                    ExecuteTravelStep(runner, step, assignment);
                    break;

                case TaskStepType.Work:
                    ExecuteWorkStep(runner, assignment);
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

        private void ExecuteWorkStep(Runner runner, Assignment assignment)
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

            // Evaluate micro rules to decide which resource to gather
            int gatherableIndex = EvaluateMicroRules(runner, node);

            // FinishTask signal — micro says "done gathering, advance macro"
            if (gatherableIndex == MicroResultFinishTask)
            {
                if (assignment.AdvanceStep())
                {
                    PublishStepAdvanced(runner, assignment);
                }
                AdvanceMacroStep(runner);
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

            // Step index stays on Work so the display correctly shows what the
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
                var action = runner.MicroRuleset.Rules[ruleIndex].Action;

                if (action.Type == ActionType.FinishTask)
                    return MicroResultFinishTask;

                if (action.Type == ActionType.GatherHere)
                {
                    int index = action.IntParam;
                    if (index >= 0 && index < node.Gatherables.Length)
                        return index;
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
            if (runner.Assignment == null) return;

            int newIndex = EvaluateMicroRules(runner, node);

            // FinishTask — micro says "done gathering"
            if (newIndex == MicroResultFinishTask)
            {
                runner.State = RunnerState.Idle;
                runner.Gathering = null;

                if (runner.Assignment.AdvanceStep())
                {
                    PublishStepAdvanced(runner, runner.Assignment);
                }
                AdvanceMacroStep(runner);
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
            if (runner.Assignment != null && runner.Assignment.AdvanceStep())
            {
                PublishStepAdvanced(runner, runner.Assignment);
            }

            // Loop boundary — apply pending assignment from earlier in the loop
            if (ApplyPendingAssignment(runner)) return;

            // Evaluate macro rules at deposit completion
            if (EvaluateMacroRules(runner, "DepositCompleted")) return;

            // Apply pending set by the macro eval above (deferred rule that fired at deposit boundary)
            if (ApplyPendingAssignment(runner)) return;

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

        // ─── Macro Layer: Rule Evaluation ────────────────────────────

        /// <summary>
        /// Evaluate the runner's macro ruleset. If a rule matches, its action maps
        /// to a new assignment. FinishCurrentTrip defers via PendingAssignment.
        /// Returns true if the runner's assignment was changed (immediate), meaning
        /// the caller should NOT proceed with normal step advancement.
        /// </summary>
        private bool EvaluateMacroRules(Runner runner, string triggerReason)
        {
            if (runner.MacroRuleset == null || runner.MacroRuleset.Rules.Count == 0)
                return false;

            var ctx = new EvaluationContext(runner, CurrentGameState, Config);
            int ruleIndex = RuleEvaluator.EvaluateRuleset(runner.MacroRuleset, ctx);

            if (ruleIndex < 0) return false;

            var rule = runner.MacroRuleset.Rules[ruleIndex];
            var newAssignment = ActionToAssignment(rule.Action);

            // If the new assignment is the same as current, skip (avoid infinite reassignment).
            // Both null = already idle, rule wants idle → suppress.
            if (newAssignment == null && runner.Assignment == null)
                return false;

            if (runner.Assignment != null && newAssignment != null
                && runner.Assignment.TargetNodeId == newAssignment.TargetNodeId)
                return false;

            // Log the decision
            LogDecision(runner, ruleIndex, rule, triggerReason,
                newAssignment != null ? $"Work @ {newAssignment.TargetNodeId}" : "Idle",
                rule.FinishCurrentTrip && rule.Action.Type != ActionType.FleeToHub);

            // FleeToHub always immediate, ignores FinishCurrentTrip
            if (rule.Action.Type == ActionType.FleeToHub)
            {
                string hubId = CurrentGameState.Map?.HubNodeId ?? "hub";
                AssignRunner(runner.Id, null, "flee to hub");
                // Start travel to hub directly
                if (runner.CurrentNodeId != hubId)
                    StartTravelInternal(runner, hubId);
                return true;
            }

            // Deferred: store as pending, apply at loop boundary
            if (rule.FinishCurrentTrip)
            {
                runner.PendingAssignment = newAssignment;
                return false;
            }

            // Immediate: change assignment now
            AssignRunner(runner.Id, newAssignment, $"macro rule: {rule.Label}");
            return true;
        }

        /// <summary>
        /// Map a macro rule's action to an Assignment.
        /// Returns null for Idle (clear assignment).
        /// </summary>
        private Assignment ActionToAssignment(AutomationAction action)
        {
            string hubId = CurrentGameState.Map?.HubNodeId ?? "hub";

            switch (action.Type)
            {
                case ActionType.WorkAt:
                    string nodeId = string.IsNullOrEmpty(action.StringParam) ? null : action.StringParam;
                    if (nodeId == null) return null;
                    return Assignment.CreateLoop(nodeId, hubId);

                case ActionType.Idle:
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Check and apply PendingAssignment at loop boundaries.
        /// Called after a step advances and wraps to step 0 (start of a new loop cycle).
        /// </summary>
        private bool ApplyPendingAssignment(Runner runner)
        {
            if (runner.PendingAssignment == null) return false;

            var pending = runner.PendingAssignment;
            runner.PendingAssignment = null;

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
            string triggerReason, string actionDetail, bool wasDeferred)
        {
            CurrentGameState.DecisionLog.Add(new DecisionLogEntry
            {
                TickNumber = CurrentGameState.TickCount,
                GameTime = CurrentGameState.TotalTimeElapsed,
                RunnerId = runner.Id,
                RunnerName = runner.Name,
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
