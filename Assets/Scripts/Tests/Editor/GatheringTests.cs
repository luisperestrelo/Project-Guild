using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
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
        private SimulationConfig _config;

        /// <summary>
        /// Helper: create a sim with one runner at a copper mine, ready to gather.
        /// Stores the config so tests can derive expected values from it.
        /// </summary>
        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f);

        private void SetupRunnerAtMine(int miningLevel = 1, bool passion = false)
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Miner" }
                    .WithSkill(SkillType.Mining, miningLevel, passion),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f, CopperGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "mine");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        /// <summary>
        /// Calculate how many ticks it takes to gather one item, matching the sim's formula.
        /// </summary>
        private int TicksPerItem(float effectiveLevel)
        {
            var gatherable = CopperGatherable;
            float baseTicks = _config.GlobalGatheringSpeedMultiplier * gatherable.BaseTicksToGather;

            float speedMultiplier = _config.GatheringFormula switch
            {
                GatheringSpeedFormula.PowerCurve =>
                    (float)System.Math.Pow(effectiveLevel, _config.GatheringSpeedExponent),
                GatheringSpeedFormula.Hyperbolic =>
                    1f + (effectiveLevel - 1f) * _config.HyperbolicSpeedPerLevel,
                _ => 1f,
            };

            return (int)System.Math.Ceiling(baseTicks / System.Math.Max(speedMultiplier, 0.01f));
        }

        /// <summary>
        /// Tick the sim until the runner's inventory is full, or until the safety limit is hit.
        /// Can't precompute this because the runner levels up mid-gather, changing speed.
        /// Returns the number of ticks executed.
        /// </summary>
        private int TickUntilInventoryFull(int safetyLimit = 5000)
        {
            int ticks = 0;
            while (_runner.Inventory.FreeSlots > 0
                && _runner.State == RunnerState.Gathering
                && ticks < safetyLimit)
            {
                _sim.Tick();
                ticks++;
            }
            return ticks;
        }

        /// <summary>
        /// Tick the sim until a condition is met, or until the safety limit is hit.
        /// </summary>
        private int TickUntil(System.Func<bool> condition, int safetyLimit = 10000)
        {
            int ticks = 0;
            while (!condition() && ticks < safetyLimit)
            {
                _sim.Tick();
                ticks++;
            }
            return ticks;
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
            _config = new SimulationConfig();
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Miner" },
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
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

        [Test]
        public void CommandGather_CreatesDefaultAssignment()
        {
            SetupRunnerAtMine();

            _sim.CommandGather(_runner.Id);

            Assert.IsNotNull(_runner.Assignment,
                "CommandGather should create a default assignment when none exists");
            Assert.AreEqual(AssignmentType.Gather, _runner.Assignment.Type);
            Assert.AreEqual("mine", _runner.Assignment.TargetNodeId);
        }

        // ─── Item production ───────────────────────────────────────

        [Test]
        public void Gathering_ProducesItem_AfterEnoughTicks()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            int ticksNeeded = TicksPerItem(1f);

            for (int i = 0; i < ticksNeeded - 1; i++)
                _sim.Tick();

            Assert.AreEqual(0, _runner.Inventory.CountItem("copper_ore"),
                "Should not have produced an item yet before the final tick");

            _sim.Tick();

            Assert.AreEqual(1, _runner.Inventory.CountItem("copper_ore"),
                "Should have produced 1 item after enough ticks");
        }

        [Test]
        public void Gathering_ProducesItem_PublishesEvent()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            ItemGathered? received = null;
            _sim.Events.Subscribe<ItemGathered>(e => received = e);

            int ticksNeeded = TicksPerItem(1f);
            for (int i = 0; i < ticksNeeded; i++)
                _sim.Tick();

            Assert.IsNotNull(received);
            Assert.AreEqual("copper_ore", received.Value.ItemId);
        }

        [Test]
        public void Gathering_ContinuesProducing_MultipleItems()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            int ticksFor3Items = TicksPerItem(1f) * 3;
            for (int i = 0; i < ticksFor3Items; i++)
                _sim.Tick();

            Assert.AreEqual(3, _runner.Inventory.CountItem("copper_ore"));
        }

        // ─── XP awards (per-tick, decoupled from item production) ──

        [Test]
        public void Gathering_AwardsXpPerTick()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            float xpBefore = _runner.GetSkill(SkillType.Mining).Xp;

            // A single tick should award XP
            _sim.Tick();

            float xpAfter = _runner.GetSkill(SkillType.Mining).Xp;
            Assert.Greater(xpAfter, xpBefore, "XP should be awarded every tick while gathering");
        }

        [Test]
        public void Gathering_XpPerTick_MatchesConfig()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // Run 5 ticks
            for (int i = 0; i < 5; i++)
                _sim.Tick();

            float xpPerTick = CopperGatherable.XpPerTick;
            float expectedXp = xpPerTick * 5f;
            Assert.AreEqual(expectedXp, _runner.GetSkill(SkillType.Mining).Xp, 0.01f);
        }

        [Test]
        public void Gathering_XpIsDecoupledFromItemProduction()
        {
            // Two runners at different levels gathering the same resource
            // should get the same XP per tick, even though one gathers items faster
            SetupRunnerAtMine(miningLevel: 1);
            _sim.CommandGather(_runner.Id);
            _sim.Tick();
            float xpLevel1 = _runner.GetSkill(SkillType.Mining).Xp;

            SetupRunnerAtMine(miningLevel: 50);
            _sim.CommandGather(_runner.Id);
            _sim.Tick();
            float xpLevel50 = _runner.GetSkill(SkillType.Mining).Xp;

            // Same XP per tick regardless of level (no passion on either)
            Assert.AreEqual(xpLevel1, xpLevel50, 0.01f,
                "XP per tick should be the same regardless of gathering speed");
        }

        [Test]
        public void Gathering_LevelUp_PublishesEvent()
        {
            SetupRunnerAtMine(miningLevel: 1);

            // Set XP close to level-up threshold so a few ticks push us over
            var skill = _runner.GetSkill(SkillType.Mining);
            float xpToNext = skill.GetXpToNextLevel(_sim.Config);
            skill.Xp = xpToNext - 1f;

            _sim.CommandGather(_runner.Id);

            RunnerSkillLeveledUp? received = null;
            _sim.Events.Subscribe<RunnerSkillLeveledUp>(e => received = e);

            // Tick until level-up happens (should be very fast since we're 1 XP away)
            TickUntil(() => received != null, safetyLimit: 100);

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
                if (ticksForLevel1 > 1000) break; // safety
            }

            // Level 50 runner
            SetupRunnerAtMine(miningLevel: 50);
            _sim.CommandGather(_runner.Id);

            int ticksForLevel50 = 0;
            while (_runner.Inventory.CountItem("copper_ore") < 1)
            {
                _sim.Tick();
                ticksForLevel50++;
                if (ticksForLevel50 > 1000) break;
            }

            Assert.Less(ticksForLevel50, ticksForLevel1,
                "Higher mining level should gather faster");
        }

        [Test]
        public void Gathering_TicksRequired_MatchesFormula()
        {
            SetupRunnerAtMine(miningLevel: 10);
            _sim.CommandGather(_runner.Id);

            // TicksPerItem uses the same formula logic as the sim
            float expected = _runner.Gathering.TicksRequired;
            float fromHelper = TicksPerItem(10f);

            // The sim stores the exact float; our helper ceiling-rounds for tick counting.
            // Verify the sim's value is close to our expectation.
            var gatherable = CopperGatherable;
            float baseTicks = _config.GlobalGatheringSpeedMultiplier * gatherable.BaseTicksToGather;
            float speedMultiplier = (float)System.Math.Pow(10f, _config.GatheringSpeedExponent);
            float exactExpected = baseTicks / speedMultiplier;

            Assert.AreEqual(exactExpected, expected, 0.01f);
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

        // ─── Inventory full -> auto-return (assignment-driven) ──────

        [Test]
        public void Gathering_InventoryFull_PublishesEvent()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            InventoryFull? received = null;
            _sim.Events.Subscribe<InventoryFull>(e => received = e);

            // Tick until inventory fills (can't precompute — runner levels up mid-gather)
            TickUntilInventoryFull();

            Assert.IsNotNull(received);
            Assert.AreEqual(_config.InventorySize, _runner.Inventory.CountItem("copper_ore"));
        }

        [Test]
        public void Gathering_InventoryFull_StartsTravelingToHub()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            TickUntilInventoryFull();

            // With assignment-driven logic: inventory full → stop gathering → Idle
            // → AdvanceMacroStep → TravelTo(hub) → Traveling
            Assert.AreEqual(RunnerState.Traveling, _runner.State,
                "Runner should start traveling to hub after inventory fills");
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
        }

        [Test]
        public void AutoReturn_DepositsAtHub_ThenReturnsToNode()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);

            // Tick until the runner has deposited at the hub
            TickUntil(() => deposited != null);

            Assert.IsNotNull(deposited, "Runner should have deposited at hub");
            Assert.AreEqual(_config.InventorySize, deposited.Value.ItemsDeposited);

            // Bank should have the items
            Assert.AreEqual(_config.InventorySize, _sim.CurrentGameState.Bank.CountItem("copper_ore"));

            // Inventory should be empty after deposit
            Assert.AreEqual(0, _runner.Inventory.CountItem("copper_ore"));

            // Runner should be heading back to the mine (or already gathering again)
            Assert.IsTrue(
                _runner.State == RunnerState.Traveling ||
                _runner.State == RunnerState.Gathering,
                "Runner should be heading back or already gathering again");
        }

        [Test]
        public void AutoReturn_FullLoop_ResumesGathering()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // Tick until the runner completes a full loop and is back gathering at the mine
            TickUntil(() =>
                _runner.CurrentNodeId == "mine"
                && _runner.State == RunnerState.Gathering
                && _sim.CurrentGameState.Bank.CountItem("copper_ore") > 0);

            Assert.AreEqual("mine", _runner.CurrentNodeId);
            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Bank should have the first batch
            Assert.AreEqual(_config.InventorySize, _sim.CurrentGameState.Bank.CountItem("copper_ore"));

            // Tick a bit more — runner should produce new items
            int ticksNeeded = TicksPerItem(1f) * 2; // generous: even at level 1 speed, 2 items
            for (int i = 0; i < ticksNeeded; i++)
                _sim.Tick();

            Assert.Greater(_runner.Inventory.CountItem("copper_ore"), 0,
                "Runner should have started producing items again after returning");
        }

        [Test]
        public void AutoReturn_MultipleLoops_AccumulatesInBank()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            // Tick until the bank has at least 2 full batches
            int expectedMinimum = _config.InventorySize * 2;
            TickUntil(() => _sim.CurrentGameState.Bank.CountItem("copper_ore") >= expectedMinimum, safetyLimit: 2000);

            Assert.GreaterOrEqual(_sim.CurrentGameState.Bank.CountItem("copper_ore"), expectedMinimum,
                "Bank should have accumulated items from multiple auto-return loops");
        }

        [Test]
        public void AutoReturn_GatheringStartedEvent_FiredOnResume()
        {
            SetupRunnerAtMine();
            _sim.CommandGather(_runner.Id);

            int gatheringStartedCount = 0;
            _sim.Events.Subscribe<GatheringStarted>(e => gatheringStartedCount++);

            // Tick until gathering resumes after a deposit cycle
            TickUntil(() => gatheringStartedCount >= 1);

            Assert.GreaterOrEqual(gatheringStartedCount, 1,
                "GatheringStarted should fire when gathering resumes after auto-return");
        }
    }
}
