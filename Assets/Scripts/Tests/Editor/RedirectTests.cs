using NUnit.Framework;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for mid-travel redirect behavior.
    /// Uses a simple 3-node map with known positions so distance math is predictable.
    /// </summary>
    [TestFixture]
    public class RedirectTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private WorldMap _map;

        // Node positions (simple right triangle for easy distance math)
        // A (0,0) --- B (30,0)
        //     \
        //      C (0,40)
        // A→B = 30, A→C = 40, B→C = 50

        [SetUp]
        public void SetUp()
        {
            _map = new WorldMap();
            _map.HubNodeId = "A";
            _map.TravelDistanceScale = 1f; // 1:1 so distances are predictable
            _map.AddNode("A", "Node A", 0f, 0f);
            _map.AddNode("B", "Node B", 30f, 0f);
            _map.AddNode("C", "Node C", 0f, 40f);
            _map.AddEdge("A", "B", 30f);
            _map.AddEdge("A", "C", 40f);
            _map.AddEdge("B", "C", 50f);
            _map.Initialize();

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Test Runner" }
                    .WithSkill(SkillType.Athletics, 1),
            };
            _sim = new GameSimulation(new SimulationConfig
            {
                BaseTravelSpeed = 10f,
                AthleticsSpeedPerLevel = 0f, // constant speed for predictable tests
            }, tickRate: 10f);
            _sim.StartNewGame(defs, map: _map, hubNodeId: "A");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        // ─── Basic redirect ──────────────────────────────────────────

        [Test]
        public void Redirect_ChangesDestination()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5); // 50% of the way to B

            bool result = _sim.CommandTravel(_runner.Id, "C");

            Assert.IsTrue(result);
            Assert.AreEqual("C", _runner.Travel.ToNodeId);
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
        }

        [Test]
        public void Redirect_SetsStartWorldOverride()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5); // halfway A→B

            _sim.CommandTravel(_runner.Id, "C");

            Assert.IsTrue(_runner.Travel.StartWorldX.HasValue);
            Assert.IsTrue(_runner.Travel.StartWorldZ.HasValue);
        }

        [Test]
        public void Redirect_VirtualPositionIsCorrect()
        {
            // A=(0,0), B=(30,0). Travel A→B, redirect at 50%.
            // Virtual position should be (15, 0).
            _sim.CommandTravel(_runner.Id, "B");
            TickN(15); // speed=10, tick=0.1s, 15 ticks = 15 distance out of 30 = 50%

            _sim.CommandTravel(_runner.Id, "C");

            Assert.AreEqual(15f, _runner.Travel.StartWorldX.Value, 0.1f);
            Assert.AreEqual(0f, _runner.Travel.StartWorldZ.Value, 0.1f);
        }

        [Test]
        public void Redirect_TotalDistanceIsEuclideanToNewTarget()
        {
            // After redirect at (15, 0) heading to C=(0, 40):
            // distance = sqrt(15^2 + 40^2) = sqrt(225+1600) = sqrt(1825) ≈ 42.72
            _sim.CommandTravel(_runner.Id, "B");
            TickN(15);

            _sim.CommandTravel(_runner.Id, "C");

            float expected = (float)System.Math.Sqrt(15f * 15f + 40f * 40f);
            Assert.AreEqual(expected, _runner.Travel.TotalDistance, 0.1f);
        }

        [Test]
        public void Redirect_ResetsDistanceCovered()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5);

            _sim.CommandTravel(_runner.Id, "C");

            Assert.AreEqual(0f, _runner.Travel.DistanceCovered);
        }

        [Test]
        public void Redirect_PreservesFromNodeId()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5);

            _sim.CommandTravel(_runner.Id, "C");

            // FromNodeId stays as original departure node
            Assert.AreEqual("A", _runner.Travel.FromNodeId);
        }

        [Test]
        public void Redirect_RunnerArrivesAtNewDestination()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5);

            _sim.CommandTravel(_runner.Id, "C");

            // Tick enough to cover any reasonable distance
            TickN(500);

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.AreEqual("C", _runner.CurrentNodeId);
        }

        // ─── Redirect to same / current destination ──────────────────

        [Test]
        public void Redirect_ToCurrentDestination_IsNoOp()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5);

            bool result = _sim.CommandTravel(_runner.Id, "B");

            Assert.IsFalse(result);
            Assert.AreEqual("B", _runner.Travel.ToNodeId);
        }

        // ─── Redirect back to origin (replaces old cancel) ──────────

        [Test]
        public void Redirect_BackToOrigin_Works()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(10); // 1/3 of the way

            bool result = _sim.CommandTravel(_runner.Id, "A");

            Assert.IsTrue(result);
            Assert.AreEqual("A", _runner.Travel.ToNodeId);
        }

        [Test]
        public void Redirect_BackToOrigin_ArrivesAtOrigin()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(10);

            _sim.CommandTravel(_runner.Id, "A");
            TickN(500);

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.AreEqual("A", _runner.CurrentNodeId);
        }

        [Test]
        public void Redirect_BackToOrigin_VirtualPositionIsCorrect()
        {
            // A=(0,0), B=(30,0). After 10 ticks at speed 10: 10 distance covered.
            // Virtual pos = (10, 0). Redirect to A.
            // StartWorldX should be 10, distance to A = 10.
            _sim.CommandTravel(_runner.Id, "B");
            TickN(10);

            _sim.CommandTravel(_runner.Id, "A");

            Assert.AreEqual(10f, _runner.Travel.StartWorldX.Value, 0.1f);
            Assert.AreEqual(0f, _runner.Travel.StartWorldZ.Value, 0.1f);
            Assert.AreEqual(10f, _runner.Travel.TotalDistance, 0.1f);
        }

        // ─── Chained redirects ───────────────────────────────────────

        [Test]
        public void ChainedRedirect_VirtualPositionIsCorrect()
        {
            // A=(0,0) → B=(30,0), redirect at (15,0) → C=(0,40)
            // Then redirect again at some point along (15,0)→(0,40).
            // After 10 ticks toward C from (15,0): 10 distance out of ~42.72.
            // Progress ≈ 10/42.72 ≈ 0.234
            // Virtual pos ≈ (15 + (0-15)*0.234, 0 + (40-0)*0.234) ≈ (11.49, 9.36)
            _sim.CommandTravel(_runner.Id, "B");
            TickN(15); // at (15, 0)

            _sim.CommandTravel(_runner.Id, "C");
            TickN(10); // 10 distance toward C

            float distToC = (float)System.Math.Sqrt(15f * 15f + 40f * 40f); // ~42.72
            float progress = 10f / distToC;
            float expectedX = 15f + (0f - 15f) * progress;
            float expectedZ = 0f + (40f - 0f) * progress;

            _sim.CommandTravel(_runner.Id, "B"); // redirect again

            Assert.AreEqual(expectedX, _runner.Travel.StartWorldX.Value, 0.5f);
            Assert.AreEqual(expectedZ, _runner.Travel.StartWorldZ.Value, 0.5f);
        }

        [Test]
        public void ChainedRedirect_ArrivesAtFinalDestination()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5);

            _sim.CommandTravel(_runner.Id, "C");
            TickN(5);

            _sim.CommandTravel(_runner.Id, "B");
            TickN(500);

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.AreEqual("B", _runner.CurrentNodeId);
        }

        [Test]
        public void ChainedRedirect_BackAndForth_ArrivesCorrectly()
        {
            // A→B, redirect to A, redirect to B, redirect to A — should arrive at A
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5);

            _sim.CommandTravel(_runner.Id, "A");
            TickN(3);

            _sim.CommandTravel(_runner.Id, "B");
            TickN(2);

            _sim.CommandTravel(_runner.Id, "A");
            TickN(500);

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.AreEqual("A", _runner.CurrentNodeId);
        }

        // ─── Normal travel has no StartWorld override ────────────────

        [Test]
        public void NormalTravel_NoStartWorldOverride()
        {
            _sim.CommandTravel(_runner.Id, "B");

            Assert.IsFalse(_runner.Travel.StartWorldX.HasValue);
            Assert.IsFalse(_runner.Travel.StartWorldZ.HasValue);
        }

        // ─── Events ──────────────────────────────────────────────────

        [Test]
        public void Redirect_PublishesStartedTravelEvent()
        {
            _sim.CommandTravel(_runner.Id, "B");
            TickN(5);

            string eventToNode = null;
            _sim.Events.Subscribe<RunnerStartedTravel>(e => eventToNode = e.ToNodeId);

            _sim.CommandTravel(_runner.Id, "C");

            Assert.AreEqual("C", eventToNode);
        }

        // ─── Edge cases ──────────────────────────────────────────────

        [Test]
        public void Redirect_WhenNotTraveling_StartsNormalTravel()
        {
            // Runner is idle at A, CommandTravel to B should start normal travel, not redirect
            bool result = _sim.CommandTravel(_runner.Id, "B");

            Assert.IsTrue(result);
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.IsFalse(_runner.Travel.StartWorldX.HasValue);
            Assert.AreEqual(30f, _runner.Travel.TotalDistance, 0.1f);
        }

        [Test]
        public void Redirect_AtProgressZero_VirtualPosMatchesFromNode()
        {
            // Redirect immediately (no ticks) — virtual pos should be the departure node
            _sim.CommandTravel(_runner.Id, "B");

            _sim.CommandTravel(_runner.Id, "C");

            // A = (0, 0)
            Assert.AreEqual(0f, _runner.Travel.StartWorldX.Value, 0.01f);
            Assert.AreEqual(0f, _runner.Travel.StartWorldZ.Value, 0.01f);
        }

        // ─── Helpers ─────────────────────────────────────────────────

        private void TickN(int n)
        {
            for (int i = 0; i < n; i++)
                _sim.Tick();
        }
    }
}
