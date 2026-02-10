using NUnit.Framework;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class GatheringTests
    {
        private GameSimulation _sim;
        private Runner _runner;

        /// <summary>
        /// Helper: create a sim with one runner at a copper mine, ready to gather.
        /// </summary>
        private void SetupRunnerAtMine(int miningLevel = 1, bool passion = false)
        {
            var config = new SimulationConfig();
            _sim = new GameSimulation(config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Miner" }
                    .WithSkill(SkillType.Mining, miningLevel, passion),
            };

            var map = new WorldMap();
            map.AddNode("hub", "Hub", NodeType.Hub);
            map.AddNode("mine", "Mine", NodeType.GatheringMine);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "mine");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        // ─── CommandGather validation ──────────────────────────────

        [Test]
        public void CommandGather_AtGatheringNode_ReturnsTrue()
        {
            SetupRunnerAtMine();

            bool result = _sim.CommandGather(_runner.Id);

            Assert.IsTrue(result);
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.IsNotNull(_runner.Gathering);
            Assert.AreEqual("mine", _runner.Gathering.NodeId);
        }

        [Test]
        public void CommandGather_NotIdle_ReturnsFalse()
        {
            SetupRunnerAtMine();
            _runner.State = RunnerState.Traveling;

            bool result = _sim.CommandGather(_runner.Id);

            Assert.IsFalse(result);
        }

        [Test]
        public void CommandGather_AtHub_ReturnsFalse()
        {
            var config = new SimulationConfig();
            _sim = new GameSimulation(config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Miner" },
            };

            var map = new WorldMap();
            map.AddNode("hub", "Hub", NodeType.Hub);
            map.Initialize();

            _sim.StartNewGame(defs, map, "hub");
            _runner = _sim.CurrentGameState.Runners[0];

            bool result = _sim.CommandGather(_runner.Id);

            Assert.IsFalse(result);
        }

        [Test]
        public void CommandGather_PublishesGatheringStarted()
        {
            SetupRunnerAtMine();

            GatheringStarted? received = null;
            _sim.Events.Subscribe<GatheringStarted>(e => received = e);

            _sim.CommandGather(_runner.Id);

            Assert.IsNotNull(received);
            Assert.AreEqual(_runner.Id, received.Value.RunnerId);
            Assert.AreEqual("mine", received.Value.NodeId);
            Assert.AreEqual("copper_ore", received.Value.ItemId);
            Assert.AreEqual(SkillType.Mining, received.Value.Skill);
        }

        // ─── Item production ───────────────────────────────────────

        [Test]
        public void Gathering_ProducesItem_AfterEnoughTicks()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // Default config: BaseTicksToGather = 10, level 1, no speed bonus
            // So it should take 10 ticks to produce one item
            for (int i = 0; i < 9; i++)
                _sim.Tick();

            Assert.AreEqual(0, _runner.Inventory.CountItem("copper_ore"),
                "Should not have produced an item yet after 9 ticks");

            _sim.Tick(); // 10th tick

            Assert.AreEqual(1, _runner.Inventory.CountItem("copper_ore"),
                "Should have produced 1 item after 10 ticks");
        }

        [Test]
        public void Gathering_ProducesItem_PublishesEvent()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            ItemGathered? received = null;
            _sim.Events.Subscribe<ItemGathered>(e => received = e);

            // Tick until item produced (10 ticks at level 1)
            for (int i = 0; i < 10; i++)
                _sim.Tick();

            Assert.IsNotNull(received);
            Assert.AreEqual("copper_ore", received.Value.ItemId);
        }

        [Test]
        public void Gathering_ContinuesProducing_MultipleItems()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // 30 ticks = 3 items at 10 ticks each
            for (int i = 0; i < 30; i++)
                _sim.Tick();

            Assert.AreEqual(3, _runner.Inventory.CountItem("copper_ore"));
        }

        // ─── XP awards ────────────────────────────────────────────

        [Test]
        public void Gathering_AwardsXp_OnProduction()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            float xpBefore = _runner.GetSkill(SkillType.Mining).Xp;

            // Produce one item
            for (int i = 0; i < 10; i++)
                _sim.Tick();

            float xpAfter = _runner.GetSkill(SkillType.Mining).Xp;
            Assert.Greater(xpAfter, xpBefore);
        }

        [Test]
        public void Gathering_XpMatchesConfig()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // Produce one item — should award BaseXpPerGather (25 for copper)
            for (int i = 0; i < 10; i++)
                _sim.Tick();

            // At level 1 no passion, XP = BaseXpPerGather = 25
            Assert.AreEqual(25f, _runner.GetSkill(SkillType.Mining).Xp, 0.01f);
        }

        [Test]
        public void Gathering_LevelUp_PublishesEvent()
        {
            // Start at a level where we're close to leveling up
            SetupRunnerAtMine(miningLevel: 1);

            // Manually set XP close to level-up threshold so gathering pushes us over
            var skill = _runner.GetSkill(SkillType.Mining);
            float xpToNext = skill.GetXpToNextLevel(_sim.Config);
            // Set XP to just under the threshold so one gather (25 XP) levels us up
            skill.Xp = xpToNext - 1f;

            _sim.CommandGather(_runner.Id);

            RunnerSkillLeveledUp? received = null;
            _sim.Events.Subscribe<RunnerSkillLeveledUp>(e => received = e);

            for (int i = 0; i < 10; i++)
                _sim.Tick();

            Assert.IsNotNull(received);
            Assert.AreEqual(SkillType.Mining, received.Value.Skill);
            Assert.AreEqual(2, received.Value.NewLevel);
        }

        // ─── Skill speed scaling ───────────────────────────────────

        [Test]
        public void Gathering_HigherLevel_FasterGathering()
        {
            // Level 1 runner
            SetupRunnerAtMine(miningLevel: 1);
            _sim.CommandGather(_runner.Id);

            int ticksForLevel1 = 0;
            while (_runner.Inventory.CountItem("copper_ore") < 1)
            {
                _sim.Tick();
                ticksForLevel1++;
                if (ticksForLevel1 > 100) break; // safety
            }

            // Level 50 runner
            SetupRunnerAtMine(miningLevel: 50);
            _sim.CommandGather(_runner.Id);

            int ticksForLevel50 = 0;
            while (_runner.Inventory.CountItem("copper_ore") < 1)
            {
                _sim.Tick();
                ticksForLevel50++;
                if (ticksForLevel50 > 100) break;
            }

            Assert.Less(ticksForLevel50, ticksForLevel1,
                "Higher mining level should gather faster");
        }

        [Test]
        public void Gathering_TicksRequired_MatchesPowerCurveFormula()
        {
            SetupRunnerAtMine(miningLevel: 10);
            _sim.CommandGather(_runner.Id);

            // Default formula is PowerCurve with exponent 0.55.
            // speedMultiplier = effectiveLevel ^ exponent = 10 ^ 0.55 ≈ 3.548
            // ticksRequired = (1.0 * 10) / 3.548 ≈ 2.819
            var config = new SimulationConfig();
            float speedMultiplier = (float)System.Math.Pow(10f, config.GatheringSpeedExponent);
            float expected = (config.GlobalGatheringSpeedMultiplier * 10f) / speedMultiplier;

            Assert.AreEqual(expected, _runner.Gathering.TicksRequired, 0.01f);
        }

        [Test]
        public void Gathering_PassionBoost_FasterGathering()
        {
            // Without passion
            SetupRunnerAtMine(miningLevel: 10, passion: false);
            _sim.CommandGather(_runner.Id);
            float ticksNormal = _runner.Gathering.TicksRequired;

            // With passion
            SetupRunnerAtMine(miningLevel: 10, passion: true);
            _sim.CommandGather(_runner.Id);
            float ticksPassion = _runner.Gathering.TicksRequired;

            Assert.Less(ticksPassion, ticksNormal,
                "Passion should increase effective level, resulting in fewer ticks");
        }

        // ─── Inventory full → auto-return ─────────────────────────

        [Test]
        public void Gathering_InventoryFull_PublishesEvent()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            InventoryFull? received = null;
            _sim.Events.Subscribe<InventoryFull>(e => received = e);

            // 28 items * 10 ticks each = 280 ticks to fill inventory
            for (int i = 0; i < 280; i++)
                _sim.Tick();

            Assert.IsNotNull(received);
            Assert.AreEqual(28, _runner.Inventory.CountItem("copper_ore"));
        }

        [Test]
        public void Gathering_InventoryFull_StartsTravelingToHub()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // Fill inventory
            for (int i = 0; i < 280; i++)
                _sim.Tick();

            Assert.AreEqual(RunnerState.Traveling, _runner.State,
                "Runner should start traveling to hub after inventory fills");
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
            Assert.IsNotNull(_runner.Gathering);
            Assert.AreEqual(GatheringSubState.TravelingToBank, _runner.Gathering.SubState);
        }

        [Test]
        public void AutoReturn_DepositsAtHub_ThenReturnsToNode()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);

            // Fill inventory (280 ticks) + generous travel time to hub and back
            // Mine-to-hub distance is 8, base speed 1.0, tick = 0.1s → ~80 ticks each way
            for (int i = 0; i < 280 + 100; i++)
                _sim.Tick();

            // Should have deposited by now
            Assert.IsNotNull(deposited, "Runner should have deposited at hub");
            Assert.AreEqual(28, deposited.Value.ItemsDeposited);

            // Bank should have the items
            Assert.AreEqual(28, _sim.CurrentGameState.Bank.CountItem("copper_ore"));

            // Inventory should be empty after deposit
            Assert.AreEqual(0, _runner.Inventory.CountItem("copper_ore"));

            // Runner should be heading back to the mine (or already there gathering)
            Assert.IsTrue(
                _runner.Gathering.SubState == GatheringSubState.TravelingToNode ||
                _runner.Gathering.SubState == GatheringSubState.Gathering,
                "Runner should be heading back or already gathering again");
        }

        [Test]
        public void AutoReturn_FullLoop_ResumesGathering()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // Run enough ticks for: fill inventory (280) + travel to hub (~80) + travel back (~80) + some gathering
            for (int i = 0; i < 500; i++)
                _sim.Tick();

            // Runner should be back at the mine and gathering again
            Assert.AreEqual("mine", _runner.CurrentNodeId);
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(GatheringSubState.Gathering, _runner.Gathering.SubState);

            // Bank should have the first batch
            Assert.AreEqual(28, _sim.CurrentGameState.Bank.CountItem("copper_ore"));

            // Inventory should have some new items from resumed gathering
            Assert.Greater(_runner.Inventory.CountItem("copper_ore"), 0,
                "Runner should have started producing items again after returning");
        }

        [Test]
        public void AutoReturn_MultipleLoops_AccumulatesInBank()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // Run enough for 2+ full loops: 2 * (280 gather + ~160 travel) + some extra
            for (int i = 0; i < 1200; i++)
                _sim.Tick();

            // Bank should have at least 2 batches worth (56+ items)
            Assert.GreaterOrEqual(_sim.CurrentGameState.Bank.CountItem("copper_ore"), 56,
                "Bank should have accumulated items from multiple auto-return loops");
        }

        [Test]
        public void AutoReturn_GatheringStartedEvent_FiredOnResume()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            int gatheringStartedCount = 0;
            _sim.Events.Subscribe<GatheringStarted>(e => gatheringStartedCount++);

            // Full loop: fill (280) + travel to hub (~80) + travel back (~80) + resume
            for (int i = 0; i < 500; i++)
                _sim.Tick();

            // Should have fired at least once for the resume (the initial CommandGather
            // was before we subscribed, so this only catches the resume event)
            Assert.GreaterOrEqual(gatheringStartedCount, 1,
                "GatheringStarted should fire when gathering resumes after auto-return");
        }
    }
}
