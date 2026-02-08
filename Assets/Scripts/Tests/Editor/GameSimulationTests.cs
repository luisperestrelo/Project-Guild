using NUnit.Framework;
using ProjectGuild.Simulation.Core;

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

            Assert.AreEqual(3, sim.State.Runners.Count);
        }

        [Test]
        public void StartNewGame_RunnersAreAtHub()
        {
            var sim = new GameSimulation();
            sim.StartNewGame("hub");

            foreach (var runner in sim.State.Runners)
            {
                Assert.AreEqual("hub", runner.CurrentNodeId);
                Assert.AreEqual(RunnerState.Idle, runner.State);
            }
        }

        [Test]
        public void Tick_IncrementsTickCount()
        {
            var sim = new GameSimulation();
            sim.StartNewGame();

            sim.Tick();
            Assert.AreEqual(1, sim.State.TickCount);

            sim.Tick();
            Assert.AreEqual(2, sim.State.TickCount);
        }

        [Test]
        public void Tick_AccumulatesTime()
        {
            var sim = new GameSimulation(tickRate: 10f);
            sim.StartNewGame();

            sim.Tick(); // 0.1 sec
            sim.Tick(); // 0.2 sec

            Assert.AreEqual(0.2f, sim.State.TotalTimeElapsed, 0.001f);
        }

        [Test]
        public void CommandTravel_SetsRunnerToTraveling()
        {
            var sim = new GameSimulation();
            sim.StartNewGame("hub");

            var runner = sim.State.Runners[0];
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

            var runner = sim.State.Runners[0];
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

            var runner = sim.State.Runners[0];
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
            sim.StartNewGame("hub");

            var slowRunner = sim.State.Runners[0];
            slowRunner.Skills[(int)SkillType.Athletics].Level = 1;

            var fastRunner = sim.State.Runners[1];
            fastRunner.Skills[(int)SkillType.Athletics].Level = 50;

            sim.CommandTravel(slowRunner.Id, "mine", 100f);
            sim.CommandTravel(fastRunner.Id, "mine", 100f);

            sim.Tick();

            Assert.Greater(
                fastRunner.Travel.DistanceCovered,
                slowRunner.Travel.DistanceCovered
            );
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

            var runner = sim.State.Runners[0];
            sim.CommandTravel(runner.Id, "mine", 0.01f);
            sim.Tick();

            Assert.AreEqual("mine", arrivedNodeId);
        }
    }
}
