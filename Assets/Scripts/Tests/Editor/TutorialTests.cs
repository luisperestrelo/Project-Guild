using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.Tutorial;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class TutorialTests
    {
        private GameSimulation _sim;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new("copper_ore", SkillType.Mining, 40f, 0.5f);

        [SetUp]
        public void SetUp()
        {
            _config = new SimulationConfig
            {
                BaseTravelSpeed = 100f,
                AthleticsSpeedPerLevel = 0f,
                DepositDurationTicks = 1,
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                },
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Guild Hall");
            map.AddNode("copper_mine", "Copper Mine", 0f, 0f, null, CopperGatherable);
            map.AddNode("pine_forest", "Pine Forest");
            map.AddNode("sunlit_pond", "Sunlit Pond");
            map.AddNode("herb_garden", "Herb Garden");
            map.AddEdge("hub", "copper_mine", 5f);
            map.AddEdge("hub", "pine_forest", 5f);
            map.AddEdge("hub", "sunlit_pond", 5f);
            map.AddEdge("hub", "herb_garden", 5f);
            map.Initialize();

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Alpha" }
                    .WithSkill(SkillType.Mining, 5),
                new RunnerFactory.RunnerDefinition { Name = "Beta" }
                    .WithSkill(SkillType.Mining, 5),
                new RunnerFactory.RunnerDefinition { Name = "Gamma" }
                    .WithSkill(SkillType.Mining, 5),
            };

            _sim = new GameSimulation(_config, tickRate: 10f);
            _sim.StartNewGame(defs, map: map, hubNodeId: "hub");
        }

        [Test]
        public void NewGame_TutorialIsActive()
        {
            Assert.IsTrue(_sim.CurrentGameState.Tutorial.IsActive);
            Assert.AreEqual(TutorialPhase.Gathering, _sim.CurrentGameState.Tutorial.CurrentPhase);
        }

        [Test]
        public void NewGame_IntroMilestoneCompleted()
        {
            Assert.IsTrue(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_Intro));
        }

        [Test]
        public void NewGame_DiscoveredNodesIncludeHubAndGatheringNodes()
        {
            var discovered = _sim.CurrentGameState.Tutorial.DiscoveredNodeIds;
            Assert.Contains("hub", discovered);
            Assert.Contains("copper_mine", discovered);
            Assert.Contains("pine_forest", discovered);
            Assert.Contains("sunlit_pond", discovered);
            Assert.Contains("herb_garden", discovered);
            Assert.AreEqual(5, discovered.Count);
        }

        [Test]
        public void SendRunnerToNode_CompletesSentMilestone()
        {
            var runner = _sim.CurrentGameState.Runners[0];
            _sim.CommandWorkAtSuspendMacrosForOneCycle(runner.Id, "copper_mine");

            Assert.IsTrue(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_SentRunnerToNode));
        }

        [Test]
        public void TutorialDisabled_NoMilestonesTracked()
        {
            // Disable tutorial
            _sim.Tutorial.SkipTutorial();
            Assert.IsFalse(_sim.CurrentGameState.Tutorial.IsActive);

            // Clear existing milestones for a clean test
            _sim.CurrentGameState.Tutorial.CompletedMilestones.Clear();

            // Send runner — should not complete the sent milestone
            var runner = _sim.CurrentGameState.Runners[0];
            _sim.CommandWorkAtSuspendMacrosForOneCycle(runner.Id, "copper_mine");

            Assert.IsFalse(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_SentRunnerToNode));
        }

        [Test]
        public void IdleNudge_FiresAfterDelayWithIdleRunners()
        {
            // Send one runner away — leaves 2 idle at hub
            var runner = _sim.CurrentGameState.Runners[0];
            _sim.CommandWorkAtSuspendMacrosForOneCycle(runner.Id, "copper_mine");

            Assert.IsTrue(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_SentRunnerToNode));
            Assert.IsFalse(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_IdleNudgeShown));

            // Tick for 14 seconds (140 ticks at 10/sec) — should NOT fire yet
            for (int i = 0; i < 140; i++)
                _sim.Tick();

            Assert.IsFalse(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_IdleNudgeShown));

            // Tick for 2 more seconds (20 ticks) — now at 16 seconds, should fire
            for (int i = 0; i < 20; i++)
                _sim.Tick();

            Assert.IsTrue(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_IdleNudgeShown));
        }

        [Test]
        public void CopperDeposit_Under20_DoesNotComplete()
        {
            // Directly put 19 copper in the bank and simulate a deposit event
            _sim.CurrentGameState.Bank.Deposit("copper_ore", 19);
            _sim.Events.Publish(new RunnerDeposited
            {
                RunnerId = _sim.CurrentGameState.Runners[0].Id,
                ItemsDeposited = 19,
            });

            Assert.IsFalse(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_CopperDeposited));
        }

        [Test]
        public void CopperDeposit_Over20_CompletesPhase()
        {
            // Put 20 copper in the bank and fire a deposit event
            _sim.CurrentGameState.Bank.Deposit("copper_ore", 20);
            _sim.Events.Publish(new RunnerDeposited
            {
                RunnerId = _sim.CurrentGameState.Runners[0].Id,
                ItemsDeposited = 20,
            });

            Assert.IsTrue(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_CopperDeposited));
            Assert.IsTrue(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_Complete));
            Assert.AreEqual(TutorialPhase.Crafting, _sim.CurrentGameState.Tutorial.CurrentPhase);
        }

        [Test]
        public void CompleteMilestone_IsIdempotent()
        {
            // Intro is already completed during StartNewGame
            Assert.IsTrue(_sim.CurrentGameState.Tutorial.IsMilestoneCompleted(
                TutorialMilestones.Gathering_Intro));

            // Track events — completing again should not publish another event
            int milestoneEventCount = 0;
            _sim.Events.Subscribe<TutorialMilestoneCompleted>(e => milestoneEventCount++);

            // Force complete the same milestone again
            _sim.Tutorial.ForceCompleteMilestone(TutorialMilestones.Gathering_Intro);

            Assert.AreEqual(0, milestoneEventCount);
        }
    }
}
