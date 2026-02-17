using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for Runner.ActiveWarning: set on GatheringFailed/NoMicroRuleMatched,
    /// cleared on productive state transitions (travel, gathering, assign, deposit).
    /// </summary>
    [TestFixture]
    public class WarningBadgeTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f);

        private static readonly Simulation.Gathering.GatherableConfig HighLevelGatherable =
            new Simulation.Gathering.GatherableConfig("mithril_ore", SkillType.Mining, 40f, 0.5f)
            { MinLevel = 50 };

        private void Setup(string startNodeId = "hub")
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("mithril_ore", "Mithril Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Alpha" }
                    .WithSkill(SkillType.Mining, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Guild Hall");
            map.AddNode("mine", "Copper Mine", 10f, 0f, CopperGatherable);
            map.AddNode("hard-mine", "Mithril Mine", 0f, 10f, HighLevelGatherable);
            map.AddNode("empty", "Empty Field", 20f, 0f);
            map.AddEdge("hub", "mine", 10f);
            map.AddEdge("hub", "hard-mine", 10f);
            map.AddEdge("hub", "empty", 10f);
            map.Initialize();

            _sim.StartNewGame(defs, map, startNodeId);
            _runner = _sim.CurrentGameState.Runners[0];
        }

        [Test]
        public void ActiveWarning_NullByDefault()
        {
            Setup();

            Assert.IsNull(_runner.ActiveWarning);
        }

        [Test]
        public void ActiveWarning_SetOnGatheringFailed_NoGatherablesAtNode()
        {
            Setup("empty");

            // Runner at empty node, assign a work sequence targeting empty
            var seq = TaskSequence.CreateLoop("empty", "hub");
            _sim.AssignRunner(_runner.Id, seq, "Test");

            // Tick enough for runner to hit the Work step (already at empty, so step 0 = TravelTo completes instantly or is skipped)
            for (int i = 0; i < 5; i++) _sim.Tick();

            Assert.IsNotNull(_runner.ActiveWarning);
            Assert.That(_runner.ActiveWarning, Does.Contain("No gatherables"));
        }

        [Test]
        public void ActiveWarning_SetOnGatheringFailed_NotEnoughSkill()
        {
            Setup("hard-mine");

            // Runner at hard-mine (requires level 50, runner has level 1)
            var seq = TaskSequence.CreateLoop("hard-mine", "hub");
            _sim.AssignRunner(_runner.Id, seq, "Test");

            for (int i = 0; i < 5; i++) _sim.Tick();

            Assert.IsNotNull(_runner.ActiveWarning);
            Assert.That(_runner.ActiveWarning, Does.Contain("level too low"));
        }

        [Test]
        public void ActiveWarning_SetOnNoMicroRuleMatched()
        {
            Setup("mine");

            // Create a sequence with an empty micro ruleset
            var emptyMicro = new Ruleset
            {
                Id = "empty-micro",
                Name = "Empty Micro",
                Category = RulesetCategory.Gathering,
            };
            _sim.CurrentGameState.MicroRulesetLibrary.Add(emptyMicro);

            var seq = TaskSequence.CreateLoop("mine", "hub", microRulesetId: "empty-micro");
            _sim.AssignRunner(_runner.Id, seq, "Test");

            for (int i = 0; i < 5; i++) _sim.Tick();

            Assert.IsNotNull(_runner.ActiveWarning);
            Assert.That(_runner.ActiveWarning, Does.Contain("No micro rules configured"));
        }

        [Test]
        public void ActiveWarning_ClearedOnAssignRunner()
        {
            Setup();
            _runner.ActiveWarning = "Test warning";

            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq, "Test");

            Assert.IsNull(_runner.ActiveWarning);
        }

        [Test]
        public void ActiveWarning_ClearedOnTravel()
        {
            Setup();
            _runner.ActiveWarning = "Test warning";

            _sim.CommandTravel(_runner.Id, "mine");

            Assert.IsNull(_runner.ActiveWarning);
        }

        [Test]
        public void ActiveWarning_ClearedWhenRunnerStartsGathering()
        {
            Setup("mine");

            // Inject a warning before assigning. AssignRunner → ExecuteCurrentStep →
            // TravelTo(mine, already there) → Work → StartGathering all chain in one call.
            // Both AssignRunner and StartGathering clear warnings.
            _runner.ActiveWarning = "Stuck warning";
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq, "Test");

            Assert.AreEqual(RunnerState.Gathering, _runner.State,
                "Runner should be gathering immediately (already at mine)");
            Assert.IsNull(_runner.ActiveWarning);
        }
    }
}
