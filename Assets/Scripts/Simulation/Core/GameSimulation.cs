using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Crafting;
using ProjectGuild.Simulation.Gathering;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.Tutorial;
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
        public ChronicleService Chronicle { get; private set; }
        public TutorialService Tutorial { get; private set; }

        /// <summary>
        /// Optional provider for real path distances (e.g. NavMesh-backed).
        /// When set, travel distance calculations try the provider first and fall
        /// back to Euclidean/FindPath math when the provider returns null.
        /// Set by the view layer after construction (not a constructor parameter).
        /// </summary>
        public IPathDistanceProvider PathDistanceProvider { get; set; }

        /// <summary>
        /// Optional provider for node interior geometry. When set, travel gets an
        /// "exiting node" phase where the runner walks to the node edge before
        /// overworld travel begins. Returns null when scene not loaded → instant exit.
        /// Set by the view layer after construction (same pattern as PathDistanceProvider).
        /// </summary>
        public INodeGeometryProvider NodeGeometryProvider { get; set; }

        private readonly System.Random _random = new();

        // Stash for the last CraftHere recipe ID from micro evaluation
        private string _lastCraftRecipeId;


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
            Chronicle = new ChronicleService(Config.ChronicleMaxEntries, () => CurrentGameState, () => Config);
            Chronicle.SubscribeAll(Events);
            Tutorial = new TutorialService(() => CurrentGameState, Events, TickDeltaTime);
            Tutorial.SubscribeAll(Events);

            // Tutorial pawn award: spawn new runner when milestone completes
            Events.Subscribe<TutorialMilestoneCompleted>(OnTutorialMilestone);
        }

        private void OnTutorialMilestone(TutorialMilestoneCompleted e)
        {
            if (e.MilestoneId == TutorialMilestones.NewPawnAwarded)
            {
                SpawnTutorialRewardRunner();
                // Transition to Complete phase
                var state = CurrentGameState;
                if (state.Tutorial.CurrentPhase != TutorialPhase.Complete)
                {
                    state.Tutorial.CompleteMilestone(TutorialMilestones.TutorialFinished);
                    var completed = state.Tutorial.CurrentPhase;
                    state.Tutorial.CurrentPhase = TutorialPhase.Complete;
                    Events.Publish(new TutorialPhaseCompleted
                    {
                        CompletedPhase = completed,
                        NextPhase = TutorialPhase.Complete,
                    });
                }
            }
        }

        /// <summary>
        /// Load an existing game state (for save/load).
        /// </summary>
        public void LoadState(GameState state)
        {
            CurrentGameState = state;
            CurrentGameState.Map?.Initialize();

            // Re-populate item registry
            ItemRegistry = new ItemRegistry();
            foreach (var itemDef in Config.ItemDefinitions)
                ItemRegistry.Register(itemDef);
            RegisterCraftingItems();

            DefaultRulesets.EnsureInLibrary(CurrentGameState);
            DefaultRulesets.EnsureTemplatesInLibrary(CurrentGameState);
            RefreshMacroConfigWarnings();
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

            // Register crafting output items (demo: hardcoded here)
            RegisterCraftingItems();

            // Initialize decision log max entries from config
            CurrentGameState.MacroDecisionLog.SetMaxEntries(Config.MacroDecisionLogMaxEntries);
            CurrentGameState.MicroDecisionLog.SetMaxEntries(Config.MicroDecisionLogMaxEntries);
            CurrentGameState.CombatDecisionLog.SetMaxEntries(Config.CombatDecisionLogMaxEntries);

            // Ensure default rulesets and templates exist in the library before creating runners
            DefaultRulesets.EnsureInLibrary(CurrentGameState);
            DefaultRulesets.EnsureTemplatesInLibrary(CurrentGameState);

            var rng = new Random();
            var usedNames = new System.Collections.Generic.HashSet<string>();
            foreach (var def in starterDefinitions)
            {
                var runner = RunnerFactory.CreateFromDefinition(def, hubNodeId, Config.InventorySize, rng, Config);

                // Ensure unique names among starters (re-roll on collision)
                if (def.Name == null)
                {
                    int attempts = 0;
                    while (usedNames.Contains(runner.Name) && attempts < 50)
                    {
                        runner.Name = RunnerFactory.GenerateRandomName(rng, Config, runner.Gender);
                        attempts++;
                    }
                }
                usedNames.Add(runner.Name);

                RestoreRunnerToFull(runner);
                CurrentGameState.Runners.Add(runner);
                Events.Publish(new RunnerCreated
                {
                    RunnerId = runner.Id,
                    RunnerName = runner.Name,
                });
            }

            // Assign default combat styles based on runner aptitude
            AssignDefaultCombatStyles();

            // Initialize tutorial for new game
            Tutorial.InitializeDiscoveredNodes();
            Tutorial.CompleteIntroMilestone();
        }

        /// <summary>
        /// Assign a default combat style to each starter runner based on their highest combat passion.
        /// Melee passion → Basic Melee, Magic passion → Basic Mage, Restoration passion → Basic Healer.
        /// </summary>
        private void AssignDefaultCombatStyles()
        {
            foreach (var runner in CurrentGameState.Runners)
            {
                if (!string.IsNullOrEmpty(runner.CombatStyleId)) continue;

                var melee = runner.GetSkill(SkillType.Melee);
                var magic = runner.GetSkill(SkillType.Magic);
                var resto = runner.GetSkill(SkillType.Restoration);

                // Pick style based on passion first, then level
                if (resto.HasPassion)
                    runner.CombatStyleId = DefaultRulesets.BasicHealerCombatStyleId;
                else if (magic.HasPassion)
                    runner.CombatStyleId = DefaultRulesets.BasicMageCombatStyleId;
                else if (melee.HasPassion)
                    runner.CombatStyleId = DefaultRulesets.BasicMeleeCombatStyleId;
                else if (resto.Level >= magic.Level && resto.Level >= melee.Level)
                    runner.CombatStyleId = DefaultRulesets.BasicHealerCombatStyleId;
                else if (magic.Level >= melee.Level)
                    runner.CombatStyleId = DefaultRulesets.BasicMageCombatStyleId;
                else
                    runner.CombatStyleId = DefaultRulesets.BasicMeleeCombatStyleId;
            }
        }

        /// <summary>
        /// Add a runner to the simulation at runtime. Publishes RunnerCreated.
        /// </summary>
        public void AddRunner(Runner runner)
        {
            RestoreRunnerToFull(runner);
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
                    .WithSkill(SkillType.Hitpoints, 7)
                    .WithSkill(SkillType.Athletics, 3),

                // Pawn 2: agile mage (passions: Magic, Athletics)
                new RunnerFactory.RunnerDefinition()
                    .WithSkill(SkillType.Magic, 4, true)
                    .WithSkill(SkillType.Athletics, 4, true)
                    .WithSkill(SkillType.Hitpoints, 2)
                    .WithSkill(SkillType.Ranged, 2),

                // Pawn 3: healer/precision (passions: Restoration, Execution)
                new RunnerFactory.RunnerDefinition()
                    .WithSkill(SkillType.Restoration, 5, true)
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

            TickEncounters();

            for (int i = 0; i < CurrentGameState.Runners.Count; i++)
            {
                TickRunner(CurrentGameState.Runners[i]);
            }

            Events.Publish(new SimulationTickCompleted { TickNumber = CurrentGameState.TickCount });
        }

        private void TickRunner(Runner runner)
        {
            // Mana regen every tick for all living runners (not just during combat)
            if (runner.State != RunnerState.Dead)
            {
                float maxMana = CombatFormulas.CalculateMaxMana(
                    runner.GetEffectiveLevel(SkillType.Restoration, Config), Config);
                runner.CurrentMana = Math.Min(runner.CurrentMana + Config.BaseManaRegenPerTick, maxMana);
            }

            // Dead runners tick their respawn timer only. No macro eval, no other processing.
            if (runner.State == RunnerState.Dead)
            {
                TickDead(runner);
                return;
            }

            // Evaluate macro rules every tick for all active (non-Idle) runners.
            // Idle runners go through ExecuteCurrentStep which evaluates macros then executes the current step.
            // An Immediate rule that fires here will call AssignRunner, changing state — skip the rest of the tick.
            if (runner.State != RunnerState.Idle)
            {
                if (EvaluateMacroRules(runner, "Tick"))
                    return;
            }

            switch (runner.State)
            {
                case RunnerState.Idle:
                    ExecuteCurrentStep(runner);
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

                case RunnerState.Fighting:
                    TickFighting(runner);
                    break;

                case RunnerState.Waiting:
                    TickWaiting(runner);
                    break;

                case RunnerState.Crafting:
                    TickCrafting(runner);
                    break;
            }
        }

        private float GetTravelSpeed(Runner runner)
        {
            float athleticsLevel = runner.GetEffectiveLevel(SkillType.Athletics, Config);
            return Config.BaseTravelSpeed + (athleticsLevel - 1) * Config.AthleticsSpeedPerLevel;
        }

        private float GetInNodeTravelSpeed(Runner runner)
        {
            return GetTravelSpeed(runner) * Config.InNodeSpeedMultiplier;
        }

        private void TickTravel(Runner runner)
        {
            if (runner.Travel == null) return;

            // Award athletics XP every tick while traveling (both exit and overworld phases)
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

            // ─── Exit phase: walk to node edge at in-node speed ───
            if (runner.Travel.IsExitingNode)
            {
                float inNodeSpeed = GetInNodeTravelSpeed(runner);
                runner.Travel.ExitDistanceCovered += inNodeSpeed * TickDeltaTime;

                if (runner.Travel.IsExitingNode)
                    return; // Still exiting — don't tick overworld yet
                // else: exit just completed, fall through to first overworld tick
            }

            // ─── Overworld phase: travel between nodes ───
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

                // Heal to full HP/mana when arriving at hub
                if (arrivedNodeId == CurrentGameState.Map.HubNodeId)
                    RestoreRunnerToFull(runner);

                // Evaluate macro rules on arrival
                if (EvaluateMacroRules(runner, "ArrivedAtNode")) return;

                // Immediately try to advance macro step now that we're idle
                ExecuteCurrentStep(runner);
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

                // Redirect during exit phase: runner hasn't left the node yet.
                // Cancel exit and start fresh travel from the current node.
                if (runner.Travel.IsExitingNode)
                {
                    runner.State = RunnerState.Idle;
                    runner.Travel = null;
                    return CommandTravel(runnerId, targetNodeId);
                }

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

                float? redirectDist = PathDistanceProvider?.GetTravelDistance(runnerId, virtualX, virtualZ, targetNodeId);
                float distToNewTarget;
                if (redirectDist.HasValue)
                {
                    distToNewTarget = redirectDist.Value;
                }
                else
                {
                    float dx = newTarget.WorldX - virtualX;
                    float dz = newTarget.WorldZ - virtualZ;
                    float raw = (float)Math.Sqrt(dx * dx + dz * dz) - newTarget.ApproachRadius;
                    distToNewTarget = Math.Max(raw, 0.1f);
                }

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

            float? providerDist = PathDistanceProvider?.GetTravelDistance(runnerId, runner.CurrentNodeId, targetNodeId);
            float distance = providerDist ?? CurrentGameState.Map.FindPath(runner.CurrentNodeId, targetNodeId, out _);
            if (distance < 0) return false;

            float exitDist = NodeGeometryProvider?.GetExitDistance(runnerId, runner.CurrentNodeId, targetNodeId) ?? 0f;

            string fromNodeId = runner.CurrentNodeId;
            runner.ActiveWarning = null;
            runner.State = RunnerState.Traveling;
            runner.Travel = new TravelState
            {
                FromNodeId = fromNodeId,
                ToNodeId = targetNodeId,
                TotalDistance = distance,
                DistanceCovered = 0f,
                ExitDistance = exitDist,
                ExitDistanceCovered = 0f,
            };

            float travelSpeed = GetTravelSpeed(runner);
            float inNodeSpeed = GetInNodeTravelSpeed(runner);
            float exitDuration = exitDist > 0f ? exitDist / inNodeSpeed : 0f;
            float overworldDuration = distance / travelSpeed;

            Events.Publish(new RunnerStartedTravel
            {
                RunnerId = runner.Id,
                FromNodeId = fromNodeId,
                ToNodeId = targetNodeId,
                EstimatedDurationSeconds = exitDuration + overworldDuration,
            });

            return true;
        }

        /// <summary>
        /// Command a runner to travel with an explicit distance (for testing).
        /// Optionally specify exit distance to test the exit phase.
        /// </summary>
        public void CommandTravel(string runnerId, string targetNodeId, float distance, float exitDistance = 0f)
        {
            var runner = FindRunner(runnerId);
            if (runner == null || runner.State == RunnerState.Dead) return;

            string fromNode = runner.CurrentNodeId;
            runner.ActiveWarning = null;
            runner.State = RunnerState.Traveling;
            runner.Travel = new TravelState
            {
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                TotalDistance = distance,
                DistanceCovered = 0f,
                ExitDistance = exitDistance,
                ExitDistanceCovered = 0f,
            };

            float speed = GetTravelSpeed(runner);
            float inNodeSpeed = GetInNodeTravelSpeed(runner);
            float exitDuration = exitDistance > 0f ? exitDistance / inNodeSpeed : 0f;
            Events.Publish(new RunnerStartedTravel
            {
                RunnerId = runner.Id,
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                EstimatedDurationSeconds = exitDuration + distance / speed,
            });
        }

        // ─── Gathering ─────────────────────────────────────────────

        private void StartGathering(Runner runner, int gatherableIndex, GatherableConfig gatherableConfig)
        {
            float ticksRequired = CalculateTicksRequired(runner, gatherableConfig);
            int spotIndex = CountRunnersAtGatherable(runner.Id, runner.CurrentNodeId, gatherableIndex);

            float transitDist = NodeGeometryProvider?.GetGatheringSpotDistance(
                runner.Id, runner.CurrentNodeId, gatherableIndex, spotIndex) ?? 0f;

            runner.ActiveWarning = null;
            runner.State = RunnerState.Gathering;
            runner.Gathering = new GatheringState
            {
                NodeId = runner.CurrentNodeId,
                GatherableIndex = gatherableIndex,
                SpotIndex = spotIndex,
                TickAccumulator = 0f,
                TicksRequired = ticksRequired,
                TransitDistance = transitDist,
                TransitDistanceCovered = 0f,
            };

            Events.Publish(new GatheringStarted
            {
                RunnerId = runner.Id,
                NodeId = runner.CurrentNodeId,
                ItemId = gatherableConfig.ProducedItemId,
                Skill = gatherableConfig.RequiredSkill,
            });
        }

        /// <summary>
        /// Count how many other runners are already gathering the same gatherable at this node.
        /// Used as the spot index so each runner gets a distinct physical position.
        /// </summary>
        private int CountRunnersAtGatherable(string excludeRunnerId, string nodeId, int gatherableIndex)
        {
            int count = 0;
            foreach (var r in CurrentGameState.Runners)
            {
                if (r.Id == excludeRunnerId) continue;
                if (r.CurrentNodeId == nodeId
                    && r.State == RunnerState.Gathering
                    && r.Gathering != null
                    && r.Gathering.GatherableIndex == gatherableIndex)
                {
                    count++;
                }
            }
            return count;
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

            // Transit gate: runner walks to gathering spot before any production/XP
            if (runner.Gathering.IsInTransit)
            {
                float inNodeSpeed = GetInNodeTravelSpeed(runner);
                runner.Gathering.TransitDistanceCovered += inNodeSpeed * TickDeltaTime;
                if (runner.Gathering.IsInTransit) return; // still walking
                // Fall through to first gathering tick
            }

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

            // Action Commitment: re-evaluate micro rules only on item completion.
            // Interrupt-flagged rules are checked every tick (e.g. InventoryFull -> FinishTask
            // when CanInterrupt=true). Non-interrupt rules wait for the action boundary.
            if (itemJustProduced)
            {
                ReevaluateMicroDuringGathering(runner, node, itemJustProduced: true);
            }
            else
            {
                ReevaluateInterruptRulesDuringGathering(runner, node);
            }
        }

        // ─── Macro Layer: Task Sequence + Step Logic ───────────────────

        /// <summary>
        /// Send a runner to work at a node with one guaranteed cycle
        /// (macro rules suspended until the sequence loops).
        /// Automatically picks gather or fight based on node content.
        /// This is the single entry point for the "Work At" / "Send To" player action.
        /// </summary>
        public void CommandWorkAtSuspendMacrosForOneCycle(string runnerId, string nodeId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            var hubId = CurrentGameState.Map.HubNodeId;
            var node = CurrentGameState.Map.GetNode(nodeId);

            TaskSequence taskSeq;
            string nodeName = node?.Name ?? nodeId;
            if (node != null && node.EnemySpawns.Length > 0 && node.Gatherables.Length == 0)
            {
                taskSeq = FindMatchingCombatLoop(nodeId, hubId)
                    ?? TaskSequence.CreateCombatLoop(nodeId, hubId, nodeName: nodeName);
            }
            else
            {
                taskSeq = FindMatchingGatherLoop(nodeId, hubId)
                    ?? TaskSequence.CreateLoop(nodeId, hubId, nodeName: nodeName);
            }

            runner.MacroSuspendedUntilLoop = true;
            AssignRunner(runnerId, taskSeq, "Work At");
        }

        /// <summary>
        /// Search the TaskSequenceLibrary for an existing standard gather loop
        /// that exactly matches: same node, looping, 4-step pattern (TravelTo → Work → TravelTo(hub) → Deposit),
        /// and the Work step uses the default micro ruleset.
        /// Returns null if no match found, signaling that a new sequence should be created.
        /// </summary>
        public TaskSequence FindMatchingGatherLoop(string nodeId, string hubId)
        {
            foreach (var seq in CurrentGameState.TaskSequenceLibrary)
            {
                if (seq.TargetNodeId != nodeId) continue;
                if (!seq.Loop) continue;
                if (seq.Steps == null || seq.Steps.Count != 4) continue;

                var s0 = seq.Steps[0];
                var s1 = seq.Steps[1];
                var s2 = seq.Steps[2];
                var s3 = seq.Steps[3];

                if (s0.Type != TaskStepType.TravelTo || s0.TargetNodeId != nodeId) continue;
                if (s1.Type != TaskStepType.Work) continue;
                if (s1.MicroRulesetId != DefaultRulesets.DefaultMicroId) continue;
                if (s2.Type != TaskStepType.TravelTo || s2.TargetNodeId != hubId) continue;
                if (s3.Type != TaskStepType.Deposit) continue;

                return seq;
            }
            return null;
        }

        /// <summary>
        /// Search for an existing combat loop sequence matching the given node.
        /// </summary>
        public TaskSequence FindMatchingCombatLoop(string nodeId, string hubId)
        {
            foreach (var seq in CurrentGameState.TaskSequenceLibrary)
            {
                if (seq.TargetNodeId != nodeId) continue;
                if (!seq.Loop) continue;
                if (seq.Steps == null || seq.Steps.Count != 4) continue;

                var s0 = seq.Steps[0];
                var s1 = seq.Steps[1];
                var s2 = seq.Steps[2];
                var s3 = seq.Steps[3];

                if (s0.Type != TaskStepType.TravelTo || s0.TargetNodeId != nodeId) continue;
                if (s1.Type != TaskStepType.Work) continue;
                if (s1.MicroRulesetId != DefaultRulesets.DefaultCombatMicroId) continue;
                if (s2.Type != TaskStepType.TravelTo || s2.TargetNodeId != hubId) continue;
                if (s3.Type != TaskStepType.Deposit) continue;

                return seq;
            }
            return null;
        }

        /// <summary>
        /// Send a runner to the hub to deposit. Uses an existing "Send to Hub" sequence
        /// from the library if one matches, otherwise creates a new one.
        /// </summary>
        public void CommandSendToHub(string runnerId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            var hubId = CurrentGameState.Map.HubNodeId;
            var taskSeq = FindMatchingSendToHub(hubId) ?? TaskSequence.CreateSendToHub(hubId);
            runner.MacroSuspendedUntilLoop = true;
            AssignRunner(runnerId, taskSeq, "Send to Hub");
        }

        /// <summary>
        /// Search the TaskSequenceLibrary for an existing "Send to Hub" sequence:
        /// non-looping, 2 steps (TravelTo hub, Deposit).
        /// </summary>
        public TaskSequence FindMatchingSendToHub(string hubId)
        {
            foreach (var seq in CurrentGameState.TaskSequenceLibrary)
            {
                if (seq.TargetNodeId != hubId) continue;
                if (seq.Loop) continue;
                if (seq.Steps == null || seq.Steps.Count != 2) continue;

                var s0 = seq.Steps[0];
                var s1 = seq.Steps[1];

                if (s0.Type != TaskStepType.TravelTo || s0.TargetNodeId != hubId) continue;
                if (s1.Type != TaskStepType.Deposit) continue;

                return seq;
            }
            return null;
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
            runner.PendingTaskSequenceId = null;
            runner.ActiveWarning = null;

            // If mid-travel, let the runner finish traveling to the destination
            // instead of teleporting back to the departure node.
            if (runner.State == RunnerState.Traveling)
            {
                runner.TaskSequenceId = null;
                runner.TaskSequenceCurrentStepIndex = 0;
                runner.CompletedAtLeastOneCycle = false;
                runner.LastCompletedTaskSequenceId = null;

                Events.Publish(new TaskSequenceChanged
                {
                    RunnerId = runner.Id,
                    Reason = "manual clear",
                });
                // Travel continues via TickTravel. On arrival, runner becomes Idle
                // with no sequence — macros re-evaluate or runner stays idle.
                return;
            }

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

            runner.ActiveWarning = null;

            // Cancel current activity — if mid-travel, capture virtual position for redirect.
            // Exit-phase runners (still walking out of a node) are NOT mid-overworld:
            // cancel their exit and let the new travel start fresh with its own exit distance.
            float? redirectWorldX = null;
            float? redirectWorldZ = null;
            if (runner.State == RunnerState.Traveling && runner.Travel != null
                && !runner.Travel.IsExitingNode)
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

            // If pulling a fighting runner out, clean up the encounter
            string combatNodeId = runner.Fighting?.NodeId;

            runner.Gathering = null;
            runner.Travel = null;
            runner.Depositing = null;
            runner.Fighting = null;
            runner.Death = null;
            runner.State = RunnerState.Idle;

            if (combatNodeId != null)
                CleanupEncounterIfEmpty(combatNodeId);
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
            runner.TaskSequenceCurrentStepIndex = 0;
            runner.CompletedAtLeastOneCycle = false;
            runner.LastCompletedTaskSequenceId = null;
            runner.PendingTaskSequenceId = null; // immediate assignment supersedes any deferred
            runner.MicroOverrides?.Clear(); // fresh assignment = clean slate for overrides
            // Don't clear MacroSuspendedUntilLoop here — WorkAt() sets it before
            // calling AssignRunner, and we want it to persist through the first cycle.

            Events.Publish(new TaskSequenceChanged
            {
                RunnerId = runner.Id,
                TargetNodeId = taskSequence?.TargetNodeId,
                Reason = reason,
            });

            // Start executing the first step
            ExecuteCurrentStep(runner);
        }

        /// <summary>
        /// Execute the current step of the runner's task sequence.
        /// Called when the runner becomes Idle (after arriving, after depositing, etc.).
        ///
        /// Uses an iterative loop instead of recursion: some steps complete instantly
        /// (TravelTo when already at target, Work when FinishTask fires immediately).
        /// The loop keeps advancing until a step starts async work (travel, gathering,
        /// depositing) or the runner gets stuck. Max iterations = step count + 1 to
        /// prevent infinite loops from misconfigured sequences.
        /// </summary>
        private void ExecuteCurrentStep(Runner runner)
        {
            var seq = GetRunnerTaskSequence(runner);
            int maxIterations = (seq?.Steps?.Count ?? 0) + 1;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                if (runner.State != RunnerState.Idle) return;

                // At loop boundary (step 0 after completing a full cycle): apply any pending
                // deferred assignment. CompletedAtLeastOneCycle distinguishes "just assigned,
                // starting at step 0" from "looped back to step 0 after finishing all steps."
                if (runner.TaskSequenceCurrentStepIndex == 0 && runner.CompletedAtLeastOneCycle
                    && ApplyPendingTaskSequence(runner))
                    return;

                // Evaluate macro rules before executing the next step.
                // If a rule fires and changes the task sequence, AssignRunner will call
                // ExecuteCurrentStep. Same-sequence suppression prevents infinite loops.
                if (EvaluateMacroRules(runner, "StepAdvance")) return;

                // Re-fetch sequence (macro rules may have changed it)
                seq = GetRunnerTaskSequence(runner);
                if (seq == null) return;

                if (seq.Steps == null || seq.Steps.Count == 0)
                {
                    runner.ActiveWarning = RunnerWarnings.NoSteps;
                    return;
                }

                var step = GetCurrentStep(runner, seq);
                if (step == null) return;

                bool completedInstantly = false;
                switch (step.Type)
                {
                    case TaskStepType.TravelTo:
                        completedInstantly = ExecuteTravelStep(runner, step, seq);
                        break;

                    case TaskStepType.Work:
                        completedInstantly = ExecuteWorkStep(runner, seq);
                        break;

                    case TaskStepType.Deposit:
                        ExecuteDepositStep(runner, seq);
                        return; // always async
                }

                if (!completedInstantly) return;
                // Step completed instantly — loop continues to next step
            }

            // Exhausted max iterations — sequence is cycling without making progress.
            runner.ActiveWarning = RunnerWarnings.LoopingWithoutProgress;
        }

        /// <summary>
        /// Returns true if the step completed instantly (already at target),
        /// false if async work started (travel) or runner got stuck.
        /// </summary>
        private bool ExecuteTravelStep(Runner runner, TaskStep step, TaskSequence seq)
        {
            // Already at target? Step is done — advance past it.
            // But if there's a redirect position, the runner was interrupted mid-travel
            // and isn't actually at CurrentNodeId — they need to travel.
            if (runner.CurrentNodeId == step.TargetNodeId && !runner.RedirectWorldX.HasValue)
            {
                if (AdvanceRunnerStepIndex(runner, seq))
                {
                    PublishStepAdvanced(runner, seq);
                    return true; // instant — loop continues
                }
                else
                {
                    HandleSequenceCompleted(runner);
                    return false; // sequence ended
                }
            }

            // Start traveling. Step index stays on TravelTo so the display
            // correctly shows what the runner is doing. When travel completes
            // (Idle → ExecuteCurrentStep), it will see "already at target" and advance.
            StartTravelInternal(runner, step.TargetNodeId);
            return false; // async work started
        }

        /// <summary>
        /// Returns true if the step completed instantly (FinishTask fired),
        /// false if async work started (gathering) or runner got stuck.
        /// </summary>
        private bool ExecuteWorkStep(Runner runner, TaskSequence seq)
        {
            var node = CurrentGameState.Map.GetNode(runner.CurrentNodeId);

            // Evaluate micro rules to decide what to do at this Work step
            int gatherableIndex = EvaluateMicroRules(runner, node);

            // FightHere signal — micro says "fight enemies at this node"
            if (gatherableIndex == MicroResultFightHere)
            {
                StartFighting(runner, node);
                return false; // async work started
            }

            // Wait signal — micro says "wait for conditions to change"
            if (gatherableIndex == MicroResultWait)
            {
                runner.ActiveWarning = null;
                runner.State = RunnerState.Waiting;
                runner.Waiting = new WaitingState { NodeId = runner.CurrentNodeId };
                return false; // async wait started
            }

            // CraftHere signal — micro says "craft at this station"
            if (gatherableIndex == MicroResultCraftHere)
            {
                // Find the recipe from the last matched rule's action
                string recipeId = GetLastMatchedCraftRecipeId(runner, node);
                if (recipeId != null && StartCrafting(runner, recipeId))
                    return false; // async crafting started
                // Can't craft (missing materials etc) — stuck
                runner.ActiveWarning = "Cannot craft: missing materials";
                return false;
            }

            // For gathering results, node must have gatherables
            if (node == null || node.Gatherables.Length == 0)
            {
                // No gatherables at this node — runner stays stuck. "Let it break."
                runner.ActiveWarning = RunnerWarnings.NoGatherablesAtNode;
                Events.Publish(new GatheringFailed
                {
                    RunnerId = runner.Id,
                    NodeId = runner.CurrentNodeId,
                    Reason = GatheringFailureReason.NoGatherablesAtNode,
                });
                return false; // stuck
            }

            // FinishTask signal — micro says "done gathering, advance macro"
            if (gatherableIndex == MicroResultFinishTask)
            {
                if (AdvanceRunnerStepIndex(runner, seq))
                {
                    PublishStepAdvanced(runner, seq);
                    return true; // instant — loop continues
                }
                else
                {
                    HandleSequenceCompleted(runner);
                    return false; // sequence ended
                }
            }

            // No valid rule matched — runner is stuck. Stay idle at the Work step.
            if (gatherableIndex == MicroResultNoMatch)
            {
                PublishNoMicroRuleMatched(runner);
                return false; // stuck
            }

            // GatherAny found no eligible gatherables (all above runner's skill level).
            if (gatherableIndex == MicroResultNoEligibleGatherable)
            {
                // Find the highest-MinLevel gatherable to report a useful error
                var highestGatherable = node.Gatherables[0];
                for (int i = 1; i < node.Gatherables.Length; i++)
                {
                    if (node.Gatherables[i].MinLevel > highestGatherable.MinLevel)
                        highestGatherable = node.Gatherables[i];
                }
                var reportSkill = runner.GetSkill(highestGatherable.RequiredSkill);

                runner.ActiveWarning = RunnerWarnings.SkillTooLow(highestGatherable.RequiredSkill, reportSkill.Level, highestGatherable.MinLevel);

                Events.Publish(new GatheringFailed
                {
                    RunnerId = runner.Id,
                    NodeId = runner.CurrentNodeId,
                    ItemId = highestGatherable.ProducedItemId,
                    Skill = highestGatherable.RequiredSkill,
                    RequiredLevel = highestGatherable.MinLevel,
                    CurrentLevel = reportSkill.Level,
                    Reason = GatheringFailureReason.NotEnoughSkill,
                });
                return false; // stuck
            }

            var gatherableConfig = node.Gatherables[gatherableIndex];

            // Check skill level requirement
            if (gatherableConfig.MinLevel > 0)
            {
                var skill = runner.GetSkill(gatherableConfig.RequiredSkill);
                if (skill.Level < gatherableConfig.MinLevel)
                {
                    // Can't gather — runner stays stuck at Work step. "Let it break."
                    runner.ActiveWarning = RunnerWarnings.SkillTooLow(gatherableConfig.RequiredSkill, skill.Level, gatherableConfig.MinLevel);

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
                    return false; // stuck
                }
            }

            StartGathering(runner, gatherableIndex, gatherableConfig);
            return false; // async work started
        }

        private void ExecuteDepositStep(Runner runner, TaskSequence seq)
        {
            float transitDist = NodeGeometryProvider?.GetDepositPointDistance(
                runner.Id, runner.CurrentNodeId) ?? 0f;

            // Start the deposit timer — actual deposit happens when it completes
            runner.ActiveWarning = null;
            runner.State = RunnerState.Depositing;
            runner.Depositing = new DepositingState
            {
                TicksRemaining = Config.DepositDurationTicks,
                TransitDistance = transitDist,
                TransitDistanceCovered = 0f,
            };
        }

        // ─── Micro Layer: Within-Task Behavior ────────────────────

        private const int MicroResultFinishTask = -1;
        private const int MicroResultNoMatch = -2;
        private const int MicroResultNoEligibleGatherable = -3;
        private const int MicroResultFightHere = -4;
        private const int MicroResultWait = -5;
        private const int MicroResultCraftHere = -6;

        /// <summary>
        /// Evaluate the micro ruleset for the current Work step to decide which resource to gather.
        /// Reads the micro ruleset from the Work step's MicroRulesetId.
        /// Returns:
        ///   >= 0: gatherable index to gather
        ///   -1 (MicroResultFinishTask): FinishTask — advance macro step
        ///   -2 (MicroResultNoMatch): no valid rule matched — runner is stuck, stay idle
        ///   -3 (MicroResultNoEligibleGatherable): rule matched but no gatherable within skill level
        ///
        /// Null or empty micro ruleset = no valid rule matched = runner is stuck (let it break).
        /// </summary>
        private int EvaluateMicroRules(Runner runner, World.WorldNode node,
            bool itemJustProduced = false, bool interruptOnly = false)
        {
            Ruleset microRuleset = null;
            bool isOverride = false;
            var seq = GetRunnerTaskSequence(runner);
            if (seq != null)
            {
                var step = GetCurrentStep(runner, seq);
                if (step != null)
                {
                    // Check runner-level override first, fall back to step's configured micro
                    string overrideId = GetRunnerMicroOverrideForStep(runner, runner.TaskSequenceCurrentStepIndex);
                    string microId = overrideId ?? step.MicroRulesetId;
                    isOverride = overrideId != null;
                    if (microId != null)
                        microRuleset = FindMicroRulesetInLibrary(microId);
                }
            }

            if (microRuleset == null)
                return MicroResultNoMatch; // let it break

            string logSource = isOverride ? "MicroEval Override" : "MicroEval";
            if (interruptOnly) logSource += " Interrupt";
            var ctx = new EvaluationContext(runner, CurrentGameState, Config);
            int ruleIndex = RuleEvaluator.EvaluateRuleset(microRuleset, ctx, interruptOnly);

            if (ruleIndex >= 0)
            {
                var rule = microRuleset.Rules[ruleIndex];
                var action = rule.Action;

                if (action.Type == ActionType.FinishTask)
                {
                    LogDecision(runner, ruleIndex, rule, logSource,
                        "Finish Task", false, DecisionLayer.Micro,
                        wasInterrupted: interruptOnly && rule.CanInterrupt);
                    return MicroResultFinishTask;
                }

                if (action.Type == ActionType.GatherHere)
                {
                    int index = ResolveGatherHereIndex(action, node, runner, itemJustProduced);
                    if (index >= 0 && index < node.Gatherables.Length)
                    {
                        string itemId = node.Gatherables[index].ProducedItemId;
                        string itemName = ItemRegistry?.Get(itemId)?.Name ?? itemId;
                        string actionLabel = $"Gather {itemName}";
                        LogDecision(runner, ruleIndex, rule, logSource,
                            actionLabel, false, DecisionLayer.Micro);
                        return index;
                    }

                    // GatherAny found no eligible gatherables at this node (all above MinLevel)
                    if (index == MicroResultNoEligibleGatherable)
                        return MicroResultNoEligibleGatherable;
                }

                if (action.Type == ActionType.GatherBestAvailable)
                {
                    int index = ResolveGatherBestAvailableIndex(action, node, runner, itemJustProduced);
                    if (index >= 0 && index < node.Gatherables.Length)
                    {
                        string itemId = node.Gatherables[index].ProducedItemId;
                        string itemName = ItemRegistry?.Get(itemId)?.Name ?? itemId;
                        string actionLabel = $"Gather Best Available -> {itemName}";
                        LogDecision(runner, ruleIndex, rule, logSource,
                            actionLabel, false, DecisionLayer.Micro);
                        return index;
                    }

                    if (index == MicroResultNoEligibleGatherable)
                        return MicroResultNoEligibleGatherable;
                }

                if (action.Type == ActionType.FightHere)
                {
                    LogDecision(runner, ruleIndex, rule, logSource,
                        "Fight Here", false, DecisionLayer.Micro,
                        wasInterrupted: interruptOnly && rule.CanInterrupt);
                    return MicroResultFightHere;
                }

                if (action.Type == ActionType.Wait)
                {
                    LogDecision(runner, ruleIndex, rule, logSource,
                        "Wait", false, DecisionLayer.Micro);
                    return MicroResultWait;
                }

                if (action.Type == ActionType.CraftHere)
                {
                    _lastCraftRecipeId = action.StringParam;
                    string recipeName = action.StringParam ?? "Unknown";
                    var craftRecipe = CraftingRecipeRegistry.Get(action.StringParam);
                    if (craftRecipe != null) recipeName = craftRecipe.Name;
                    LogDecision(runner, ruleIndex, rule, logSource,
                        $"Craft {recipeName}", false, DecisionLayer.Micro);
                    return MicroResultCraftHere;
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

                // Fresh pick: Work step entry (Gathering==null) or item just produced.
                // "Any" means "any I'm capable of" — filter by MinLevel.
                var eligible = new System.Collections.Generic.List<int>();
                for (int i = 0; i < node.Gatherables.Length; i++)
                {
                    var g = node.Gatherables[i];
                    if (g.MinLevel <= 0 || runner.GetSkill(g.RequiredSkill).Level >= g.MinLevel)
                        eligible.Add(i);
                }

                if (eligible.Count == 0) return MicroResultNoEligibleGatherable;
                return eligible[_random.Next(eligible.Count)];
            }

            // Positional index (default behavior)
            return action.IntParam;
        }

        /// <summary>
        /// Resolve GatherBestAvailable action to a gatherable index at the given node.
        /// Filters by RequiredSkill == chosen skill AND MinLevel &lt;= runner's level,
        /// then picks the one with the highest MinLevel (deterministic; ties broken by lowest index).
        /// Mid-gather stability: keeps current resource until an item is produced (same as GatherAny).
        /// </summary>
        private int ResolveGatherBestAvailableIndex(AutomationAction action, World.WorldNode node,
            Runner runner, bool itemJustProduced)
        {
            if (node.Gatherables.Length == 0) return MicroResultNoEligibleGatherable;

            var skillType = (SkillType)action.IntParam;
            int runnerLevel = runner.GetSkill(skillType).Level;

            // Mid-gather stability: keep current resource if still valid for this skill+level
            if (runner.Gathering != null && !itemJustProduced
                && runner.Gathering.GatherableIndex >= 0
                && runner.Gathering.GatherableIndex < node.Gatherables.Length)
            {
                var current = node.Gatherables[runner.Gathering.GatherableIndex];
                if (current.RequiredSkill == skillType && runnerLevel >= current.MinLevel)
                    return runner.Gathering.GatherableIndex;
            }

            // Fresh pick: find highest-MinLevel gatherable the runner qualifies for
            int bestIndex = -1;
            int bestMinLevel = -1;

            for (int i = 0; i < node.Gatherables.Length; i++)
            {
                var g = node.Gatherables[i];
                if (g.RequiredSkill != skillType) continue;
                if (runnerLevel < g.MinLevel) continue;

                // Pick highest MinLevel; ties broken by lowest index (first wins)
                if (g.MinLevel > bestMinLevel)
                {
                    bestMinLevel = g.MinLevel;
                    bestIndex = i;
                }
            }

            return bestIndex >= 0 ? bestIndex : MicroResultNoEligibleGatherable;
        }

        /// <summary>
        /// Returns true if at least one gatherable at this node is within the runner's skill level.
        /// </summary>
        private static bool HasAnyEligibleGatherable(Runner runner, World.WorldNode node)
        {
            for (int i = 0; i < node.Gatherables.Length; i++)
            {
                var g = node.Gatherables[i];
                if (g.MinLevel <= 0 || runner.GetSkill(g.RequiredSkill).Level >= g.MinLevel)
                    return true;
            }
            return false;
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
                    ExecuteCurrentStep(runner);
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

            // GatherAny found no eligible gatherables mid-gather — shouldn't normally happen
            // (runner was already gathering something eligible), but handle it cleanly.
            if (newIndex == MicroResultNoEligibleGatherable)
            {
                runner.State = RunnerState.Idle;
                runner.Gathering = null;
                runner.ActiveWarning = RunnerWarnings.NoEligibleGatherables;
                return;
            }

            // Different resource — check MinLevel before switching
            if (newIndex != runner.Gathering.GatherableIndex)
            {
                var gatherableConfig = node.Gatherables[newIndex];

                // MinLevel gate: same check as ExecuteWorkStep. If the runner can't
                // gather this resource, stop and go stuck rather than silently mining it.
                if (gatherableConfig.MinLevel > 0)
                {
                    var skill = runner.GetSkill(gatherableConfig.RequiredSkill);
                    if (skill.Level < gatherableConfig.MinLevel)
                    {
                        runner.State = RunnerState.Idle;
                        runner.Gathering = null;
                        runner.ActiveWarning = RunnerWarnings.SkillTooLow(gatherableConfig.RequiredSkill, skill.Level, gatherableConfig.MinLevel);
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

                int newSpotIndex = CountRunnersAtGatherable(runner.Id, runner.CurrentNodeId, newIndex);
                runner.Gathering.GatherableIndex = newIndex;
                runner.Gathering.SpotIndex = newSpotIndex;
                runner.Gathering.TickAccumulator = 0f;
                runner.Gathering.TicksRequired = CalculateTicksRequired(runner, gatherableConfig);

                // Transit to new gathering spot
                float transitDist = NodeGeometryProvider?.GetGatheringSpotDistance(
                    runner.Id, runner.CurrentNodeId, newIndex, newSpotIndex) ?? 0f;
                runner.Gathering.TransitDistance = transitDist;
                runner.Gathering.TransitDistanceCovered = 0f;
            }
            // Same resource — keep going, accumulator rolls over naturally
        }

        /// <summary>
        /// Mid-action interrupt check: evaluate only CanInterrupt rules during the action
        /// commitment window. Non-interrupt rules are skipped. Only fires FinishTask
        /// (the primary interrupt use case: InventoryFull mid-gather).
        /// </summary>
        private void ReevaluateInterruptRulesDuringGathering(Runner runner, World.WorldNode node)
        {
            if (GetRunnerTaskSequence(runner) == null) return;

            int result = EvaluateMicroRules(runner, node, itemJustProduced: false, interruptOnly: true);

            if (result == MicroResultFinishTask)
            {
                runner.State = RunnerState.Idle;
                runner.Gathering = null;

                var seq = GetRunnerTaskSequence(runner);
                if (seq != null && AdvanceRunnerStepIndex(runner, seq))
                {
                    PublishStepAdvanced(runner, seq);
                    ExecuteCurrentStep(runner);
                }
                else
                {
                    HandleSequenceCompleted(runner);
                }
            }
            // All other results (no match, same resource, different resource) are ignored
            // during interrupt-only evaluation. The runner continues the current action.
        }

        private void TickDepositing(Runner runner)
        {
            if (runner.Depositing == null) return;

            // Transit gate: runner walks to deposit point before countdown starts
            if (runner.Depositing.IsInTransit)
            {
                float inNodeSpeed = GetInNodeTravelSpeed(runner);
                runner.Depositing.TransitDistanceCovered += inNodeSpeed * TickDeltaTime;
                if (runner.Depositing.IsInTransit) return; // still walking
                // Fall through to first deposit tick
            }

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

            // ExecuteCurrentStep handles pending application at step 0
            // and macro evaluation before each step.
            ExecuteCurrentStep(runner);
        }

        private string GetLastMatchedCraftRecipeId(Runner runner, WorldNode node)
        {
            return _lastCraftRecipeId;
        }

        // ─── Crafting ───────────────────────────────────────────────

        private void TickCrafting(Runner runner)
        {
            if (runner.CraftingProgress == null) return;

            runner.CraftingProgress.TicksRemaining--;
            if (runner.CraftingProgress.TicksRemaining > 0) return;

            // Crafting complete
            var recipe = CraftingRecipeRegistry.Get(runner.CraftingProgress.RecipeId);
            if (recipe != null)
            {
                // Award XP
                var skill = runner.GetSkill(recipe.RequiredSkill);
                bool leveledUp = skill.AddXp(recipe.XpReward, Config);
                if (leveledUp)
                {
                    Events.Publish(new RunnerSkillLeveledUp
                    {
                        RunnerId = runner.Id,
                        Skill = recipe.RequiredSkill,
                        NewLevel = skill.Level,
                    });
                }

                // Produce item
                if (recipe.EquipmentSlot.HasValue)
                {
                    // Equipment goes straight to bank as a named item
                    CurrentGameState.Bank.Deposit(recipe.ProducedItemId, 1);
                }
                else
                {
                    // Regular item (potion etc)
                    CurrentGameState.Bank.Deposit(recipe.ProducedItemId, 1);
                }

                Events.Publish(new CraftingCompleted
                {
                    RunnerId = runner.Id,
                    RecipeId = recipe.Id,
                    ProducedItemId = recipe.ProducedItemId,
                    NodeId = runner.CraftingProgress.NodeId,
                });
            }

            runner.State = RunnerState.Idle;
            runner.CraftingProgress = null;

            // Advance step
            var seq = GetRunnerTaskSequence(runner);
            if (seq != null)
            {
                if (AdvanceRunnerStepIndex(runner, seq))
                {
                    PublishStepAdvanced(runner, seq);
                }
                else
                {
                    HandleSequenceCompleted(runner);
                    return;
                }
            }

            ExecuteCurrentStep(runner);
        }

        private void RegisterCraftingItems()
        {
            CraftingRecipeRegistry.Initialize();

            // Register items that are produced by crafting (if not already in registry)
            void Reg(string id, string name, ItemCategory cat, bool stack = false, int maxStack = 1)
            {
                if (!ItemRegistry.Has(id))
                    ItemRegistry.Register(new ItemDefinition(id, name, cat, stack, maxStack));
            }

            Reg("copper_sword", "Copper Sword", ItemCategory.Gear);
            Reg("copper_shield", "Copper Shield", ItemCategory.Gear);
            Reg("copper_helmet", "Copper Helmet", ItemCategory.Gear);
            Reg("copper_body", "Copper Body Armour", ItemCategory.Gear);
            Reg("wooden_staff", "Wooden Staff", ItemCategory.Gear);
            Reg("wooden_wand", "Wooden Wand", ItemCategory.Gear);
            Reg("health_potion", "Health Potion", ItemCategory.Consumable, true, 10);
            Reg("mana_potion", "Mana Potion", ItemCategory.Consumable, true, 10);
        }

        /// <summary>
        /// Start crafting a recipe. Called when micro rules produce CraftHere.
        /// Consumes materials from bank, starts crafting timer.
        /// </summary>
        public bool StartCrafting(Runner runner, string recipeId)
        {
            CraftingRecipeRegistry.Initialize();
            var recipe = CraftingRecipeRegistry.Get(recipeId);
            if (recipe == null) return false;

            // Check materials in bank
            foreach (var ing in recipe.Ingredients)
            {
                if (CurrentGameState.Bank.CountItem(ing.ItemId) < ing.Quantity)
                    return false;
            }

            // Consume materials
            foreach (var ing in recipe.Ingredients)
            {
                for (int i = 0; i < ing.Quantity; i++)
                    CurrentGameState.Bank.RemoveItem(ing.ItemId, 1);
            }

            runner.State = RunnerState.Crafting;
            runner.CraftingProgress = new CraftingState
            {
                NodeId = runner.CurrentNodeId,
                RecipeId = recipeId,
                TicksRemaining = recipe.CraftTicks,
                TicksTotal = recipe.CraftTicks,
            };

            Events.Publish(new CraftingStarted
            {
                RunnerId = runner.Id,
                RecipeId = recipeId,
                NodeId = runner.CurrentNodeId,
            });

            return true;
        }

        // ─── Combat ────────────────────────────────────────────────

        private int _nextEnemyInstanceId;

        private AbilityConfig FindAbilityDefinition(string id)
        {
            for (int i = 0; i < Config.AbilityDefinitions.Length; i++)
            {
                if (Config.AbilityDefinitions[i].Id == id)
                    return Config.AbilityDefinitions[i];
            }
            return null;
        }

        private EnemyConfig FindEnemyDefinition(string id)
        {
            for (int i = 0; i < Config.EnemyDefinitions.Length; i++)
            {
                if (Config.EnemyDefinitions[i].Id == id)
                    return Config.EnemyDefinitions[i];
            }
            return null;
        }

        private void StartFighting(Runner runner, WorldNode node)
        {
            if (node == null || node.EnemySpawns == null || node.EnemySpawns.Length == 0)
            {
                runner.ActiveWarning = RunnerWarnings.NoEnemiesAtNode;
                return;
            }

            // Initialize HP/mana if uninitialized (-1 means first combat entry)
            if (runner.CurrentHitpoints < 0f)
            {
                float hpLevel = runner.GetEffectiveLevel(SkillType.Hitpoints, Config);
                runner.CurrentHitpoints = CombatFormulas.CalculateMaxHitpoints(hpLevel, Config);
            }
            if (runner.CurrentMana < 0f)
            {
                float restoLevel = runner.GetEffectiveLevel(SkillType.Restoration, Config);
                runner.CurrentMana = CombatFormulas.CalculateMaxMana(restoLevel, Config);
            }

            // Get or create encounter for this node
            if (!CurrentGameState.EncounterStates.TryGetValue(node.Id, out var encounter))
            {
                encounter = new EncounterState(node.Id);
                CurrentGameState.EncounterStates[node.Id] = encounter;

                // Spawn initial enemies
                for (int s = 0; s < node.EnemySpawns.Length; s++)
                {
                    var spawnEntry = node.EnemySpawns[s];
                    var enemyDef = FindEnemyDefinition(spawnEntry.EnemyConfigId);
                    if (enemyDef == null) continue;

                    int spawnCount = Math.Min(spawnEntry.InitialCount, spawnEntry.MaxCount);
                    for (int i = 0; i < spawnCount; i++)
                    {
                        var instance = new EnemyInstance
                        {
                            InstanceId = $"enemy-{_nextEnemyInstanceId++}",
                            ConfigId = spawnEntry.EnemyConfigId,
                            CurrentHp = enemyDef.MaxHitpoints,
                            SpawnEntryIndex = s,
                        };
                        encounter.Enemies.Add(instance);

                        Events.Publish(new EnemySpawned
                        {
                            EnemyInstanceId = instance.InstanceId,
                            EnemyConfigId = instance.ConfigId,
                            NodeId = node.Id,
                        });
                    }
                }
            }

            encounter.IsActive = true;
            runner.ActiveWarning = null;
            runner.State = RunnerState.Fighting;
            runner.Fighting = new FightingState { NodeId = node.Id };

            Events.Publish(new CombatStarted
            {
                RunnerId = runner.Id,
                NodeId = node.Id,
            });
        }

        private void TickFighting(Runner runner)
        {
            if (runner.Fighting == null) return;

            // Death check: enemy damage from TickEncounters may have killed this runner
            if (runner.CurrentHitpoints <= 0f)
            {
                HandleRunnerDeath(runner);
                return;
            }

            var node = CurrentGameState.Map.GetNode(runner.Fighting.NodeId);
            CurrentGameState.EncounterStates.TryGetValue(runner.Fighting.NodeId, out var encounter);

            // Decrement ability cooldowns
            if (runner.Fighting.CooldownTrackers.Count > 0)
            {
                var keys = new List<string>(runner.Fighting.CooldownTrackers.Keys);
                foreach (var key in keys)
                {
                    int cd = runner.Fighting.CooldownTrackers[key] - 1;
                    if (cd <= 0)
                        runner.Fighting.CooldownTrackers.Remove(key);
                    else
                        runner.Fighting.CooldownTrackers[key] = cd;
                }
            }

            // Disengaging: count down, then exit combat
            if (runner.Fighting.IsDisengaging)
            {
                runner.Fighting.DisengageTicksRemaining--;
                if (runner.Fighting.DisengageTicksRemaining <= 0)
                {
                    string fightingNodeId = runner.Fighting.NodeId;
                    runner.State = RunnerState.Idle;
                    runner.Fighting = null;
                    CleanupEncounterIfEmpty(fightingNodeId);

                    var seq = GetRunnerTaskSequence(runner);
                    if (seq != null && AdvanceRunnerStepIndex(runner, seq))
                    {
                        PublishStepAdvanced(runner, seq);
                        ExecuteCurrentStep(runner);
                    }
                    else
                    {
                        HandleSequenceCompleted(runner);
                    }
                }
                return;
            }

            // Mid-action: committed to an ability
            if (runner.Fighting.IsActing)
            {
                // Interrupt-only micro check (e.g. FinishTask with CanInterrupt)
                if (node != null)
                    ReevaluateInterruptRulesDuringFighting(runner, node);

                // Check if interrupt started disengage
                if (runner.Fighting == null || runner.Fighting.IsDisengaging) return;

                // Interrupt-only combat style ability check
                if (encounter != null)
                    ReevaluateInterruptAbilityDuringFighting(runner, encounter);
                if (runner.Fighting == null) return;

                runner.Fighting.ActionTicksRemaining--;
                if (runner.Fighting.ActionTicksRemaining <= 0)
                {
                    // Action completed: resolve effects
                    var ability = FindAbilityDefinition(runner.Fighting.CurrentAbilityId);
                    if (ability != null && encounter != null)
                    {
                        var targetEnemy = encounter.FindEnemy(runner.Fighting.CurrentTargetEnemyId);

                        // If the target died or vanished mid-cast, the action fizzles (no damage, no XP, no log)
                        bool isHealAbility = ability.Effects.Count > 0
                            && (ability.Effects[0].Type == EffectType.Heal
                                || ability.Effects[0].Type == EffectType.HealSelf
                                || ability.Effects[0].Type == EffectType.HealAoe);
                        bool targetGone = runner.Fighting.CurrentTargetEnemyId != null
                            && (targetEnemy == null || !targetEnemy.IsAlive);
                        if (targetGone && !isHealAbility)
                        {
                            // Fizzle: clear action state, skip to next decision
                        }
                        else
                        {
                            ResolveAbilityEffects(runner, ability, targetEnemy, encounter);

                            // Award combat XP
                            var skill = runner.GetSkill(ability.SkillType);
                            float xp = CombatFormulas.CalculateCombatXp(ability.ActionTimeTicks, Config);
                            if (ability.SkillType == SkillType.Restoration)
                                xp *= Config.RestorationXpMultiplier;
                            bool leveledUp = skill.AddXp(xp, Config);
                            if (leveledUp)
                            {
                                Events.Publish(new RunnerSkillLeveledUp
                                {
                                    RunnerId = runner.Id,
                                    Skill = ability.SkillType,
                                    NewLevel = skill.Level,
                                });
                            }
                        }

                        // Set cooldown (even on fizzle: the ability was used)
                        if (ability.CooldownTicks > 0)
                            runner.Fighting.CooldownTrackers[ability.Id] = ability.CooldownTicks;
                    }

                    runner.Fighting.CurrentAbilityId = null;
                    runner.Fighting.CurrentTargetEnemyId = null;
                    runner.Fighting.CurrentTargetAllyId = null;
                    runner.Fighting.ActionTicksTotal = 0;

                    // Full micro re-eval at action completion
                    if (node != null)
                        ReevaluateMicroDuringFighting(runner, node);

                    // Check if re-eval caused state change
                    if (runner.State != RunnerState.Fighting || runner.Fighting == null) return;
                    if (runner.Fighting.IsDisengaging) return;
                }
                else
                {
                    return; // still acting
                }
            }

            // Free to act: select target and ability via combat style
            if (encounter == null) return;
            if (runner.Fighting.IsActing) return; // re-eval started new action

            var combatStyle = FindCombatStyle(runner);
            if (combatStyle == null)
            {
                runner.ActiveWarning = RunnerWarnings.NoCombatStyle;
                return; // idle in combat
            }

            var combatCtx = new CombatEvaluationContext(runner, encounter, CurrentGameState, Config);
            int targetRuleIdx;
            var target = CombatStyleEvaluator.EvaluateTargeting(combatStyle, combatCtx, out targetRuleIdx);
            Runner allyTarget = null;
            int allyTargetRuleIdx = -1;

            if (target == null)
            {
                // No enemy target: try ally targeting (for healers)
                allyTarget = CombatStyleEvaluator.EvaluateTargetingForAlly(combatStyle, combatCtx, out allyTargetRuleIdx);
                if (allyTarget == null) return; // no target at all
            }

            int abilityRuleIdx;
            var selectedAbility = CombatStyleEvaluator.EvaluateAbility(
                combatStyle, combatCtx, Config.AbilityDefinitions, out abilityRuleIdx);
            if (selectedAbility == null) return; // no available ability, idle

            // Ability/target mismatch guard: if we only have an ally target but selected a
            // damage ability, we need an enemy target. Try to find one via fallback.
            if (target == null && allyTarget != null && selectedAbility.Effects.Count > 0)
            {
                var primaryType = selectedAbility.Effects[0].Type;
                bool isDamageAbility = primaryType == EffectType.Damage || primaryType == EffectType.DamageAoe;
                if (isDamageAbility)
                {
                    // Try nearest enemy as fallback
                    var aliveEnemies = encounter.GetAliveEnemies();
                    if (aliveEnemies.Count > 0)
                    {
                        target = aliveEnemies[0];
                        allyTarget = null;
                    }
                    else
                    {
                        return; // no enemy to attack
                    }
                }
            }

            runner.ActiveWarning = null;

            // Start action commitment
            runner.Fighting.CurrentTargetEnemyId = target?.InstanceId;
            runner.Fighting.CurrentTargetAllyId = allyTarget?.Id;
            runner.Fighting.CurrentAbilityId = selectedAbility.Id;
            runner.Fighting.ActionTicksRemaining = selectedAbility.ActionTimeTicks;
            runner.Fighting.ActionTicksTotal = selectedAbility.ActionTimeTicks;

            // Consume mana
            if (selectedAbility.ManaCost > 0f)
            {
                runner.CurrentMana -= selectedAbility.ManaCost;
                runner.LastManaSpentTick = CurrentGameState.TickCount;
            }

            // Log combat decision
            int matchedTargetIdx = targetRuleIdx >= 0 ? targetRuleIdx : allyTargetRuleIdx;
            LogCombatDecision(runner, target, selectedAbility, combatStyle, matchedTargetIdx, abilityRuleIdx, allyTarget);
        }

        private void ResolveAbilityEffects(Runner runner, AbilityConfig ability,
            EnemyInstance targetEnemy, EncounterState encounter)
        {
            float attackerLevel = runner.GetEffectiveLevel(ability.SkillType, Config);
            float totalDamageDealt = 0f;
            float totalHealingDone = 0f;
            bool wasKill = false;
            string healTargetRunnerId = null;
            var deferredKills = new List<(EnemyInstance enemy, EncounterState enc)>();

            foreach (var effect in ability.Effects)
            {
                // Check condition
                float targetHp = targetEnemy?.CurrentHp ?? 0f;
                float targetMaxHp = 0f;
                if (targetEnemy != null)
                {
                    var targetDef = FindEnemyDefinition(targetEnemy.ConfigId);
                    targetMaxHp = targetDef?.MaxHitpoints ?? 100f;
                }

                switch (effect.Type)
                {
                    case EffectType.Damage:
                    {
                        if (targetEnemy == null) break;
                        float dmg = CombatFormulas.CalculateDamage(effect, attackerLevel,
                            FindEnemyDefinition(targetEnemy.ConfigId)?.BaseDefence ?? 0f, Config);
                        if (CombatFormulas.IsEffectConditionMet(effect.Condition,
                            targetHp, targetMaxHp, wasKill))
                        {
                            totalDamageDealt += dmg;
                            if (targetEnemy.IsAlive)
                            {
                                targetEnemy.CurrentHp -= dmg;
                                if (!targetEnemy.IsAlive)
                                {
                                    wasKill = true;
                                    deferredKills.Add((targetEnemy, encounter));
                                }
                            }
                        }
                        break;
                    }

                    case EffectType.DamageAoe:
                    {
                        var aliveEnemies = encounter.GetAliveEnemies();
                        int maxTargets = effect.MaxTargets < 0 ? aliveEnemies.Count
                            : Math.Min(effect.MaxTargets, aliveEnemies.Count);
                        float perHitDmg = 0f;
                        for (int i = 0; i < maxTargets; i++)
                        {
                            var enemy = aliveEnemies[i];
                            var enemyDef = FindEnemyDefinition(enemy.ConfigId);
                            float dmg = CombatFormulas.CalculateDamage(effect, attackerLevel,
                                enemyDef?.BaseDefence ?? 0f, Config);
                            enemy.CurrentHp -= dmg;
                            perHitDmg = dmg; // all enemies get same base damage
                            if (!enemy.IsAlive)
                            {
                                wasKill = true;
                                deferredKills.Add((enemy, encounter));
                            }
                        }
                        // Report per-hit damage, not accumulated total
                        totalDamageDealt += perHitDmg;
                        break;
                    }

                    case EffectType.Taunt:
                    {
                        if (targetEnemy != null && targetEnemy.IsAlive)
                            targetEnemy.TauntedByRunnerId = runner.Id;
                        break;
                    }

                    case EffectType.TauntAoe:
                    {
                        var aliveEnemies = encounter.GetAliveEnemies();
                        int maxTargets = effect.MaxTargets < 0 ? aliveEnemies.Count
                            : Math.Min(effect.MaxTargets, aliveEnemies.Count);
                        for (int i = 0; i < maxTargets; i++)
                            aliveEnemies[i].TauntedByRunnerId = runner.Id;
                        break;
                    }

                    case EffectType.TauntAll:
                    {
                        foreach (var enemy in encounter.GetAliveEnemies())
                            enemy.TauntedByRunnerId = runner.Id;
                        break;
                    }

                    case EffectType.Heal:
                    {
                        // Heal lowest-HP ally at node
                        var healAmount = CombatFormulas.CalculateHeal(effect, attackerLevel, Config);
                        Runner lowestHpAlly = null;
                        float lowestHp = float.MaxValue;
                        foreach (var r in CurrentGameState.Runners)
                        {
                            if (r.State != RunnerState.Fighting || r.Fighting?.NodeId != runner.Fighting.NodeId)
                                continue;
                            if (r.CurrentHitpoints < lowestHp)
                            {
                                lowestHp = r.CurrentHitpoints;
                                lowestHpAlly = r;
                            }
                        }
                        if (lowestHpAlly != null)
                        {
                            float maxHp = CombatFormulas.CalculateMaxHitpoints(
                                lowestHpAlly.GetEffectiveLevel(SkillType.Hitpoints, Config), Config);
                            lowestHpAlly.CurrentHitpoints = Math.Min(
                                lowestHpAlly.CurrentHitpoints + healAmount, maxHp);
                            totalHealingDone += healAmount;
                            healTargetRunnerId = lowestHpAlly.Id;
                        }
                        break;
                    }

                    case EffectType.HealSelf:
                    {
                        if (!CombatFormulas.IsEffectConditionMet(effect.Condition,
                            targetHp, targetMaxHp, wasKill))
                            break;
                        var healAmount = CombatFormulas.CalculateHeal(effect, attackerLevel, Config);
                        float maxHp = CombatFormulas.CalculateMaxHitpoints(
                            runner.GetEffectiveLevel(SkillType.Hitpoints, Config), Config);
                        runner.CurrentHitpoints = Math.Min(
                            runner.CurrentHitpoints + healAmount, maxHp);
                        totalHealingDone += healAmount;
                        healTargetRunnerId = runner.Id;
                        break;
                    }

                    case EffectType.HealAoe:
                    {
                        var healAmount = CombatFormulas.CalculateHeal(effect, attackerLevel, Config);
                        var allies = new List<Runner>();
                        foreach (var r in CurrentGameState.Runners)
                        {
                            if (r.State == RunnerState.Fighting && r.Fighting?.NodeId == runner.Fighting.NodeId)
                                allies.Add(r);
                        }
                        int maxTargets = effect.MaxTargets < 0 ? allies.Count
                            : Math.Min(effect.MaxTargets, allies.Count);
                        for (int i = 0; i < maxTargets; i++)
                        {
                            float maxHp = CombatFormulas.CalculateMaxHitpoints(
                                allies[i].GetEffectiveLevel(SkillType.Hitpoints, Config), Config);
                            allies[i].CurrentHitpoints = Math.Min(
                                allies[i].CurrentHitpoints + healAmount, maxHp);
                            totalHealingDone += healAmount;
                        }
                        break;
                    }
                }
            }

            // Publish action completed
            bool primaryIsHeal = ability.Effects.Count > 0
                && (ability.Effects[0].Type == EffectType.Heal
                    || ability.Effects[0].Type == EffectType.HealSelf
                    || ability.Effects[0].Type == EffectType.HealAoe);
            Events.Publish(new CombatActionCompleted
            {
                RunnerId = runner.Id,
                AbilityId = ability.Id,
                TargetEnemyInstanceId = targetEnemy?.InstanceId,
                HealTargetRunnerId = healTargetRunnerId,
                PrimaryEffectType = ability.Effects.Count > 0 ? ability.Effects[0].Type : EffectType.Damage,
                Value = primaryIsHeal ? totalHealingDone : totalDamageDealt,
                SecondaryHealValue = !primaryIsHeal ? totalHealingDone : 0f,
                WasKill = wasKill,
            });

            // Process kills AFTER CombatActionCompleted so chronicle order is:
            // attacked (killed) -> defeated -> received loot
            foreach (var (killedEnemy, enc) in deferredKills)
                HandleEnemyKill(runner, killedEnemy, enc);
        }

        private void HandleEnemyKill(Runner runner, EnemyInstance enemy, EncounterState encounter)
        {
            var enemyDef = FindEnemyDefinition(enemy.ConfigId);
            if (enemyDef == null) return;

            // Roll loot
            foreach (var lootEntry in enemyDef.LootTable)
            {
                if (_random.NextDouble() > lootEntry.DropChance) continue;

                int quantity = lootEntry.MinQuantity == lootEntry.MaxQuantity
                    ? lootEntry.MinQuantity
                    : _random.Next(lootEntry.MinQuantity, lootEntry.MaxQuantity + 1);

                // Distribute to random fighting runner at this node
                var fightersAtNode = new List<Runner>();
                foreach (var r in CurrentGameState.Runners)
                {
                    if (r.State == RunnerState.Fighting && r.Fighting?.NodeId == encounter.NodeId)
                        fightersAtNode.Add(r);
                }

                var recipient = fightersAtNode.Count > 0
                    ? fightersAtNode[_random.Next(fightersAtNode.Count)]
                    : runner;

                var itemDef = ItemRegistry?.Get(lootEntry.ItemId);
                if (itemDef != null)
                {
                    for (int i = 0; i < quantity; i++)
                        recipient.Inventory.TryAdd(itemDef, 1);
                }

                Events.Publish(new LootDropped
                {
                    RunnerId = recipient.Id,
                    ItemId = lootEntry.ItemId,
                    Quantity = quantity,
                });
            }

            // Start respawn timer
            int spawnIdx = enemy.SpawnEntryIndex;
            var node = CurrentGameState.Map.GetNode(encounter.NodeId);
            if (node != null && spawnIdx >= 0 && spawnIdx < node.EnemySpawns.Length)
                enemy.RespawnTicksRemaining = node.EnemySpawns[spawnIdx].RespawnTimeTicks;

            Events.Publish(new EnemyDied
            {
                EnemyInstanceId = enemy.InstanceId,
                EnemyConfigId = enemy.ConfigId,
                NodeId = encounter.NodeId,
                KillerRunnerId = runner.Id,
            });
        }

        /// <summary>
        /// Find the combat style for a runner. Checks per-step override first,
        /// then runner's CombatStyleId. Returns null if none found.
        /// </summary>
        private CombatStyle FindCombatStyle(Runner runner)
        {
            // Check per-step override
            var seq = GetRunnerTaskSequence(runner);
            if (seq != null)
            {
                var step = GetCurrentStep(runner, seq);
                if (step != null && !string.IsNullOrEmpty(step.CombatStyleOverrideId))
                {
                    var overrideStyle = FindCombatStyleInLibrary(step.CombatStyleOverrideId);
                    if (overrideStyle != null) return overrideStyle;
                }
            }

            // Fall back to runner-level combat style
            if (string.IsNullOrEmpty(runner.CombatStyleId)) return null;
            return FindCombatStyleInLibrary(runner.CombatStyleId);
        }

        public CombatStyle FindCombatStyleInLibrary(string styleId)
        {
            if (string.IsNullOrEmpty(styleId)) return null;
            foreach (var style in CurrentGameState.CombatStyleLibrary)
            {
                if (style.Id == styleId) return style;
            }
            return null;
        }

        /// <summary>
        /// Tick a runner in the Waiting state. Re-evaluates micro rules each tick.
        /// When conditions change (e.g. enough allies arrived), the runner proceeds.
        /// </summary>
        private void TickWaiting(Runner runner)
        {
            if (runner.Waiting == null) return;

            var node = CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            if (node == null) return;

            int result = EvaluateMicroRules(runner, node);

            if (result == MicroResultWait)
                return; // still waiting

            // Conditions changed: clear waiting state and proceed
            runner.State = RunnerState.Idle;
            runner.Waiting = null;

            if (result == MicroResultFightHere)
            {
                StartFighting(runner, node);
                return;
            }

            if (result == MicroResultFinishTask)
            {
                var seq = GetRunnerTaskSequence(runner);
                if (seq != null && AdvanceRunnerStepIndex(runner, seq))
                {
                    PublishStepAdvanced(runner, seq);
                    ExecuteCurrentStep(runner);
                }
                else
                {
                    HandleSequenceCompleted(runner);
                }
                return;
            }

            // For gather results or no-match, re-execute from the current step
            ExecuteCurrentStep(runner);
        }

        private void LogCombatDecision(Runner runner, EnemyInstance target,
            AbilityConfig ability, CombatStyle style,
            int targetingRuleIndex, int abilityRuleIndex,
            Runner allyTarget = null)
        {
            string targetName;
            if (target != null)
            {
                var targetDef = FindEnemyDefinition(target.ConfigId);
                targetName = targetDef?.Name ?? target.ConfigId;
            }
            else if (allyTarget != null)
            {
                targetName = allyTarget.Name ?? allyTarget.Id;
            }
            else
            {
                targetName = "(no target)";
            }
            string detail = $"{ability.Name} -> {targetName}";

            // Build rich condition snapshot showing WHY this decision was made
            var snapshot = new System.Text.StringBuilder();

            // Targeting rule conditions
            if (targetingRuleIndex >= 0 && targetingRuleIndex < style.TargetingRules.Count)
            {
                var tRule = style.TargetingRules[targetingRuleIndex];
                string label = !string.IsNullOrEmpty(tRule.Label) ? tRule.Label : $"T#{targetingRuleIndex + 1}";
                snapshot.Append($"[{label}] ");
                if (tRule.Conditions.Count > 0)
                {
                    foreach (var cond in tRule.Conditions)
                    {
                        snapshot.Append(FormatConditionShort(cond));
                        snapshot.Append(", ");
                    }
                    snapshot.Length -= 2; // remove trailing ", "
                }
                else
                {
                    snapshot.Append("Always");
                }
                snapshot.Append($" => {tRule.Selection}");
            }

            // Ability rule conditions
            if (abilityRuleIndex >= 0 && abilityRuleIndex < style.AbilityRules.Count)
            {
                var aRule = style.AbilityRules[abilityRuleIndex];
                if (snapshot.Length > 0) snapshot.Append(" | ");
                string label = !string.IsNullOrEmpty(aRule.Label) ? aRule.Label : $"A#{abilityRuleIndex + 1}";
                snapshot.Append($"[{label}] ");
                if (aRule.Conditions.Count > 0)
                {
                    foreach (var cond in aRule.Conditions)
                    {
                        snapshot.Append(FormatConditionShort(cond));
                        snapshot.Append(", ");
                    }
                    snapshot.Length -= 2;
                }
                else
                {
                    snapshot.Append("Always");
                }
            }

            CurrentGameState.CombatDecisionLog.Add(new DecisionLogEntry
            {
                TickNumber = CurrentGameState.TickCount,
                GameTime = CurrentGameState.TotalTimeElapsed,
                RunnerId = runner.Id,
                RunnerName = runner.Name,
                NodeId = runner.CurrentNodeId,
                Layer = DecisionLayer.Combat,
                RuleIndex = abilityRuleIndex,
                RuleLabel = style.Name,
                TriggerReason = "CombatStyle",
                ActionType = ActionType.FightHere,
                ActionDetail = detail,
                WasDeferred = false,
                WasInterrupted = false,
                ConditionSnapshot = snapshot.ToString(),
            });
        }

        private static string FormatConditionShort(CombatCondition cond)
        {
            return cond.Type switch
            {
                CombatConditionType.Always => "Always",
                CombatConditionType.SelfHpPercent => $"HP{FormatOp(cond.Operator)}{(int)cond.NumericValue}%",
                CombatConditionType.SelfManaPercent => $"Mana{FormatOp(cond.Operator)}{(int)cond.NumericValue}%",
                CombatConditionType.TargetHpPercent => $"TargHP{FormatOp(cond.Operator)}{(int)cond.NumericValue}%",
                CombatConditionType.LowestAllyHpPercent => $"LowestAllyHP{FormatOp(cond.Operator)}{(int)cond.NumericValue}%",
                CombatConditionType.EnemyCountAtNode => $"Enemies{FormatOp(cond.Operator)}{(int)cond.NumericValue}",
                CombatConditionType.AllyCountAtNode => $"Allies{FormatOp(cond.Operator)}{(int)cond.NumericValue}",
                CombatConditionType.AlliesInCombatAtNode => $"AlliesInCombat{FormatOp(cond.Operator)}{(int)cond.NumericValue}",
                CombatConditionType.AbilityOffCooldown => $"{cond.StringParam} ready",
                CombatConditionType.EnemyIsCasting => "EnemyCasting",
                CombatConditionType.AnyUntauntedEnemy => "UntauntedExists",
                CombatConditionType.AlliesBelowHpPercent => $"{cond.StringParam ?? "1"}+ allies<{(int)cond.NumericValue}%HP",
                CombatConditionType.EnemyTargetingSelf => "TargetingMe",
                _ => cond.Type.ToString(),
            };
        }

        private static string FormatOp(ComparisonOperator op)
        {
            return op switch
            {
                ComparisonOperator.LessThan => "<",
                ComparisonOperator.LessOrEqual => "<=",
                ComparisonOperator.GreaterThan => ">",
                ComparisonOperator.GreaterOrEqual => ">=",
                ComparisonOperator.Equal => "=",
                _ => "?",
            };
        }

        private void HandleRunnerDeath(Runner runner)
        {
            string deathNodeId = runner.Fighting?.NodeId ?? runner.CurrentNodeId;
            runner.State = RunnerState.Dead;
            runner.Fighting = null;

            // Calculate respawn time based on distance to hub
            float travelTimeToHub = 0f;
            var map = CurrentGameState.Map;
            if (map != null && deathNodeId != map.HubNodeId)
            {
                float distance = map.GetEuclideanDistance(deathNodeId, map.HubNodeId);
                if (distance < 0f) distance = 0f;
                float speed = GetTravelSpeed(runner);
                if (speed > 0f) travelTimeToHub = distance / speed;
            }

            float respawnSeconds = CombatFormulas.CalculateRespawnTime(travelTimeToHub, Config);
            int respawnTicks = Math.Max(1, (int)(respawnSeconds / TickDeltaTime));

            runner.Death = new DeathState
            {
                RespawnTicksRemaining = respawnTicks,
                DeathNodeId = deathNodeId,
            };

            // Reset task sequence step index to 0 so runner restarts sequence on respawn
            runner.TaskSequenceCurrentStepIndex = 0;

            Events.Publish(new RunnerDied
            {
                RunnerId = runner.Id,
                NodeId = deathNodeId,
            });

            CleanupEncounterIfEmpty(deathNodeId);
        }

        /// <summary>Restore a runner to full HP and mana based on their current stats.</summary>
        private void RestoreRunnerToFull(Runner runner)
        {
            float hpLevel = runner.GetEffectiveLevel(SkillType.Hitpoints, Config);
            float restoLevel = runner.GetEffectiveLevel(SkillType.Restoration, Config);
            runner.CurrentHitpoints = CombatFormulas.CalculateMaxHitpoints(hpLevel, Config);
            runner.CurrentMana = CombatFormulas.CalculateMaxMana(restoLevel, Config);
        }

        private void TickDead(Runner runner)
        {
            if (runner.Death == null) return;

            runner.Death.RespawnTicksRemaining--;
            if (runner.Death.RespawnTicksRemaining > 0) return;

            // Respawn at hub with full HP/mana
            RestoreRunnerToFull(runner);

            runner.State = RunnerState.Idle;
            runner.CurrentNodeId = CurrentGameState.Map.HubNodeId;
            runner.Death = null;

            Events.Publish(new RunnerRespawned { RunnerId = runner.Id });

            // Pick up sequence from step 0 (reset in HandleRunnerDeath)
            ExecuteCurrentStep(runner);
        }

        private void TickEncounters()
        {
            if (CurrentGameState.EncounterStates.Count == 0) return;

            // Collect keys to avoid modifying dictionary during iteration
            var nodeIds = new List<string>(CurrentGameState.EncounterStates.Keys);

            foreach (var nodeId in nodeIds)
            {
                var encounter = CurrentGameState.EncounterStates[nodeId];
                if (!encounter.IsActive) continue;

                // Check if any runners are still fighting at this node
                bool hasActiveFighters = false;
                foreach (var r in CurrentGameState.Runners)
                {
                    if (r.State == RunnerState.Fighting && r.Fighting?.NodeId == nodeId)
                    {
                        hasActiveFighters = true;
                        break;
                    }
                }

                if (!hasActiveFighters)
                {
                    encounter.IsActive = false;
                    encounter.Enemies.Clear();
                    CurrentGameState.EncounterStates.Remove(nodeId);
                    Events.Publish(new EncounterEnded { NodeId = nodeId });
                    continue;
                }

                var node = CurrentGameState.Map.GetNode(nodeId);
                if (node == null) continue;

                // Process dead enemy respawn timers
                foreach (var enemy in encounter.Enemies)
                {
                    if (enemy.IsAlive) continue;
                    if (enemy.RespawnTicksRemaining <= 0) continue;

                    enemy.RespawnTicksRemaining--;
                    if (enemy.RespawnTicksRemaining <= 0)
                    {
                        var enemyDef = FindEnemyDefinition(enemy.ConfigId);
                        if (enemyDef != null)
                        {
                            enemy.CurrentHp = enemyDef.MaxHitpoints;
                            enemy.ActionTicksRemaining = 0;
                            enemy.TauntedByRunnerId = null;
                            enemy.CooldownTrackers.Clear();

                            Events.Publish(new EnemySpawned
                            {
                                EnemyInstanceId = enemy.InstanceId,
                                EnemyConfigId = enemy.ConfigId,
                                NodeId = nodeId,
                            });
                        }
                    }
                }

                // Enemy AI: each alive enemy attacks runners
                foreach (var enemy in encounter.Enemies)
                {
                    if (!enemy.IsAlive) continue;

                    var enemyDef = FindEnemyDefinition(enemy.ConfigId);
                    if (enemyDef == null) continue;

                    // Decrement cooldowns
                    if (enemy.CooldownTrackers.Count > 0)
                    {
                        var keys = new List<string>(enemy.CooldownTrackers.Keys);
                        foreach (var key in keys)
                        {
                            int cd = enemy.CooldownTrackers[key] - 1;
                            if (cd <= 0)
                                enemy.CooldownTrackers.Remove(key);
                            else
                                enemy.CooldownTrackers[key] = cd;
                        }
                    }

                    // Decrement action timer
                    if (enemy.IsActing)
                    {
                        enemy.ActionTicksRemaining--;
                        if (enemy.ActionTicksRemaining <= 0)
                        {
                            // Attack resolves: damage the target runner
                            var targetRunner = SelectRunnerTargetForEnemy(enemy, nodeId);
                            if (targetRunner != null)
                            {
                                float defenceLevel = targetRunner.GetEffectiveLevel(SkillType.Defence, Config);
                                float runnerDefence = CombatFormulas.CalculateRunnerDefence(defenceLevel, Config);
                                float damage = enemyDef.BaseDamage * (1f - runnerDefence / 100f);
                                targetRunner.CurrentHitpoints -= damage;

                                // Award defensive XP (proportional to pre-mitigation damage)
                                AwardDefensiveXp(targetRunner, enemyDef.BaseDamage);

                                Events.Publish(new RunnerTookDamage
                                {
                                    RunnerId = targetRunner.Id,
                                    EnemyInstanceId = enemy.InstanceId,
                                    Damage = damage,
                                    RemainingHp = targetRunner.CurrentHitpoints,
                                });
                            }
                        }
                        continue;
                    }

                    // Free to act: start basic attack
                    var target = SelectRunnerTargetForEnemy(enemy, nodeId);
                    if (target != null)
                    {
                        enemy.ActionTicksRemaining = enemyDef.AttackSpeedTicks;
                    }
                }
            }
        }

        /// <summary>
        /// Select a runner for an enemy to target. Taunted runners take priority,
        /// then first fighting runner at the node.
        /// </summary>
        private Runner SelectRunnerTargetForEnemy(EnemyInstance enemy, string nodeId)
        {
            // Check taunt override
            if (enemy.TauntedByRunnerId != null)
            {
                var taunted = FindRunner(enemy.TauntedByRunnerId);
                if (taunted != null && taunted.State == RunnerState.Fighting
                    && taunted.Fighting?.NodeId == nodeId)
                    return taunted;
                enemy.TauntedByRunnerId = null; // taunt expired (runner left/died)
            }

            // Default: first fighting runner at this node
            foreach (var r in CurrentGameState.Runners)
            {
                if (r.State == RunnerState.Fighting && r.Fighting?.NodeId == nodeId)
                    return r;
            }
            return null;
        }

        private void AwardDefensiveXp(Runner runner, float preMitigationDamage)
        {
            // Hitpoints XP: proportional to pre-mitigation damage
            float hpXp = preMitigationDamage * Config.HitpointsXpPerDamage;
            var hpSkill = runner.GetSkill(SkillType.Hitpoints);
            bool hpLeveledUp = hpSkill.AddXp(hpXp, Config);
            if (hpLeveledUp)
            {
                Events.Publish(new RunnerSkillLeveledUp
                {
                    RunnerId = runner.Id,
                    Skill = SkillType.Hitpoints,
                    NewLevel = hpSkill.Level,
                });
            }

            // Defence XP: proportional to pre-mitigation damage
            float defXp = preMitigationDamage * Config.DefenceXpPerDamage;
            var defSkill = runner.GetSkill(SkillType.Defence);
            bool defLeveledUp = defSkill.AddXp(defXp, Config);
            if (defLeveledUp)
            {
                Events.Publish(new RunnerSkillLeveledUp
                {
                    RunnerId = runner.Id,
                    Skill = SkillType.Defence,
                    NewLevel = defSkill.Level,
                });
            }
        }

        private void CleanupEncounterIfEmpty(string nodeId)
        {
            if (nodeId == null) return;

            bool hasActiveFighters = false;
            foreach (var r in CurrentGameState.Runners)
            {
                if (r.State == RunnerState.Fighting && r.Fighting?.NodeId == nodeId)
                {
                    hasActiveFighters = true;
                    break;
                }
            }

            if (!hasActiveFighters && CurrentGameState.EncounterStates.ContainsKey(nodeId))
            {
                CurrentGameState.EncounterStates[nodeId].IsActive = false;
                CurrentGameState.EncounterStates[nodeId].Enemies.Clear();
                CurrentGameState.EncounterStates.Remove(nodeId);
                Events.Publish(new EncounterEnded { NodeId = nodeId });
            }
        }

        /// <summary>
        /// Re-evaluate micro rules at ability completion during combat.
        /// If FightHere, continue fighting (pick new target/ability on next tick).
        /// If FinishTask, start disengage.
        /// </summary>
        private void ReevaluateMicroDuringFighting(Runner runner, WorldNode node)
        {
            if (GetRunnerTaskSequence(runner) == null) return;

            int result = EvaluateMicroRules(runner, node);

            if (result == MicroResultFinishTask)
            {
                // Start disengage: Athletics reduces time, floor at MinDisengageTimeTicks
                float athleticsLevel = runner.GetEffectiveLevel(SkillType.Athletics, Config);
                runner.Fighting.IsDisengaging = true;
                runner.Fighting.DisengageTicksRemaining = CombatFormulas.CalculateDisengageTicks(athleticsLevel, Config);
                runner.Fighting.CurrentAbilityId = null;
                runner.Fighting.CurrentTargetEnemyId = null;
                runner.Fighting.ActionTicksRemaining = 0;
                runner.Fighting.ActionTicksTotal = 0;
                return;
            }

            if (result == MicroResultFightHere)
            {
                // Continue fighting, will pick target/ability on next tick
                return;
            }

            // NoMatch or other: keep fighting (don't stop mid-combat on broken rules)
        }

        /// <summary>
        /// Interrupt-only check during combat action commitment.
        /// Only FinishTask with CanInterrupt triggers disengage.
        /// </summary>
        private void ReevaluateInterruptRulesDuringFighting(Runner runner, WorldNode node)
        {
            if (GetRunnerTaskSequence(runner) == null) return;

            int result = EvaluateMicroRules(runner, node, itemJustProduced: false, interruptOnly: true);

            if (result == MicroResultFinishTask)
            {
                float athleticsLevel = runner.GetEffectiveLevel(SkillType.Athletics, Config);
                runner.Fighting.IsDisengaging = true;
                runner.Fighting.DisengageTicksRemaining = CombatFormulas.CalculateDisengageTicks(athleticsLevel, Config);
                runner.Fighting.CurrentAbilityId = null;
                runner.Fighting.CurrentTargetEnemyId = null;
                runner.Fighting.ActionTicksRemaining = 0;
                runner.Fighting.ActionTicksTotal = 0;
            }
        }

        /// <summary>
        /// Interrupt-only check for combat style ability rules during action commitment.
        /// If an interrupt ability rule matches (CanInterrupt=true), cancel current action
        /// and start the new ability immediately.
        /// </summary>
        private void ReevaluateInterruptAbilityDuringFighting(Runner runner, EncounterState encounter)
        {
            var combatStyle = FindCombatStyle(runner);
            if (combatStyle == null) return;

            var combatCtx = new CombatEvaluationContext(runner, encounter, CurrentGameState, Config);
            int interruptRuleIdx;
            var interruptAbility = CombatStyleEvaluator.EvaluateAbility(
                combatStyle, combatCtx, Config.AbilityDefinitions, out interruptRuleIdx, interruptOnly: true);

            if (interruptAbility == null) return;
            if (interruptAbility.Id == runner.Fighting.CurrentAbilityId) return; // same ability

            // Cancel current action and start interrupt ability
            runner.Fighting.CurrentAbilityId = interruptAbility.Id;
            runner.Fighting.ActionTicksRemaining = interruptAbility.ActionTimeTicks;
            runner.Fighting.ActionTicksTotal = interruptAbility.ActionTimeTicks;

            // Consume mana
            if (interruptAbility.ManaCost > 0f)
            {
                runner.CurrentMana -= interruptAbility.ManaCost;
                runner.LastManaSpentTick = CurrentGameState.TickCount;
            }

            // Re-target if needed (interrupt might need a different target)
            int interruptTargetRuleIdx;
            var target = CombatStyleEvaluator.EvaluateTargeting(combatStyle, combatCtx, out interruptTargetRuleIdx);
            if (target != null)
                runner.Fighting.CurrentTargetEnemyId = target.InstanceId;

            LogCombatDecision(runner, encounter.FindEnemy(runner.Fighting.CurrentTargetEnemyId)
                ?? encounter.Enemies[0], interruptAbility, combatStyle, interruptTargetRuleIdx, interruptRuleIdx);
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
            runner.LastCompletedTaskSequenceId = completedSeq?.Id;
            runner.TaskSequenceId = null;

            runner.State = RunnerState.Idle;

            Events.Publish(new TaskSequenceCompleted
            {
                RunnerId = runner.Id,
                SequenceName = seqName,
            });

            // Macro rules get a chance to assign a new sequence.
            // If a rule fires, AssignRunner calls ExecuteCurrentStep internally.
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
            var newSeq = ResolveTaskSequenceFromMacroAction(rule.Action);

            var currentSeq = GetRunnerTaskSequence(runner);

            // If the new sequence is the same as current, skip (avoid infinite reassignment).
            // Both null = already idle, rule wants idle → suppress.
            if (newSeq == null && currentSeq == null)
                return false;

            // Same-sequence suppression: compare by ID (both are library sequences now)
            if (currentSeq != null && newSeq != null
                && currentSeq.Id == newSeq.Id)
                return false;

            // Also suppress if the sequence just completed with the same ID
            // (e.g., ReturnToHub completed → macro fires ReturnToHub again → suppress).
            if (currentSeq == null && newSeq != null
                && runner.LastCompletedTaskSequenceId == newSeq.Id)
                return false;

            // Deferred: store as pending, apply at sequence boundary (step 0).
            // But if there's no active sequence, degrade to Immediately —
            // there's nothing to "finish" so waiting makes no sense.
            bool actuallyDeferred = rule.FinishCurrentSequence && currentSeq != null;
            string actionDetail = newSeq != null ? $"Assign: {newSeq.Name ?? newSeq.Id}" : "Idle";

            if (actuallyDeferred)
            {
                // Only log + set on first fire — the rule evaluates every tick but
                // we only want one Decision Log entry, not a wall of duplicates.
                // TODO: Revisit this approach when addressing broader decision log spam
                // (macro + micro both fire every tick). May want a unified "log first
                // occurrence only" strategy. Deferred to Phase 5 (combat).
                if (runner.PendingTaskSequenceId != newSeq?.Id)
                {
                    LogDecision(runner, ruleIndex, rule, triggerReason, actionDetail, actuallyDeferred);
                    runner.PendingTaskSequenceId = newSeq?.Id;
                    runner.PendingSetAtGameTime = CurrentGameState.TotalTimeElapsed;
                    runner.PendingConditionSnapshot = FormatConditionSnapshot(rule, runner);
                    runner.PendingRuleLabel = rule.Label;
                }
                return false;
            }

            LogDecision(runner, ruleIndex, rule, triggerReason, actionDetail, actuallyDeferred);

            // Immediate: change task sequence now
            AssignRunner(runner.Id, newSeq, $"macro rule: {rule.Label}");
            return true;
        }

        /// <summary>
        /// Resolve a macro rule's action to a TaskSequence from the library.
        /// AssignSequence: look up by ID. Idle: return null.
        /// Returns null for Idle (clear task sequence) or if the referenced sequence is not found (let it break).
        /// </summary>
        private TaskSequence ResolveTaskSequenceFromMacroAction(AutomationAction action)
        {
            switch (action.Type)
            {
                case ActionType.AssignSequence:
                    if (string.IsNullOrEmpty(action.StringParam)) return null;
                    return FindTaskSequenceInLibrary(action.StringParam);

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

            string actionDetail = pending.Name ?? pending.Id;
            runner.PendingTaskSequenceId = null;

            // Log the deferred action execution in the Decision Log.
            // Replays the original rule context so the player sees:
            //   1. What condition caused it (from when the deferred was set)
            //   2. When the decision was made
            //   3. What the runner is now going to do
            // UI format: "[time] [MACRO] Name: ConditionSnapshot → ActionDetail"
            float elapsed = CurrentGameState.TotalTimeElapsed - runner.PendingSetAtGameTime;
            string conditionText = !string.IsNullOrEmpty(runner.PendingConditionSnapshot)
                ? runner.PendingConditionSnapshot
                : "(unknown condition)";
            string snapshot = elapsed >= 1f
                ? $"{conditionText} (deferred {elapsed:F0}s ago)"
                : conditionText;
            CurrentGameState.MacroDecisionLog.Add(new DecisionLogEntry
            {
                TickNumber = CurrentGameState.TickCount,
                GameTime = CurrentGameState.TotalTimeElapsed,
                RunnerId = runner.Id,
                RunnerName = runner.Name,
                NodeId = runner.CurrentNodeId,
                Layer = DecisionLayer.Macro,
                RuleIndex = -1,
                RuleLabel = runner.PendingRuleLabel ?? "Deferred rule",
                TriggerReason = "loop boundary",
                ActionType = ActionType.AssignSequence,
                ConditionSnapshot = snapshot,
                ActionDetail = actionDetail,
                WasDeferred = false,
            });

            Events.Publish(new AutomationPendingActionExecuted
            {
                RunnerId = runner.Id,
                ActionType = ActionType.AssignSequence,
                ActionDetail = actionDetail,
            });

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

            bool isEmpty = ruleset == null || ruleset.Rules == null || ruleset.Rules.Count == 0;
            runner.ActiveWarning = isEmpty
                ? RunnerWarnings.NoMicroRulesConfigured
                : RunnerWarnings.NoMicroRuleMatched(
                    CurrentGameState.Map.GetNode(runner.CurrentNodeId)?.Name ?? runner.CurrentNodeId,
                    ruleset.Rules.Count);

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
            FindTaskSequenceInLibrary(runner.TaskSequenceId);

        /// <summary>Get the runner's pending task sequence from the library.</summary>
        public TaskSequence GetRunnerPendingTaskSequence(Runner runner) =>
            FindTaskSequenceInLibrary(runner.PendingTaskSequenceId);

        /// <summary>Get the runner's macro ruleset from the library.</summary>
        public Ruleset GetRunnerMacroRuleset(Runner runner) =>
            FindMacroRulesetInLibrary(runner.MacroRulesetId);

        /// <summary>
        /// Get the runner's micro override for a specific step index, or null if no override.
        /// </summary>
        public string GetRunnerMicroOverrideForStep(Runner runner, int stepIndex)
        {
            if (runner.MicroOverrides == null) return null;
            for (int i = 0; i < runner.MicroOverrides.Count; i++)
            {
                if (runner.MicroOverrides[i].StepIndex == stepIndex)
                    return runner.MicroOverrides[i].MicroRulesetId;
            }
            return null;
        }

        /// <summary>Returns true if the runner has any micro overrides.</summary>
        public bool RunnerHasMicroOverrides(Runner runner) =>
            runner.MicroOverrides != null && runner.MicroOverrides.Count > 0;

        // ─── Runner Commands ──────────────────────────────────────────

        public void CommandRenameRunner(string runnerId, string newName)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;
            runner.Name = newName?.Trim() ?? "";
        }

        /// <summary>
        /// Spawn the tutorial reward runner. Biased toward gathering skills.
        /// Placed at hub idle.
        /// </summary>
        public Runner SpawnTutorialRewardRunner()
        {
            var bias = new RunnerFactory.BiasConstraints
            {
                PickOneSkillToBoostedAndPassionate = new[]
                {
                    SkillType.Mining, SkillType.Woodcutting, SkillType.Fishing, SkillType.Foraging
                },
                WeakenedSkills = new[]
                {
                    SkillType.Melee, SkillType.Magic, SkillType.Restoration, SkillType.Defence
                },
            };

            var runner = RunnerFactory.CreateBiased(_random, Config, bias, CurrentGameState.Map.HubNodeId);

            // Initialize HP/mana
            float maxHp = CombatFormulas.CalculateMaxHp(
                runner.GetEffectiveLevel(SkillType.Hitpoints, Config), Config);
            float maxMana = CombatFormulas.CalculateMaxMana(
                runner.GetEffectiveLevel(SkillType.Restoration, Config), Config);
            runner.CurrentHitpoints = maxHp;
            runner.CurrentMana = maxMana;

            CurrentGameState.Runners.Add(runner);

            Events.Publish(new RunnerCreated
            {
                RunnerId = runner.Id,
                RunnerName = runner.Name,
            });

            Events.Publish(new TutorialPawnAwarded
            {
                RunnerId = runner.Id,
                RunnerName = runner.Name,
            });

            return runner;
        }

        // ─── Equipment Commands ──────────────────────────────────────

        /// <summary>
        /// Equip an item from the bank onto a runner. Creates the EquipmentItem from recipe data.
        /// Returns displaced item to bank if any.
        /// </summary>
        public bool CommandEquipFromBank(string runnerId, string itemId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return false;

            if (CurrentGameState.Bank.CountItem(itemId) <= 0) return false;

            // Find recipe that produces this item to get equipment data
            CraftingRecipeRegistry.Initialize();
            CraftingRecipe recipe = null;
            foreach (var r in CraftingRecipeRegistry.All)
            {
                if (r.ProducedItemId == itemId && r.EquipmentSlot.HasValue)
                {
                    recipe = r;
                    break;
                }
            }

            if (recipe == null || !recipe.EquipmentSlot.HasValue) return false;

            // Remove from bank
            CurrentGameState.Bank.RemoveItem(itemId, 1);

            // Unequip existing item in that slot (return to bank)
            var existing = runner.Equipment.GetSlot(recipe.EquipmentSlot.Value);
            if (existing != null)
            {
                CurrentGameState.Bank.Deposit(existing.ItemId, 1);
                runner.Equipment.Unequip(recipe.EquipmentSlot.Value);
            }

            // Create and equip new item
            var equipItem = new EquipmentItem(itemId, recipe.Name, recipe.EquipmentSlot.Value);
            if (recipe.EquipmentStats != null)
            {
                foreach (var kvp in recipe.EquipmentStats)
                    equipItem.WithBonus(kvp.Key, kvp.Value);
            }
            runner.Equipment.Equip(equipItem);

            Events.Publish(new ItemEquipped
            {
                RunnerId = runner.Id,
                ItemId = itemId,
                Slot = recipe.EquipmentSlot.Value,
            });

            return true;
        }

        // ─── Library CRUD Commands ──────────────────────────────────

        /// <summary>Register a task sequence in the library. Returns its Id.</summary>
        public string CommandCreateTaskSequence(TaskSequence template)
        {
            if (string.IsNullOrEmpty(template.Id))
                template.Id = Guid.NewGuid().ToString();
            CurrentGameState.TaskSequenceLibrary.Add(template);
            // A new sequence could resolve previously-broken macro rule references
            RefreshMacroConfigWarnings();
            return template.Id;
        }

        /// <summary>
        /// Create a new blank task sequence with defaults. Single entry point for all "new sequence" flows.
        /// </summary>
        public string CommandCreateTaskSequence()
        {
            var seq = new TaskSequence
            {
                Name = GenerateNextName("Sequence", CurrentGameState.TaskSequenceLibrary, s => s.Name),
                AutoGenerateName = true,
                Loop = true,
                Steps = new List<TaskStep>(),
            };
            return CommandCreateTaskSequence(seq);
        }

        /// <summary>Register a macro ruleset in the library. Returns its Id.</summary>
        public string CommandCreateMacroRuleset(Ruleset template)
        {
            if (string.IsNullOrEmpty(template.Id))
                template.Id = Guid.NewGuid().ToString();
            CurrentGameState.MacroRulesetLibrary.Add(template);
            return template.Id;
        }

        /// <summary>
        /// Create a new blank macro ruleset with defaults. Single entry point for all "new macro" flows.
        /// </summary>
        public string CommandCreateMacroRuleset()
        {
            var ruleset = new Ruleset
            {
                Name = GenerateNextName("Macro Ruleset", CurrentGameState.MacroRulesetLibrary, r => r.Name),
                Category = RulesetCategory.General,
            };
            return CommandCreateMacroRuleset(ruleset);
        }

        /// <summary>Register a micro ruleset in the library. Returns its Id.</summary>
        public string CommandCreateMicroRuleset(Ruleset template)
        {
            if (string.IsNullOrEmpty(template.Id))
                template.Id = Guid.NewGuid().ToString();
            CurrentGameState.MicroRulesetLibrary.Add(template);
            return template.Id;
        }

        /// <summary>
        /// Create a new empty micro ruleset. Players populate it via templates or manual rule addition.
        /// </summary>
        public string CommandCreateMicroRuleset()
        {
            var ruleset = new Ruleset
            {
                Name = GenerateNextName("Micro Ruleset", CurrentGameState.MicroRulesetLibrary, r => r.Name),
                Category = RulesetCategory.Gathering,
            };
            return CommandCreateMicroRuleset(ruleset);
        }

        /// <summary>
        /// Generate an incrementing name like "Sequence 1", "Sequence 2", etc.
        /// Finds the next available number by checking existing names in the library.
        /// </summary>
        public static string GenerateNextName<T>(string baseName, List<T> library, Func<T, string> getName)
        {
            int next = 1;
            var existing = new HashSet<string>();
            foreach (var item in library)
                existing.Add(getName(item) ?? "");

            while (existing.Contains($"{baseName} {next}"))
                next++;

            return $"{baseName} {next}";
        }

        /// <summary>Remove a task sequence from the library. Clears runner refs that point to it.</summary>
        public void CommandDeleteTaskSequence(string id)
        {
            CurrentGameState.TaskSequenceLibrary.RemoveAll(ts => ts.Id == id);
            foreach (var runner in CurrentGameState.Runners)
            {
                if (runner.TaskSequenceId == id)
                    ClearTaskSequence(runner.Id);
                if (runner.PendingTaskSequenceId == id)
                    runner.PendingTaskSequenceId = null;
            }
            RefreshMacroConfigWarnings();
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
                }
            }
            RefreshMacroConfigWarnings();
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
            runner.MacroConfigWarning = ComputeMacroConfigWarning(runner);
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
            runner.MacroConfigWarning = ComputeMacroConfigWarning(runner);
            return clone.Id;
        }

        /// <summary>Deep-copy a task sequence into a new library entry. Returns new Id.</summary>
        public string CommandCloneTaskSequence(string sourceSequenceId)
        {
            var source = FindTaskSequenceInLibrary(sourceSequenceId);
            if (source == null) return null;
            var clone = source.DeepCopy();
            clone.Name = (source.Name ?? "Sequence") + " (copy)";
            CurrentGameState.TaskSequenceLibrary.Add(clone);
            return clone.Id;
        }

        /// <summary>Deep-copy a macro ruleset into a new library entry. Returns new Id.</summary>
        public string CommandCloneMacroRuleset(string sourceRulesetId)
        {
            var source = FindMacroRulesetInLibrary(sourceRulesetId);
            if (source == null) return null;
            var clone = source.DeepCopy();
            clone.Name = (source.Name ?? "Macro") + " (copy)";
            CurrentGameState.MacroRulesetLibrary.Add(clone);
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

        // ─── Combat Style Commands ───────────────────────────────────

        /// <summary>Register a combat style in the library. Returns its Id.</summary>
        public string CommandCreateCombatStyle(CombatStyle template)
        {
            if (string.IsNullOrEmpty(template.Id))
                template.Id = Guid.NewGuid().ToString();
            CurrentGameState.CombatStyleLibrary.Add(template);
            return template.Id;
        }

        /// <summary>Create a new blank combat style with defaults.</summary>
        public string CommandCreateCombatStyle()
        {
            var style = new CombatStyle
            {
                Name = GenerateNextName("Combat Style", CurrentGameState.CombatStyleLibrary, s => s.Name),
            };
            return CommandCreateCombatStyle(style);
        }

        /// <summary>Deep-copy a combat style into a new library entry. Returns new Id.</summary>
        public string CommandCloneCombatStyle(string sourceStyleId)
        {
            var source = FindCombatStyleInLibrary(sourceStyleId);
            if (source == null) return null;
            var clone = source.DeepCopy();
            clone.Name = (source.Name ?? "Combat Style") + " (copy)";
            CurrentGameState.CombatStyleLibrary.Add(clone);
            return clone.Id;
        }

        /// <summary>Remove a combat style from the library. Clears runner refs that point to it.</summary>
        public void CommandDeleteCombatStyle(string styleId)
        {
            CurrentGameState.CombatStyleLibrary.RemoveAll(s => s.Id == styleId);
            foreach (var runner in CurrentGameState.Runners)
            {
                if (runner.CombatStyleId == styleId)
                    runner.CombatStyleId = null;
            }
            // Also clear combat style overrides in task sequences
            foreach (var seq in CurrentGameState.TaskSequenceLibrary)
            {
                if (seq.Steps == null) continue;
                foreach (var step in seq.Steps)
                {
                    if (step.CombatStyleOverrideId == styleId)
                        step.CombatStyleOverrideId = null;
                }
            }
        }

        /// <summary>Rename a combat style.</summary>
        public void CommandRenameCombatStyle(string styleId, string newName)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null) return;
            style.Name = newName?.Trim() ?? "";
        }

        /// <summary>Assign a combat style to a runner by ID.</summary>
        public void CommandAssignCombatStyleToRunner(string runnerId, string styleId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;
            runner.CombatStyleId = styleId;
        }

        // ─── Combat Style Rule Manipulation ─────────────────────────

        /// <summary>Add a targeting rule to a combat style.</summary>
        public void CommandAddTargetingRule(string styleId, TargetingRule rule, int insertIndex = -1)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null) return;
            if (insertIndex < 0 || insertIndex >= style.TargetingRules.Count)
                style.TargetingRules.Add(rule);
            else
                style.TargetingRules.Insert(insertIndex, rule);
        }

        /// <summary>Remove a targeting rule from a combat style by index.</summary>
        public void CommandRemoveTargetingRule(string styleId, int ruleIndex)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null || ruleIndex < 0 || ruleIndex >= style.TargetingRules.Count) return;
            style.TargetingRules.RemoveAt(ruleIndex);
        }

        /// <summary>Replace a targeting rule at a given index.</summary>
        public void CommandUpdateTargetingRule(string styleId, int ruleIndex, TargetingRule rule)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null || ruleIndex < 0 || ruleIndex >= style.TargetingRules.Count) return;
            style.TargetingRules[ruleIndex] = rule;
        }

        /// <summary>Move a targeting rule from one index to another.</summary>
        public void CommandMoveTargetingRule(string styleId, int fromIndex, int toIndex)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null) return;
            if (fromIndex < 0 || fromIndex >= style.TargetingRules.Count) return;
            if (toIndex < 0 || toIndex >= style.TargetingRules.Count) return;
            var rule = style.TargetingRules[fromIndex];
            style.TargetingRules.RemoveAt(fromIndex);
            style.TargetingRules.Insert(toIndex, rule);
        }

        /// <summary>Add an ability rule to a combat style.</summary>
        public void CommandAddAbilityRule(string styleId, AbilityRule rule, int insertIndex = -1)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null) return;
            if (insertIndex < 0 || insertIndex >= style.AbilityRules.Count)
                style.AbilityRules.Add(rule);
            else
                style.AbilityRules.Insert(insertIndex, rule);
        }

        /// <summary>Remove an ability rule from a combat style by index.</summary>
        public void CommandRemoveAbilityRule(string styleId, int ruleIndex)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null || ruleIndex < 0 || ruleIndex >= style.AbilityRules.Count) return;
            style.AbilityRules.RemoveAt(ruleIndex);
        }

        /// <summary>Replace an ability rule at a given index.</summary>
        public void CommandUpdateAbilityRule(string styleId, int ruleIndex, AbilityRule rule)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null || ruleIndex < 0 || ruleIndex >= style.AbilityRules.Count) return;
            style.AbilityRules[ruleIndex] = rule;
        }

        /// <summary>Move an ability rule from one index to another.</summary>
        public void CommandMoveAbilityRule(string styleId, int fromIndex, int toIndex)
        {
            var style = FindCombatStyleInLibrary(styleId);
            if (style == null) return;
            if (fromIndex < 0 || fromIndex >= style.AbilityRules.Count) return;
            if (toIndex < 0 || toIndex >= style.AbilityRules.Count) return;
            var rule = style.AbilityRules[fromIndex];
            style.AbilityRules.RemoveAt(fromIndex);
            style.AbilityRules.Insert(toIndex, rule);
        }

        // ─── Template Commands ──────────────────────────────────────

        /// <summary>
        /// Find a step template by ID. Returns null if not found.
        /// </summary>
        public StepTemplate FindStepTemplate(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in CurrentGameState.StepTemplateLibrary)
                if (t.Id == id) return t;
            return null;
        }

        /// <summary>
        /// Find a rule template by ID in the specified library.
        /// </summary>
        public RuleTemplate FindRuleTemplate(string id, bool isMacro)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var library = isMacro
                ? CurrentGameState.MacroRuleTemplateLibrary
                : CurrentGameState.MicroRuleTemplateLibrary;
            foreach (var t in library)
                if (t.Id == id) return t;
            return null;
        }

        /// <summary>
        /// Apply a step template to a task sequence — batch-inserts all template steps.
        /// Null TargetNodeId on TravelTo steps is resolved to the first map node.
        /// </summary>
        public void CommandApplyStepTemplate(string seqId, string templateId)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;
            var template = FindStepTemplate(templateId);
            if (template == null) return;

            string defaultNodeId = CurrentGameState.Map?.Nodes?.Count > 0
                ? CurrentGameState.Map.Nodes[0].Id : "hub";

            foreach (var templateStep in template.DeepCopySteps())
            {
                // Resolve null TargetNodeId on TravelTo steps
                if (templateStep.Type == TaskStepType.TravelTo
                    && string.IsNullOrEmpty(templateStep.TargetNodeId))
                {
                    templateStep.TargetNodeId = defaultNodeId;
                }

                CommandAddStepToTaskSequence(seqId, templateStep);
            }
        }

        /// <summary>
        /// Apply a rule template to a ruleset — batch-inserts all template rules (deep-copied).
        /// </summary>
        public void CommandApplyRuleTemplate(string rulesetId, string templateId, bool isMacro)
        {
            var (ruleset, _) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;
            var template = FindRuleTemplate(templateId, isMacro);
            if (template == null) return;

            foreach (var rule in template.DeepCopyRules())
                CommandAddRuleToRuleset(rulesetId, rule);
        }

        /// <summary>
        /// Create a custom step template from a list of steps. Returns the new template's Id.
        /// </summary>
        public string CommandCreateStepTemplate(string name, List<TaskStep> steps)
        {
            var template = new StepTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                IsBuiltIn = false,
                Steps = new List<TaskStep>(),
            };
            // Deep-copy steps into the template
            foreach (var step in steps)
                template.Steps.Add(new TaskStep(step.Type, step.TargetNodeId, step.MicroRulesetId));
            CurrentGameState.StepTemplateLibrary.Add(template);
            return template.Id;
        }

        /// <summary>
        /// Create a custom rule template from a list of rules. Returns the new template's Id.
        /// </summary>
        public string CommandCreateRuleTemplate(string name, List<Rule> rules, bool isMacro)
        {
            var template = new RuleTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                IsBuiltIn = false,
            };
            // Deep-copy rules via the template's own method (after setting Rules)
            template.Rules = new List<Rule>();
            foreach (var rule in rules)
            {
                var newRule = new Rule
                {
                    Action = new AutomationAction
                    {
                        Type = rule.Action.Type,
                        StringParam = rule.Action.StringParam,
                        IntParam = rule.Action.IntParam,
                    },
                    Enabled = rule.Enabled,
                    FinishCurrentSequence = rule.FinishCurrentSequence,
                    Label = rule.Label,
                };
                foreach (var cond in rule.Conditions)
                {
                    newRule.Conditions.Add(new Condition
                    {
                        Type = cond.Type,
                        Operator = cond.Operator,
                        NumericValue = cond.NumericValue,
                        StringParam = cond.StringParam,
                        IntParam = cond.IntParam,
                    });
                }
                template.Rules.Add(newRule);
            }

            var library = isMacro
                ? CurrentGameState.MacroRuleTemplateLibrary
                : CurrentGameState.MicroRuleTemplateLibrary;
            library.Add(template);
            return template.Id;
        }

        /// <summary>
        /// Delete a step template. Refuses if the template is built-in.
        /// </summary>
        public bool CommandDeleteStepTemplate(string id)
        {
            var template = FindStepTemplate(id);
            if (template == null || template.IsBuiltIn) return false;
            CurrentGameState.StepTemplateLibrary.Remove(template);
            return true;
        }

        /// <summary>
        /// Delete a rule template. Refuses if the template is built-in.
        /// </summary>
        public bool CommandDeleteRuleTemplate(string id, bool isMacro)
        {
            var template = FindRuleTemplate(id, isMacro);
            if (template == null || template.IsBuiltIn) return false;
            var library = isMacro
                ? CurrentGameState.MacroRuleTemplateLibrary
                : CurrentGameState.MicroRuleTemplateLibrary;
            library.Remove(template);
            return true;
        }

        /// <summary>
        /// Rename any template (step, macro rule, or micro rule).
        /// </summary>
        public void CommandRenameTemplate(string id, string newName, TemplateKind kind)
        {
            switch (kind)
            {
                case TemplateKind.Step:
                    var step = FindStepTemplate(id);
                    if (step != null) step.Name = newName?.Trim() ?? "";
                    break;
                case TemplateKind.MacroRule:
                    var macro = FindRuleTemplate(id, isMacro: true);
                    if (macro != null) macro.Name = newName?.Trim() ?? "";
                    break;
                case TemplateKind.MicroRule:
                    var micro = FindRuleTemplate(id, isMacro: false);
                    if (micro != null) micro.Name = newName?.Trim() ?? "";
                    break;
            }
        }

        /// <summary>
        /// Reorder a template within its library (move to newIndex).
        /// </summary>
        public void CommandReorderTemplate(string id, int newIndex, TemplateKind kind)
        {
            switch (kind)
            {
                case TemplateKind.Step:
                    ReorderInList(CurrentGameState.StepTemplateLibrary, id, newIndex, t => t.Id);
                    break;
                case TemplateKind.MacroRule:
                    ReorderInList(CurrentGameState.MacroRuleTemplateLibrary, id, newIndex, t => t.Id);
                    break;
                case TemplateKind.MicroRule:
                    ReorderInList(CurrentGameState.MicroRuleTemplateLibrary, id, newIndex, t => t.Id);
                    break;
            }
        }

        private static void ReorderInList<T>(List<T> list, string id, int newIndex, Func<T, string> getId)
        {
            int fromIndex = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (getId(list[i]) == id) { fromIndex = i; break; }
            }
            if (fromIndex < 0) return;
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= list.Count) newIndex = list.Count - 1;
            if (fromIndex == newIndex) return;

            var item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(newIndex, item);
        }

        /// <summary>
        /// Maximum number of favorited templates per library.
        /// Prevents the quick-access row from overflowing.
        /// </summary>
        public const int MaxTemplateFavorites = 20;

        /// <summary>
        /// Toggle the IsFavorite flag on a template. Favorited templates appear
        /// as quick-access buttons in the editor. Refuses to add more than
        /// MaxTemplateFavorites per library.
        /// </summary>
        public bool CommandToggleTemplateFavorite(string id, TemplateKind kind)
        {
            switch (kind)
            {
                case TemplateKind.Step:
                    var step = FindStepTemplate(id);
                    if (step == null) return false;
                    if (step.IsFavorite) { step.IsFavorite = false; return true; }
                    if (CountFavorites(CurrentGameState.StepTemplateLibrary, t => t.IsFavorite) >= MaxTemplateFavorites)
                        return false;
                    step.IsFavorite = true;
                    return true;
                case TemplateKind.MacroRule:
                    var macro = FindRuleTemplate(id, isMacro: true);
                    if (macro == null) return false;
                    if (macro.IsFavorite) { macro.IsFavorite = false; return true; }
                    if (CountFavorites(CurrentGameState.MacroRuleTemplateLibrary, t => t.IsFavorite) >= MaxTemplateFavorites)
                        return false;
                    macro.IsFavorite = true;
                    return true;
                case TemplateKind.MicroRule:
                    var micro = FindRuleTemplate(id, isMacro: false);
                    if (micro == null) return false;
                    if (micro.IsFavorite) { micro.IsFavorite = false; return true; }
                    if (CountFavorites(CurrentGameState.MicroRuleTemplateLibrary, t => t.IsFavorite) >= MaxTemplateFavorites)
                        return false;
                    micro.IsFavorite = true;
                    return true;
            }
            return false;
        }

        private static int CountFavorites<T>(List<T> list, Func<T, bool> isFavorite)
        {
            int count = 0;
            foreach (var item in list)
                if (isFavorite(item)) count++;
            return count;
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
            var (ruleset, isMacro) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;

            if (insertIndex < 0 || insertIndex >= ruleset.Rules.Count)
                ruleset.Rules.Add(rule);
            else
                ruleset.Rules.Insert(insertIndex, rule);

            if (isMacro) RefreshMacroConfigWarnings();
        }

        /// <summary>Remove a rule from a ruleset by index.</summary>
        public void CommandRemoveRuleFromRuleset(string rulesetId, int ruleIndex)
        {
            var (ruleset, isMacro) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;
            if (ruleIndex < 0 || ruleIndex >= ruleset.Rules.Count) return;

            ruleset.Rules.RemoveAt(ruleIndex);
            if (isMacro) RefreshMacroConfigWarnings();
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
            var (ruleset, isMacro) = FindRulesetInAnyLibrary(rulesetId);
            if (ruleset == null) return;
            if (ruleIndex < 0 || ruleIndex >= ruleset.Rules.Count) return;

            ruleset.Rules[ruleIndex] = updatedRule;
            if (isMacro) RefreshMacroConfigWarnings();
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
            else
            {
                RefreshMacroConfigWarnings();
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

            // Adjust runner step indices: insert before/at current → increment.
            // Skip when the sequence had no steps before (count is 1 after this add) —
            // the runner's step index 0 means "start from the beginning", not a tracked position.
            if (seq.Steps.Count > 1)
            {
                foreach (var runner in CurrentGameState.Runners)
                {
                    if (runner.TaskSequenceId != seqId) continue;
                    if (actualIndex <= runner.TaskSequenceCurrentStepIndex)
                        runner.TaskSequenceCurrentStepIndex++;
                }
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

        /// <summary>Set the combat style override on a Work step. Null = use runner default.</summary>
        public void CommandSetWorkStepCombatStyleOverride(string seqId, int stepIndex, string combatStyleId)
        {
            var seq = FindTaskSequenceInLibrary(seqId);
            if (seq == null) return;
            if (stepIndex < 0 || stepIndex >= seq.Steps.Count) return;
            if (seq.Steps[stepIndex].Type != TaskStepType.Work) return;
            seq.Steps[stepIndex].CombatStyleOverrideId = combatStyleId;
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

        /// <summary>Find macro rules that reference a task sequence ID. Returns (rulesetName, ruleLabel) pairs.</summary>
        public List<(string rulesetName, string ruleLabel)> GetMacroRulesReferencingTaskSequence(string seqId)
        {
            var results = new List<(string, string)>();
            if (string.IsNullOrEmpty(seqId)) return results;
            foreach (var ruleset in CurrentGameState.MacroRulesetLibrary)
            {
                foreach (var rule in ruleset.Rules)
                {
                    if (rule.Action?.Type == ActionType.AssignSequence && rule.Action.StringParam == seqId)
                        results.Add((ruleset.Name ?? ruleset.Id, rule.Label ?? "Unnamed rule"));
                }
            }
            return results;
        }

        /// <summary>
        /// Recompute MacroConfigWarning for all runners whose macro rulesets might
        /// have broken references (rules pointing to deleted task sequences).
        /// Called from command methods that can change the validity of macro rule targets.
        /// </summary>
        public void RefreshMacroConfigWarnings()
        {
            foreach (var runner in CurrentGameState.Runners)
            {
                runner.MacroConfigWarning = ComputeMacroConfigWarning(runner);
            }
        }

        private string ComputeMacroConfigWarning(Runner runner)
        {
            if (string.IsNullOrEmpty(runner.MacroRulesetId)) return null;
            var ruleset = FindMacroRulesetInLibrary(runner.MacroRulesetId);
            if (ruleset == null) return null;
            foreach (var rule in ruleset.Rules)
            {
                if (rule.Action?.Type == ActionType.AssignSequence &&
                    !string.IsNullOrEmpty(rule.Action.StringParam) &&
                    FindTaskSequenceInLibrary(rule.Action.StringParam) == null)
                    return "Macro ruleset has rules referencing deleted sequences";
            }
            return null;
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

        /// <summary>Get names of task sequences that reference a micro ruleset in their Work steps.</summary>
        public List<string> GetSequenceNamesUsingMicroRuleset(string microId)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(microId)) return names;
            foreach (var seq in CurrentGameState.TaskSequenceLibrary)
            {
                if (seq.Steps == null) continue;
                foreach (var step in seq.Steps)
                {
                    if (step.Type == TaskStepType.Work && step.MicroRulesetId == microId)
                    {
                        names.Add(seq.Name ?? seq.Id);
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
                    runner.CompletedAtLeastOneCycle = true;
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
            DecisionLayer layer = DecisionLayer.Macro, bool wasInterrupted = false)
        {
            DecisionLog targetLog;
            if (layer == DecisionLayer.Macro)
                targetLog = CurrentGameState.MacroDecisionLog;
            else if (layer == DecisionLayer.Combat)
                targetLog = CurrentGameState.CombatDecisionLog;
            else
                targetLog = CurrentGameState.MicroDecisionLog;
            targetLog.Add(new DecisionLogEntry
            {
                TickNumber = CurrentGameState.TickCount,
                GameTime = CurrentGameState.TotalTimeElapsed,
                RunnerId = runner.Id,
                RunnerName = runner.Name,
                NodeId = runner.CurrentNodeId,
                Layer = layer,
                RuleIndex = ruleIndex,
                RuleLabel = !string.IsNullOrEmpty(rule.Label) ? rule.Label : $"Rule #{ruleIndex}",
                TriggerReason = triggerReason,
                ActionType = rule.Action.Type,
                ActionDetail = actionDetail,
                WasDeferred = wasDeferred,
                WasInterrupted = wasInterrupted,
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

            bool isRedirectFromOverworld = startX.HasValue && startZ.HasValue;

            // If redirecting mid-travel, calculate distance from virtual position
            float distance;
            if (isRedirectFromOverworld)
            {
                var targetNode = CurrentGameState.Map.GetNode(targetNodeId);
                if (targetNode == null) return;

                float? providerDist = PathDistanceProvider?.GetTravelDistance(runner.Id, startX.Value, startZ.Value, targetNodeId);
                if (providerDist.HasValue)
                {
                    distance = providerDist.Value;
                }
                else
                {
                    float dx = targetNode.WorldX - startX.Value;
                    float dz = targetNode.WorldZ - startZ.Value;
                    float raw = (float)Math.Sqrt(dx * dx + dz * dz) - targetNode.ApproachRadius;
                    distance = Math.Max(raw, 0.1f);
                }
            }
            else
            {
                float? providerDist = PathDistanceProvider?.GetTravelDistance(runner.Id, fromNode, targetNodeId);
                distance = providerDist ?? CurrentGameState.Map.FindPath(fromNode, targetNodeId, out _);
                if (distance < 0) return;
            }

            // Exit phase: only for normal travel from a node (not mid-overworld redirect)
            float exitDist = 0f;
            if (!isRedirectFromOverworld)
                exitDist = NodeGeometryProvider?.GetExitDistance(runner.Id, fromNode, targetNodeId) ?? 0f;

            runner.ActiveWarning = null;
            runner.State = RunnerState.Traveling;
            runner.Travel = new TravelState
            {
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                TotalDistance = distance,
                DistanceCovered = 0f,
                ExitDistance = exitDist,
                ExitDistanceCovered = 0f,
                StartWorldX = startX,
                StartWorldZ = startZ,
            };

            float speed = GetTravelSpeed(runner);
            float inNodeSpeed = GetInNodeTravelSpeed(runner);
            float exitDuration = exitDist > 0f ? exitDist / inNodeSpeed : 0f;
            Events.Publish(new RunnerStartedTravel
            {
                RunnerId = runner.Id,
                FromNodeId = fromNode,
                ToNodeId = targetNodeId,
                EstimatedDurationSeconds = exitDuration + distance / speed,
            });
        }

        // ─── Micro Override Commands ──────────────────────────────────

        /// <summary>Set a micro override for a specific Work step on a runner.</summary>
        public void CommandSetMicroOverride(string runnerId, int stepIndex, string microRulesetId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;

            runner.MicroOverrides ??= new List<MicroOverride>();

            // Replace existing override for this step, or add new
            for (int i = 0; i < runner.MicroOverrides.Count; i++)
            {
                if (runner.MicroOverrides[i].StepIndex == stepIndex)
                {
                    runner.MicroOverrides[i].MicroRulesetId = microRulesetId;
                    return;
                }
            }
            runner.MicroOverrides.Add(new MicroOverride { StepIndex = stepIndex, MicroRulesetId = microRulesetId });
        }

        /// <summary>Clear a micro override for a specific Work step on a runner.</summary>
        public void CommandClearMicroOverride(string runnerId, int stepIndex)
        {
            var runner = FindRunner(runnerId);
            if (runner?.MicroOverrides == null) return;

            for (int i = 0; i < runner.MicroOverrides.Count; i++)
            {
                if (runner.MicroOverrides[i].StepIndex == stepIndex)
                {
                    runner.MicroOverrides.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>Clear all micro overrides on a runner.</summary>
        public void CommandClearAllMicroOverrides(string runnerId)
        {
            var runner = FindRunner(runnerId);
            runner?.MicroOverrides?.Clear();
        }

        /// <summary>
        /// Fork the runner's current task sequence into a new one with overrides baked in.
        /// Creates a new sequence in the library, assigns it to the runner, clears overrides.
        /// </summary>
        /// <summary>
        /// Clone a micro ruleset and immediately set it as the override for a runner's Work step.
        /// Returns the cloned ruleset ID, or null on failure.
        /// </summary>
        public string CommandCloneMicroRulesetAsOverride(string runnerId, int stepIndex, string sourceMicroId)
        {
            string cloneId = CommandCloneMicroRuleset(sourceMicroId);
            if (cloneId == null) return null;
            CommandSetMicroOverride(runnerId, stepIndex, cloneId);
            return cloneId;
        }

        public void CommandForkTaskSequenceWithOverrides(string runnerId, string name = null)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return;
            if (runner.MicroOverrides == null || runner.MicroOverrides.Count == 0) return;

            var seq = GetRunnerTaskSequence(runner);
            if (seq == null) return;

            var copy = seq.DeepCopy();
            copy.Name = name ?? $"{seq.Name} ({runner.Name}'s)";
            copy.AutoGenerateName = false;

            // Bake overrides into the copy's steps
            foreach (var ov in runner.MicroOverrides)
            {
                if (ov.StepIndex >= 0 && ov.StepIndex < copy.Steps.Count
                    && copy.Steps[ov.StepIndex].Type == TaskStepType.Work)
                {
                    copy.Steps[ov.StepIndex].MicroRulesetId = ov.MicroRulesetId;
                }
            }

            // AssignRunner adds to library and clears overrides
            AssignRunner(runnerId, copy, "fork from overrides");
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
