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

            // HubNodeId is set by the map itself (CreateStarterMap or WorldMapAsset.ToWorldMap).
            // The hubNodeId parameter below is for runner spawning, not hub identification.

            // Populate item registry from config
            ItemRegistry = new ItemRegistry();
            foreach (var itemDef in Config.ItemDefinitions)
                ItemRegistry.Register(itemDef);

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
                // To create a definition with a forced name, set Name = "Whatever"

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

            // Process all runners
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

            // Award athletics XP every tick while traveling (same decoupling as gathering).
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

            // ─── Redirect: runner is already traveling, change destination mid-travel ───
            if (runner.State == RunnerState.Traveling && runner.Travel != null)
            {
                if (targetNodeId == runner.Travel.ToNodeId) return false; // already going there

                var map = CurrentGameState.Map;
                var fromNode = map.GetNode(runner.Travel.FromNodeId);
                var toNode = map.GetNode(runner.Travel.ToNodeId);
                var newTarget = map.GetNode(targetNodeId);
                if (fromNode == null || toNode == null || newTarget == null) return false;

                // Calculate the runner's virtual position by lerping between origin and destination
                // using their current travel progress (0.0 = at origin, 1.0 = at destination).
                //
                // If the runner already has an overridden start position (from a previous redirect),
                // lerp from that override instead of the FromNode position.
                float progress = runner.Travel.Progress;
                float startX = runner.Travel.StartWorldX ?? fromNode.WorldX;
                float startZ = runner.Travel.StartWorldZ ?? fromNode.WorldZ;
                float virtualX = startX + (toNode.WorldX - startX) * progress;
                float virtualZ = startZ + (toNode.WorldZ - startZ) * progress;

                // Distance from virtual position to new target (Euclidean), scaled for gameplay.
                float dx = newTarget.WorldX - virtualX;
                float dz = newTarget.WorldZ - virtualZ;
                float distToNewTarget = (float)Math.Sqrt(dx * dx + dz * dz) * map.TravelDistanceScale;

                // Store virtual position as the start point so the view can lerp from here
                // to the new destination without any visual snap/teleport.
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

            // ─── Normal travel: runner is idle (or gathering — handled by existing guards) ───
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
        /// The two-argument overload that uses the world map is generally preferred.
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
        /// Runner must be Idle and standing at a node with gatherables.
        /// gatherableIndex selects which gatherable to work on (default 0 = first).
        /// The automation layer decides which index to pass.
        /// </summary>
        /// <returns>True if gathering started, false if invalid.</returns>
        public bool CommandGather(string runnerId, int gatherableIndex = 0)
        {
            var runner = FindRunner(runnerId);
            if (runner == null || runner.State != RunnerState.Idle) return false;

            var node = CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            if (node == null || node.Gatherables.Length == 0) return false;
            if (gatherableIndex < 0 || gatherableIndex >= node.Gatherables.Length) return false;

            var gatherableConfig = node.Gatherables[gatherableIndex];

            // Check minimum skill level requirement
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

            float ticksRequired = CalculateTicksRequired(runner, gatherableConfig);

            runner.State = RunnerState.Gathering;
            runner.Gathering = new GatheringState
            {
                NodeId = runner.CurrentNodeId,
                GatherableIndex = gatherableIndex,
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
        /// The selected GatheringFormula determines how skill level translates to speed.
        /// Result: baseTicks / speedMultiplier (higher multiplier = faster gathering).
        /// </summary>
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
            if (runner.Gathering == null || runner.Gathering.SubState != GatheringSubState.Gathering)
                return;

            var node = CurrentGameState.Map.GetNode(runner.Gathering.NodeId);
            if (node == null || runner.Gathering.GatherableIndex >= node.Gatherables.Length) return;

            var gatherableConfig = node.Gatherables[runner.Gathering.GatherableIndex];

            // Award XP every tick while gathering (decoupled from item production speed).
            // The runner learns by practicing, not by completing items.
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
            float distance = CurrentGameState.Map.FindPath(runner.CurrentNodeId, targetNodeId, out _);
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

            string hubNodeId = CurrentGameState.Map.HubNodeId;

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
            CurrentGameState.Bank.DepositAll(runner.Inventory);

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
            var node = CurrentGameState.Map.GetNode(runner.Gathering.NodeId);
            var gatherableConfig = node.Gatherables[runner.Gathering.GatherableIndex];

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

        /// <summary>
        /// Cancel a runner's gathering session (including auto-return loop).
        /// Runner returns to Idle at their current location.
        /// </summary>
        public bool CancelGathering(string runnerId)
        {
            var runner = FindRunner(runnerId);
            if (runner == null) return false;
            if (runner.Gathering == null) return false;

            runner.Gathering = null;
            runner.State = RunnerState.Idle;
            runner.Travel = null; // cancel any in-progress auto-return travel too
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
