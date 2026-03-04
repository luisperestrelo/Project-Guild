using NUnit.Framework;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for the "exiting node" travel phase.
    /// When INodeGeometryProvider returns an exit distance, travel has two phases:
    /// 1. Exit: runner walks to node edge at in-node speed
    /// 2. Overworld: runner travels between nodes at overworld speed
    /// </summary>
    [TestFixture]
    public class ExitPhaseTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        // Simple 3-node map: A(0,0) → B(100,0), A → C(0,100)
        // Overworld distances: A→B = 100, A→C = 100

        [SetUp]
        public void SetUp()
        {
            var map = new WorldMap();
            map.HubNodeId = "A";
            map.AddNode("A", "Node A", 0f, 0f);
            map.AddNode("B", "Node B", 100f, 0f);
            map.AddNode("C", "Node C", 0f, 100f);
            map.AddEdge("A", "B", 100f);
            map.AddEdge("A", "C", 100f);
            map.AddEdge("B", "C", 141.42f);
            map.Initialize();

            _config = new SimulationConfig
            {
                BaseTravelSpeed = 10f,
                AthleticsSpeedPerLevel = 0f, // constant overworld speed for predictable tests
                InNodeSpeedMultiplier = 0.5f, // in-node speed = 10 * 0.5 = 5 m/s
                AthleticsXpPerTick = 1f,
            };

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Test Runner" }
                    .WithSkill(SkillType.Athletics, 1),
            };
            _sim = new GameSimulation(_config, tickRate: 10f);
            _sim.StartNewGame(defs, map: map, hubNodeId: "A");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        // ─── Exit phase basics ──────────────────────────────────────

        [Test]
        public void CommandTravel_WithExitDistance_SetsExitFields()
        {
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 20f);

            Assert.AreEqual(20f, _runner.Travel.ExitDistance);
            Assert.AreEqual(0f, _runner.Travel.ExitDistanceCovered);
            Assert.IsTrue(_runner.Travel.IsExitingNode);
        }

        [Test]
        public void CommandTravel_WithoutExitDistance_NoExitPhase()
        {
            _sim.CommandTravel(_runner.Id, "B", distance: 100f);

            Assert.AreEqual(0f, _runner.Travel.ExitDistance);
            Assert.IsFalse(_runner.Travel.IsExitingNode);
        }

        [Test]
        public void ExitPhase_TicksAtInNodeSpeed()
        {
            // In-node speed = 5 m/s, tick = 0.1s → 0.5m per tick
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 5f);
            _sim.Tick(); // 0.5m exit progress

            Assert.AreEqual(0.5f, _runner.Travel.ExitDistanceCovered, 0.01f);
            Assert.AreEqual(0f, _runner.Travel.DistanceCovered, 0.01f); // overworld hasn't started
            Assert.IsTrue(_runner.Travel.IsExitingNode);
        }

        [Test]
        public void ExitPhase_DoesNotTickOverworld()
        {
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 5f);

            // Tick 5 times = 2.5m of 5m exit distance → still exiting
            TickN(5);

            Assert.IsTrue(_runner.Travel.IsExitingNode);
            Assert.AreEqual(0f, _runner.Travel.DistanceCovered, 0.01f);
        }

        [Test]
        public void ExitPhase_TransitionsToOverworld()
        {
            // Exit distance 5m at 5m/s = 10 ticks to exit
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 5f);

            TickN(10); // exit phase complete

            Assert.IsFalse(_runner.Travel.IsExitingNode);
            // After exit completes, first overworld tick also runs (fall through)
            Assert.IsTrue(_runner.Travel.DistanceCovered > 0f);
        }

        [Test]
        public void ExitPhase_ThenOverworld_ArrivesAtDestination()
        {
            // Exit: 5m at 5m/s = 1s = 10 ticks
            // Overworld: 100m at 10m/s = 10s = 100 ticks
            // Total: ~110 ticks
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 5f);

            TickN(200); // plenty to arrive

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.AreEqual("B", _runner.CurrentNodeId);
        }

        // ─── Progress properties ─────────────────────────────────────

        [Test]
        public void ExitProgress_ReflectsExitPhase()
        {
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 5f);

            Assert.AreEqual(0f, _runner.Travel.ExitProgress, 0.01f);

            TickN(5); // 2.5m of 5m
            Assert.AreEqual(0.5f, _runner.Travel.ExitProgress, 0.01f);

            TickN(5); // 5m of 5m
            Assert.AreEqual(1f, _runner.Travel.ExitProgress, 0.01f);
        }

        [Test]
        public void OverallProgress_CombinesExitAndOverworld()
        {
            // Exit: 10m, Overworld: 90m → total 100m
            _sim.CommandTravel(_runner.Id, "B", distance: 90f, exitDistance: 10f);

            Assert.AreEqual(0f, _runner.Travel.OverallProgress, 0.01f);

            // After exit complete (10m at 5m/s = 20 ticks)
            TickN(20);
            // ExitDistanceCovered >= ExitDistance, DistanceCovered has first overworld tick
            // OverallProgress = (10 + small) / 100
            Assert.IsTrue(_runner.Travel.OverallProgress > 0.09f);
            Assert.IsTrue(_runner.Travel.OverallProgress < 0.15f);
        }

        [Test]
        public void OverallProgress_WithNoExitPhase_EqualsProgress()
        {
            _sim.CommandTravel(_runner.Id, "B", distance: 100f);

            TickN(50); // 50m of 100m
            Assert.AreEqual(_runner.Travel.Progress, _runner.Travel.OverallProgress, 0.01f);
        }

        // ─── Athletics XP during exit phase ──────────────────────────

        [Test]
        public void ExitPhase_AwardsAthleticsXp()
        {
            float xpBefore = _runner.GetSkill(SkillType.Athletics).Xp;
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 5f);

            TickN(5); // still in exit phase

            float xpAfter = _runner.GetSkill(SkillType.Athletics).Xp;
            Assert.IsTrue(xpAfter > xpBefore, "Athletics XP should accrue during exit phase");
            Assert.AreEqual(5f, xpAfter - xpBefore, 0.01f); // 5 ticks * 1.0 XP/tick
        }

        // ─── Redirect during exit phase ──────────────────────────────

        [Test]
        public void RedirectDuringExit_CancelsExitAndStartsFreshTravel()
        {
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 10f);
            TickN(5); // partway through exit

            Assert.IsTrue(_runner.Travel.IsExitingNode);

            // Redirect to C — should cancel exit and start fresh
            bool result = _sim.CommandTravel(_runner.Id, "C");

            Assert.IsTrue(result);
            Assert.AreEqual("C", _runner.Travel.ToNodeId);
            Assert.AreEqual(0f, _runner.Travel.DistanceCovered);
            // ExitDistance depends on NodeGeometryProvider (null here → 0)
            Assert.AreEqual(0f, _runner.Travel.ExitDistance);
        }

        [Test]
        public void RedirectDuringExit_RunnerStaysAtCurrentNode()
        {
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 10f);
            TickN(5);

            _sim.CommandTravel(_runner.Id, "C");

            // Runner should still be at A (hasn't left during exit phase)
            Assert.AreEqual("A", _runner.Travel.FromNodeId);
        }

        [Test]
        public void RedirectDuringExit_ArrivesAtNewDestination()
        {
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 10f);
            TickN(5);

            _sim.CommandTravel(_runner.Id, "C");
            TickN(500);

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.AreEqual("C", _runner.CurrentNodeId);
        }

        // ─── Redirect during overworld (no regression) ───────────────

        [Test]
        public void RedirectDuringOverworld_WorksAsBeforeWithExitPhase()
        {
            // Start travel with exit phase, complete exit, then redirect during overworld
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 5f);
            TickN(10); // exit complete (5m at 5m/s = 10 ticks)
            TickN(10); // 10 ticks into overworld = 10m distance

            // Should now be in overworld phase
            Assert.IsFalse(_runner.Travel.IsExitingNode);
            Assert.IsTrue(_runner.Travel.DistanceCovered > 0f);

            // Redirect to C
            bool result = _sim.CommandTravel(_runner.Id, "C");
            Assert.IsTrue(result);
            Assert.AreEqual("C", _runner.Travel.ToNodeId);
        }

        // ─── In-node speed scaling ───────────────────────────────────

        [Test]
        public void InNodeSpeed_IsOverworldSpeedTimesMultiplier()
        {
            // Overworld speed = 10 (from SetUp config), multiplier = 0.5 → in-node = 5
            // Test via explicit CommandTravel with exit distance:
            // 5m exit at 5m/s = 10 ticks. If in-node speed were wrong, tick count would differ.
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 5f);
            TickN(9);
            Assert.IsTrue(_runner.Travel.IsExitingNode, "Should still be exiting after 9 ticks at 5m/s");
            _sim.Tick(); // 10th tick completes 5m
            Assert.IsFalse(_runner.Travel.IsExitingNode, "Should finish exit after 10 ticks at 5m/s");
        }

        [Test]
        public void InNodeSpeed_ScalesWithMultiplier()
        {
            var config = new SimulationConfig
            {
                BaseTravelSpeed = 10f,
                AthleticsSpeedPerLevel = 0f,
                InNodeSpeedMultiplier = 2.0f, // in-node = 20 m/s
            };
            // Verify via sim: 20m exit at 20m/s (10 * 2.0) = 10 ticks
            var map = new WorldMap();
            map.HubNodeId = "X";
            map.AddNode("X", "X", 0f, 0f);
            map.AddNode("Y", "Y", 100f, 0f);
            map.AddEdge("X", "Y", 100f);
            map.Initialize();

            var sim = new GameSimulation(config, tickRate: 10f);
            var defs = new[] { new RunnerFactory.RunnerDefinition { Name = "Fast" }.WithSkill(SkillType.Athletics, 1) };
            sim.StartNewGame(defs, map: map, hubNodeId: "X");
            var runner = sim.CurrentGameState.Runners[0];

            sim.CommandTravel(runner.Id, "Y", distance: 100f, exitDistance: 20f);
            for (int i = 0; i < 9; i++) sim.Tick();
            Assert.IsTrue(runner.Travel.IsExitingNode);
            sim.Tick(); // 10th tick: 20m at 20m/s (2m/tick) = done
            Assert.IsFalse(runner.Travel.IsExitingNode);
        }

        // ─── Save/load compatibility ─────────────────────────────────

        [Test]
        public void OldSave_NoExitFields_DefaultsToZero()
        {
            // Simulate an old save: TravelState without exit fields
            var travel = new TravelState
            {
                FromNodeId = "A",
                ToNodeId = "B",
                TotalDistance = 100f,
                DistanceCovered = 50f,
            };

            // New fields default to 0
            Assert.AreEqual(0f, travel.ExitDistance);
            Assert.AreEqual(0f, travel.ExitDistanceCovered);
            Assert.IsFalse(travel.IsExitingNode);
            Assert.AreEqual(1f, travel.ExitProgress);
            Assert.AreEqual(travel.Progress, travel.OverallProgress, 0.01f);
        }

        // ─── NodeGeometryProvider integration ────────────────────────

        [Test]
        public void CommandTravel_WithNodeGeometryProvider_QueriesExitDistance()
        {
            var mockProvider = new MockNodeGeometryProvider(15f);
            _sim.NodeGeometryProvider = mockProvider;

            _sim.CommandTravel(_runner.Id, "B");

            Assert.AreEqual(15f, _runner.Travel.ExitDistance, 0.01f);
            Assert.IsTrue(_runner.Travel.IsExitingNode);
        }

        [Test]
        public void CommandTravel_ProviderReturnsNull_NoExitPhase()
        {
            var mockProvider = new MockNodeGeometryProvider(null);
            _sim.NodeGeometryProvider = mockProvider;

            _sim.CommandTravel(_runner.Id, "B");

            Assert.AreEqual(0f, _runner.Travel.ExitDistance);
            Assert.IsFalse(_runner.Travel.IsExitingNode);
        }

        [Test]
        public void EstimatedDuration_IncludesExitTime()
        {
            float capturedDuration = 0f;
            _sim.Events.Subscribe<RunnerStartedTravel>(e => capturedDuration = e.EstimatedDurationSeconds);

            // Exit: 10m at 5m/s = 2s. Overworld: 100m at 10m/s = 10s. Total: 12s.
            _sim.CommandTravel(_runner.Id, "B", distance: 100f, exitDistance: 10f);

            Assert.AreEqual(12f, capturedDuration, 0.1f);
        }

        [Test]
        public void EstimatedDuration_WithoutExit_IsJustOverworld()
        {
            float capturedDuration = 0f;
            _sim.Events.Subscribe<RunnerStartedTravel>(e => capturedDuration = e.EstimatedDurationSeconds);

            // Overworld: 100m at 10m/s = 10s
            _sim.CommandTravel(_runner.Id, "B", distance: 100f);

            Assert.AreEqual(10f, capturedDuration, 0.1f);
        }

        // ─── Helpers ────────────────────────────────────────────────

        private void TickN(int n)
        {
            for (int i = 0; i < n; i++)
                _sim.Tick();
        }

        /// <summary>
        /// Simple mock that returns a fixed exit distance for any query.
        /// </summary>
        private class MockNodeGeometryProvider : INodeGeometryProvider
        {
            private readonly float? _exitDistance;

            public MockNodeGeometryProvider(float? exitDistance)
            {
                _exitDistance = exitDistance;
            }

            public float? GetExitDistance(string runnerId, string nodeId, string destinationNodeId)
                => _exitDistance;

            public float? GetGatheringSpotDistance(string runnerId, string nodeId, int gatherableIndex)
                => 0f;

            public float? GetDepositPointDistance(string runnerId, string nodeId)
                => 0f;
        }
    }
}
