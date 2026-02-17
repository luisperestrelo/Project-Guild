using System.Linq;
using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class GameSimulationTests
    {
        [Test]
        public void StartNewGame_CreatesThreeRunners()
        {
            var sim = new GameSimulation();
            sim.StartNewGame("hub");

            Assert.AreEqual(3, sim.CurrentGameState.Runners.Count);
        }

        [Test]
        public void StartNewGame_RunnersAreAtHub()
        {
            var sim = new GameSimulation();
            sim.StartNewGame("hub");

            foreach (var runner in sim.CurrentGameState.Runners)
            {
                Assert.AreEqual("hub", runner.CurrentNodeId);
                Assert.AreEqual(RunnerState.Idle, runner.State);
            }
        }

        [Test]
        public void StartNewGame_WithDefinitions_UsesExactStats()
        {
            var sim = new GameSimulation();
            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Custom Guy" }
                    .WithSkill(SkillType.Melee, 50, passion: true),
            };
            sim.StartNewGame(defs, hubNodeId: "hub");

            Assert.AreEqual(1, sim.CurrentGameState.Runners.Count);
            Assert.AreEqual("Custom Guy", sim.CurrentGameState.Runners[0].Name);
            Assert.AreEqual(50, sim.CurrentGameState.Runners[0].GetSkill(SkillType.Melee).Level);
            Assert.IsTrue(sim.CurrentGameState.Runners[0].GetSkill(SkillType.Melee).HasPassion);
        }

        [Test]
        public void Tick_IncrementsTickCount()
        {
            var sim = new GameSimulation();
            sim.StartNewGame();

            sim.Tick();
            Assert.AreEqual(1, sim.CurrentGameState.TickCount);

            sim.Tick();
            Assert.AreEqual(2, sim.CurrentGameState.TickCount);
        }

        [Test]
        public void Tick_AccumulatesTime()
        {
            var sim = new GameSimulation(tickRate: 10f);
            sim.StartNewGame();

            sim.Tick(); // 0.1 sec
            sim.Tick(); // 0.2 sec

            Assert.AreEqual(0.2f, sim.CurrentGameState.TotalTimeElapsed, 0.001f);
        }

        [Test]
        public void CommandTravel_SetsRunnerToTraveling()
        {
            var sim = new GameSimulation();
            sim.StartNewGame("hub");

            var runner = sim.CurrentGameState.Runners[0];
            sim.CommandTravel(runner.Id, "mine", 10f);

            Assert.AreEqual(RunnerState.Traveling, runner.State);
            Assert.IsNotNull(runner.Travel);
            Assert.AreEqual("hub", runner.Travel.FromNodeId);
            Assert.AreEqual("mine", runner.Travel.ToNodeId);
            Assert.AreEqual(10f, runner.Travel.TotalDistance);
        }

        [Test]
        public void Travel_ProgressesOverTicks()
        {
            var sim = new GameSimulation(tickRate: 10f);
            sim.StartNewGame("hub");

            var runner = sim.CurrentGameState.Runners[0];
            sim.CommandTravel(runner.Id, "mine", 10f);

            float initialProgress = runner.Travel.DistanceCovered;
            sim.Tick();

            Assert.Greater(runner.Travel.DistanceCovered, initialProgress);
        }

        [Test]
        public void Travel_CompletesAndSetsIdle()
        {
            var sim = new GameSimulation(tickRate: 10f);
            sim.StartNewGame("hub");

            var runner = sim.CurrentGameState.Runners[0];
            // Very short distance so it completes in a few ticks
            sim.CommandTravel(runner.Id, "mine", 0.05f);

            sim.Tick();

            Assert.AreEqual(RunnerState.Idle, runner.State);
            Assert.AreEqual("mine", runner.CurrentNodeId);
            Assert.IsNull(runner.Travel);
        }

        [Test]
        public void Travel_HigherAthletics_FasterTravel()
        {
            var sim = new GameSimulation(tickRate: 10f);
            // Create two runners with different Athletics levels
            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Slow" }
                    .WithSkill(SkillType.Athletics, 1),
                new RunnerFactory.RunnerDefinition { Name = "Fast" }
                    .WithSkill(SkillType.Athletics, 50),
            };
            sim.StartNewGame(defs, hubNodeId: "hub");

            var slowRunner = sim.CurrentGameState.Runners[0];
            var fastRunner = sim.CurrentGameState.Runners[1];

            sim.CommandTravel(slowRunner.Id, "mine", 100f);
            sim.CommandTravel(fastRunner.Id, "mine", 100f);

            sim.Tick();

            Assert.Greater(
                fastRunner.Travel.DistanceCovered,
                slowRunner.Travel.DistanceCovered
            );
        }

        [Test]
        public void Travel_SpeedRespectsConfig()
        {
            var config = new SimulationConfig
            {
                BaseTravelSpeed = 5.0f,
                AthleticsSpeedPerLevel = 0.1f,
            };
            var sim = new GameSimulation(config, tickRate: 10f);
            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Speedy" }
                    .WithSkill(SkillType.Athletics, 1),
            };
            sim.StartNewGame(defs, hubNodeId: "hub");

            var runner = sim.CurrentGameState.Runners[0];
            sim.CommandTravel(runner.Id, "mine", 100f);
            sim.Tick();

            // At Athletics 1, speed = BaseTravelSpeed + (1-1)*0.1 = 5.0
            // In one tick (0.1s): distance = 5.0 * 0.1 = 0.5
            Assert.AreEqual(0.5f, runner.Travel.DistanceCovered, 0.001f);
        }

        [Test]
        public void EventBus_PublishesRunnerCreatedOnNewGame()
        {
            var sim = new GameSimulation();
            int createdCount = 0;
            sim.Events.Subscribe<RunnerCreated>(e => createdCount++);

            sim.StartNewGame();

            Assert.AreEqual(3, createdCount);
        }

        [Test]
        public void EventBus_PublishesArrivedEventOnTravelComplete()
        {
            var sim = new GameSimulation(tickRate: 10f);
            sim.StartNewGame("hub");

            string arrivedNodeId = null;
            sim.Events.Subscribe<RunnerArrivedAtNode>(e => arrivedNodeId = e.NodeId);

            var runner = sim.CurrentGameState.Runners[0];
            sim.CommandTravel(runner.Id, "mine", 0.01f);
            sim.Tick();

            Assert.AreEqual("mine", arrivedNodeId);
        }

        // ─── Map-based travel ────────────────────────────────────────

        [Test]
        public void CommandTravel_MapBased_UsesMapDistance()
        {
            var sim = new GameSimulation(tickRate: 10f);
            sim.StartNewGame("hub");

            var runner = sim.CurrentGameState.Runners[0];
            float expectedDist = sim.CurrentGameState.Map.GetDirectDistance("hub", "copper_mine");

            bool started = sim.CommandTravel(runner.Id, "copper_mine");

            Assert.IsTrue(started);
            Assert.AreEqual(RunnerState.Traveling, runner.State);
            Assert.AreEqual(expectedDist, runner.Travel.TotalDistance);
        }

        [Test]
        public void CommandTravel_MapBased_AlreadyAtNode_ReturnsFalse()
        {
            var sim = new GameSimulation();
            sim.StartNewGame("hub");

            var runner = sim.CurrentGameState.Runners[0];
            bool started = sim.CommandTravel(runner.Id, "hub");

            Assert.IsFalse(started);
            Assert.AreEqual(RunnerState.Idle, runner.State);
        }

        [Test]
        public void StartNewGame_CreatesWorldMap()
        {
            var sim = new GameSimulation();
            sim.StartNewGame("hub");

            Assert.IsNotNull(sim.CurrentGameState.Map);
            Assert.IsNotNull(sim.CurrentGameState.Map.GetNode("hub"));
        }

        // ─── FindMatchingGatherLoop / Sequence Reuse ──────────────────

        private static GameSimulation CreateSimWithMap()
        {
            var sim = new GameSimulation(tickRate: 10f);
            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Guild Hall");
            map.AddNode("mine", "Copper Mine", 10f, 0f);
            map.AddNode("forest", "Pine Forest", 0f, 10f);
            map.AddEdge("hub", "mine", 10f);
            map.AddEdge("hub", "forest", 10f);
            map.Initialize();

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Alpha" }
                    .WithSkill(SkillType.Mining, 1),
            };
            sim.StartNewGame(defs, map, "hub");
            return sim;
        }

        [Test]
        public void FindMatchingGatherLoop_ReturnsNullWhenLibraryEmpty()
        {
            var sim = CreateSimWithMap();

            var result = sim.FindMatchingGatherLoop("mine", "hub");

            Assert.IsNull(result);
        }

        [Test]
        public void FindMatchingGatherLoop_MatchesStandardLoop()
        {
            var sim = CreateSimWithMap();
            var loop = TaskSequence.CreateLoop("mine", "hub");
            sim.CurrentGameState.TaskSequenceLibrary.Add(loop);

            var result = sim.FindMatchingGatherLoop("mine", "hub");

            Assert.IsNotNull(result);
            Assert.AreEqual(loop.Id, result.Id);
        }

        [Test]
        public void FindMatchingGatherLoop_NoMatchDifferentNode()
        {
            var sim = CreateSimWithMap();
            var loop = TaskSequence.CreateLoop("mine", "hub");
            sim.CurrentGameState.TaskSequenceLibrary.Add(loop);

            var result = sim.FindMatchingGatherLoop("forest", "hub");

            Assert.IsNull(result);
        }

        [Test]
        public void FindMatchingGatherLoop_NoMatchDifferentMicro()
        {
            var sim = CreateSimWithMap();
            var loop = TaskSequence.CreateLoop("mine", "hub", microRulesetId: "custom-micro");
            sim.CurrentGameState.TaskSequenceLibrary.Add(loop);

            var result = sim.FindMatchingGatherLoop("mine", "hub");

            Assert.IsNull(result);
        }

        [Test]
        public void FindMatchingGatherLoop_NoMatchNonLooping()
        {
            var sim = CreateSimWithMap();
            var loop = TaskSequence.CreateLoop("mine", "hub");
            loop.Loop = false;
            sim.CurrentGameState.TaskSequenceLibrary.Add(loop);

            var result = sim.FindMatchingGatherLoop("mine", "hub");

            Assert.IsNull(result);
        }

        [Test]
        public void CommandWorkAtSuspendMacros_ReusesExistingSequence()
        {
            var sim = CreateSimWithMap();
            var runner = sim.CurrentGameState.Runners[0];

            // First Work At creates a new sequence
            sim.CommandWorkAtSuspendMacrosForOneCycle(runner.Id, "mine");
            int libraryCountAfterFirst = sim.CurrentGameState.TaskSequenceLibrary.Count;
            string firstSeqId = runner.TaskSequenceId;

            // Second Work At at same node should reuse
            sim.CommandWorkAtSuspendMacrosForOneCycle(runner.Id, "mine");
            int libraryCountAfterSecond = sim.CurrentGameState.TaskSequenceLibrary.Count;

            Assert.AreEqual(libraryCountAfterFirst, libraryCountAfterSecond);
            Assert.AreEqual(firstSeqId, runner.TaskSequenceId);
        }

        [Test]
        public void CommandWorkAtSuspendMacros_DifferentNode_CreatesNewSequence()
        {
            var sim = CreateSimWithMap();
            var runner = sim.CurrentGameState.Runners[0];

            sim.CommandWorkAtSuspendMacrosForOneCycle(runner.Id, "mine");
            int libraryCountAfterFirst = sim.CurrentGameState.TaskSequenceLibrary.Count;
            string firstSeqId = runner.TaskSequenceId;

            sim.CommandWorkAtSuspendMacrosForOneCycle(runner.Id, "forest");
            int libraryCountAfterSecond = sim.CurrentGameState.TaskSequenceLibrary.Count;

            Assert.AreEqual(libraryCountAfterFirst + 1, libraryCountAfterSecond);
            Assert.AreNotEqual(firstSeqId, runner.TaskSequenceId);
        }
    }
}
