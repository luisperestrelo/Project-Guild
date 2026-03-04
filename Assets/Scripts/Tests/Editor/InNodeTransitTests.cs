using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for sim-driven in-node transit phases on Gathering and Depositing states.
    /// When INodeGeometryProvider returns a distance, runners walk to the target
    /// at in-node speed before gathering/depositing begins. Same pattern as exit phase on travel.
    /// </summary>
    [TestFixture]
    public class InNodeTransitTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new("copper_ore", SkillType.Mining, 40f, 0.5f);

        private static readonly Simulation.Gathering.GatherableConfig TinGatherable =
            new("tin_ore", SkillType.Mining, 40f, 0.5f);

        [SetUp]
        public void SetUp()
        {
            _config = new SimulationConfig
            {
                BaseTravelSpeed = 10f,
                AthleticsSpeedPerLevel = 0f,
                InNodeSpeedMultiplier = 0.5f, // in-node speed = 10 * 0.5 = 5 m/s
                DepositDurationTicks = 10,
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("tin_ore", "Tin Ore", ItemCategory.Ore),
                },
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f, null, CopperGatherable, TinGatherable);
            map.AddEdge("hub", "mine", 100f);
            map.Initialize();

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Test Runner" }
                    .WithSkill(SkillType.Mining, 1),
            };
            _sim = new GameSimulation(_config, tickRate: 10f);
            _sim.StartNewGame(defs, map: map, hubNodeId: "mine");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        // ─── Gathering transit basics ────────────────────────────────

        [Test]
        public void StartGathering_WithTransitDistance_SetsTransitFields()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 10f);

            StartGatheringAtMine();

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(10f, _runner.Gathering.TransitDistance, 0.01f);
            Assert.AreEqual(0f, _runner.Gathering.TransitDistanceCovered, 0.01f);
            Assert.IsTrue(_runner.Gathering.IsInTransit);
        }

        [Test]
        public void GatheringTransit_TicksAtInNodeSpeed()
        {
            // In-node speed = 5 m/s, tick = 0.1s -> 0.5m per tick
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 5f);

            StartGatheringAtMine();
            _sim.Tick();

            Assert.AreEqual(0.5f, _runner.Gathering.TransitDistanceCovered, 0.01f);
            Assert.IsTrue(_runner.Gathering.IsInTransit);
        }

        [Test]
        public void GatheringTransit_NoXpDuringTransit()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 5f);

            float xpBefore = _runner.GetSkill(SkillType.Mining).Xp;
            StartGatheringAtMine();

            // Tick 5 times (2.5m of 5m transit — still walking)
            TickN(5);

            Assert.IsTrue(_runner.Gathering.IsInTransit);
            float xpAfter = _runner.GetSkill(SkillType.Mining).Xp;
            Assert.AreEqual(xpBefore, xpAfter, 0.01f, "No XP should be awarded during transit");
        }

        [Test]
        public void GatheringTransit_NoItemsDuringTransit()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 5f);

            StartGatheringAtMine();

            int slotsBefore = _runner.Inventory.Slots.Count;
            TickN(5); // still in transit

            Assert.IsTrue(_runner.Gathering.IsInTransit);
            Assert.AreEqual(slotsBefore, _runner.Inventory.Slots.Count, "No items should be produced during transit");
        }

        [Test]
        public void GatheringTransit_CompletesAndGatheringStarts()
        {
            // Transit: 5m at 5m/s = 10 ticks
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 5f);

            float xpBefore = _runner.GetSkill(SkillType.Mining).Xp;
            StartGatheringAtMine();
            TickN(10); // transit complete

            Assert.IsFalse(_runner.Gathering.IsInTransit);
            // After transit, first gathering tick should also have run (fall through)
            float xpAfter = _runner.GetSkill(SkillType.Mining).Xp;
            Assert.IsTrue(xpAfter > xpBefore, "XP should accrue after transit completes");
        }

        [Test]
        public void GatheringTransit_TransitProgressTracking()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 5f);

            StartGatheringAtMine();
            Assert.AreEqual(0f, _runner.Gathering.TransitProgress, 0.01f);

            TickN(5); // 2.5m of 5m
            Assert.AreEqual(0.5f, _runner.Gathering.TransitProgress, 0.01f);

            TickN(5); // 5m of 5m
            Assert.AreEqual(1f, _runner.Gathering.TransitProgress, 0.01f);
        }

        // ─── Gatherable switch transit ────────────────────────────────

        [Test]
        public void GatherableSwitch_SetsNewTransitDistance()
        {
            // Start gathering copper with no transit (provider returns 0 initially)
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 0f);
            StartGatheringAtMine();

            Assert.IsFalse(_runner.Gathering.IsInTransit);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex);

            // Now set provider to return distance (simulates old spot -> new spot distance)
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 8f);

            // Create a micro ruleset that specifically picks tin (index 1)
            var pickTinRuleset = new Ruleset
            {
                Id = "pick-tin",
                Name = "Pick Tin",
                Rules = new System.Collections.Generic.List<Rule>
                {
                    new()
                    {
                        Action = new AutomationAction
                        {
                            Type = ActionType.GatherHere,
                            StringParam = "tin_ore",
                        },
                    },
                },
            };
            _sim.CurrentGameState.MicroRulesetLibrary.Add(pickTinRuleset);

            var seq = _sim.GetRunnerTaskSequence(_runner);
            if (seq != null && seq.Steps != null)
            {
                foreach (var step in seq.Steps)
                {
                    if (step.Type == TaskStepType.Work)
                        step.MicroRulesetId = "pick-tin";
                }
            }

            // Tick to trigger micro re-eval — will switch from copper (0) to tin (1)
            _sim.Tick();

            Assert.AreEqual(1, _runner.Gathering.GatherableIndex, "Should have switched to tin");
            Assert.AreEqual(8f, _runner.Gathering.TransitDistance, 0.01f);
            Assert.AreEqual(0f, _runner.Gathering.TransitDistanceCovered, 0.01f);
            Assert.IsTrue(_runner.Gathering.IsInTransit);
        }

        // ─── Deposit transit basics ──────────────────────────────────

        [Test]
        public void ExecuteDepositStep_WithTransitDistance_SetsTransitFields()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(depositPointDistance: 12f);

            StartDepositingAtHub();

            Assert.AreEqual(RunnerState.Depositing, _runner.State);
            Assert.AreEqual(12f, _runner.Depositing.TransitDistance, 0.01f);
            Assert.AreEqual(0f, _runner.Depositing.TransitDistanceCovered, 0.01f);
            Assert.IsTrue(_runner.Depositing.IsInTransit);
        }

        [Test]
        public void DepositTransit_TicksAtInNodeSpeed()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(depositPointDistance: 5f);

            StartDepositingAtHub();
            _sim.Tick();

            Assert.AreEqual(0.5f, _runner.Depositing.TransitDistanceCovered, 0.01f);
            Assert.IsTrue(_runner.Depositing.IsInTransit);
        }

        [Test]
        public void DepositTransit_CountdownDoesNotTickDuringTransit()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(depositPointDistance: 5f);

            StartDepositingAtHub();

            int ticksBefore = _runner.Depositing.TicksRemaining;
            TickN(5); // still in transit

            Assert.IsTrue(_runner.Depositing.IsInTransit);
            Assert.AreEqual(ticksBefore, _runner.Depositing.TicksRemaining,
                "Deposit countdown should not tick during transit");
        }

        [Test]
        public void DepositTransit_CompletesAndCountdownStarts()
        {
            // Transit: 5m at 5m/s = 10 ticks
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(depositPointDistance: 5f);

            StartDepositingAtHub();
            int ticksBefore = _runner.Depositing.TicksRemaining;
            TickN(10); // transit complete

            Assert.IsFalse(_runner.Depositing.IsInTransit);
            // After transit, first deposit tick should have run (fall through)
            Assert.AreEqual(ticksBefore - 1, _runner.Depositing.TicksRemaining,
                "Deposit countdown should start after transit completes");
        }

        [Test]
        public void DepositTransit_TransitProgressTracking()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(depositPointDistance: 5f);

            StartDepositingAtHub();
            Assert.AreEqual(0f, _runner.Depositing.TransitProgress, 0.01f);

            TickN(5); // 2.5m of 5m
            Assert.AreEqual(0.5f, _runner.Depositing.TransitProgress, 0.01f);

            TickN(5); // 5m of 5m
            Assert.AreEqual(1f, _runner.Depositing.TransitProgress, 0.01f);
        }

        // ─── No transit (provider null) ──────────────────────────────

        [Test]
        public void StartGathering_NoProvider_InstantGathering()
        {
            // No provider set → transit distance = 0 → no transit phase
            StartGatheringAtMine();

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0f, _runner.Gathering.TransitDistance);
            Assert.IsFalse(_runner.Gathering.IsInTransit);
        }

        [Test]
        public void ExecuteDepositStep_NoProvider_InstantDeposit()
        {
            StartDepositingAtHub();

            Assert.AreEqual(RunnerState.Depositing, _runner.State);
            Assert.AreEqual(0f, _runner.Depositing.TransitDistance);
            Assert.IsFalse(_runner.Depositing.IsInTransit);
        }

        // ─── No transit (distance < 0.5m returns 0) ─────────────────

        [Test]
        public void StartGathering_TrivialDistance_NoTransit()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 0f);

            StartGatheringAtMine();

            Assert.AreEqual(0f, _runner.Gathering.TransitDistance);
            Assert.IsFalse(_runner.Gathering.IsInTransit);
        }

        [Test]
        public void ExecuteDepositStep_TrivialDistance_NoTransit()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(depositPointDistance: 0f);

            StartDepositingAtHub();

            Assert.AreEqual(0f, _runner.Depositing.TransitDistance);
            Assert.IsFalse(_runner.Depositing.IsInTransit);
        }

        // ─── Save/load compatibility ─────────────────────────────────

        [Test]
        public void OldSave_GatheringState_NoTransitFields_DefaultsToZero()
        {
            var gathering = new GatheringState
            {
                NodeId = "mine",
                GatherableIndex = 0,
                TickAccumulator = 5f,
                TicksRequired = 40f,
            };

            Assert.AreEqual(0f, gathering.TransitDistance);
            Assert.AreEqual(0f, gathering.TransitDistanceCovered);
            Assert.IsFalse(gathering.IsInTransit);
            Assert.AreEqual(1f, gathering.TransitProgress);
        }

        [Test]
        public void OldSave_DepositingState_NoTransitFields_DefaultsToZero()
        {
            var depositing = new DepositingState
            {
                TicksRemaining = 10,
            };

            Assert.AreEqual(0f, depositing.TransitDistance);
            Assert.AreEqual(0f, depositing.TransitDistanceCovered);
            Assert.IsFalse(depositing.IsInTransit);
            Assert.AreEqual(1f, depositing.TransitProgress);
        }

        // ─── Macro interrupt during transit ──────────────────────────

        [Test]
        public void MacroInterruptDuringGatheringTransit_ClearsState()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(gatheringSpotDistance: 100f);

            StartGatheringAtMine();
            Assert.IsTrue(_runner.Gathering.IsInTransit);

            // Interrupt via AssignRunner (same path as macro rules and player commands)
            var newSeq = TaskSequence.CreateLoop("hub", "hub");
            _sim.AssignRunner(_runner.Id, newSeq);

            Assert.IsNull(_runner.Gathering, "Gathering state should be cleared on reassignment");
        }

        [Test]
        public void MacroInterruptDuringDepositTransit_ClearsState()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(depositPointDistance: 100f);

            StartDepositingAtHub();
            Assert.IsTrue(_runner.Depositing.IsInTransit);

            // Interrupt via AssignRunner
            var newSeq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, newSeq);

            Assert.IsNull(_runner.Depositing, "Depositing state should be cleared on reassignment");
        }

        // ─── Provider returns null ───────────────────────────────────

        [Test]
        public void StartGathering_ProviderReturnsNull_NoTransit()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(
                gatheringSpotDistance: null, depositPointDistance: null, exitDistance: null);

            StartGatheringAtMine();

            Assert.AreEqual(0f, _runner.Gathering.TransitDistance);
            Assert.IsFalse(_runner.Gathering.IsInTransit);
        }

        [Test]
        public void ExecuteDepositStep_ProviderReturnsNull_NoTransit()
        {
            _sim.NodeGeometryProvider = new MockNodeGeometryProvider(
                gatheringSpotDistance: null, depositPointDistance: null, exitDistance: null);

            StartDepositingAtHub();

            Assert.AreEqual(0f, _runner.Depositing.TransitDistance);
            Assert.IsFalse(_runner.Depositing.IsInTransit);
        }

        // ─── Helpers ─────────────────────────────────────────────────

        private void StartGatheringAtMine()
        {
            // Runner starts at mine. Assign a gather loop which starts gathering.
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);

            // Runner is at mine → TravelTo(mine) skips → Work step → StartGathering
            Assert.AreEqual(RunnerState.Gathering, _runner.State,
                "Runner should be gathering after assignment at mine");
        }

        private void StartDepositingAtHub()
        {
            // Move runner to hub, give them items, then trigger deposit
            _runner.CurrentNodeId = "hub";
            _runner.State = RunnerState.Idle;
            _runner.Travel = null;
            _runner.Gathering = null;

            // Add items so deposit has something to deposit
            var itemDef = _sim.ItemRegistry.Get("copper_ore");
            _runner.Inventory.TryAdd(itemDef, 5);

            // Create a sequence that starts with Deposit at hub
            var seq = new TaskSequence
            {
                Id = "test-deposit",
                Name = "Test Deposit",
                Loop = false,
                Steps = new System.Collections.Generic.List<TaskStep>
                {
                    new() { Type = TaskStepType.Deposit },
                },
            };
            _sim.CurrentGameState.TaskSequenceLibrary.Add(seq);
            _sim.AssignRunner(_runner.Id, seq);
        }

        private void TickN(int n)
        {
            for (int i = 0; i < n; i++)
                _sim.Tick();
        }

        /// <summary>
        /// Configurable mock that returns fixed distances for all query types.
        /// Default: returns 0 for all distances (no transit).
        /// </summary>
        private class MockNodeGeometryProvider : INodeGeometryProvider
        {
            private readonly float? _exitDistance;
            private readonly float? _gatheringSpotDistance;
            private readonly float? _depositPointDistance;

            public MockNodeGeometryProvider(
                float? gatheringSpotDistance = 0f,
                float? depositPointDistance = 0f,
                float? exitDistance = 0f)
            {
                _exitDistance = exitDistance;
                _gatheringSpotDistance = gatheringSpotDistance;
                _depositPointDistance = depositPointDistance;
            }

            public float? GetExitDistance(string runnerId, string nodeId, string destinationNodeId)
                => _exitDistance;

            public float? GetGatheringSpotDistance(string runnerId, string nodeId, int gatherableIndex)
                => _gatheringSpotDistance;

            public float? GetDepositPointDistance(string runnerId, string nodeId)
                => _depositPointDistance;
        }
    }
}
