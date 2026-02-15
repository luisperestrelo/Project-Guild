using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class EventLogServiceTests
    {
        private GameState _state;

        private EventLogService CreateService(int maxEntries = 100)
        {
            _state = new GameState { TickCount = 1 };
            return new EventLogService(maxEntries, () => _state);
        }

        private EventLogEntry MakeEntry(
            EventCategory category = EventCategory.StateChange,
            string runnerId = "r1",
            string summary = "test",
            string collapseKey = null)
        {
            return new EventLogEntry
            {
                TickNumber = _state.TickCount,
                Category = category,
                RunnerId = runnerId,
                Summary = summary,
                CollapseKey = collapseKey,
            };
        }

        // ─── Basic add/retrieve ───────────────────────────────────

        [Test]
        public void Add_StoresEntry()
        {
            var svc = CreateService();
            svc.Add(MakeEntry(summary: "hello"));

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual("hello", svc.Entries[0].Summary);
            Assert.AreEqual(1, svc.Entries[0].RepeatCount);
        }

        // ─── Collapsing ──────────────────────────────────────────

        [Test]
        public void Collapsing_SameKey_IncrementsCount()
        {
            var svc = CreateService();
            svc.CollapsingEnabled = true;
            svc.Add(MakeEntry(collapseKey: "k1"));
            svc.Add(MakeEntry(collapseKey: "k1"));
            svc.Add(MakeEntry(collapseKey: "k1"));

            Assert.AreEqual(1, svc.Entries.Count, "Consecutive same-key entries should collapse");
            Assert.AreEqual(3, svc.Entries[0].RepeatCount);
        }

        [Test]
        public void Collapsing_DifferentKey_NoCollapse()
        {
            var svc = CreateService();
            svc.CollapsingEnabled = true;
            svc.Add(MakeEntry(collapseKey: "k1"));
            svc.Add(MakeEntry(collapseKey: "k2"));

            Assert.AreEqual(2, svc.Entries.Count, "Different keys should not collapse");
        }

        [Test]
        public void Collapsing_SameKeyDifferentRunner_NoCollapse()
        {
            var svc = CreateService();
            svc.CollapsingEnabled = true;
            svc.Add(MakeEntry(runnerId: "r1", collapseKey: "k1"));
            svc.Add(MakeEntry(runnerId: "r2", collapseKey: "k1"));

            Assert.AreEqual(2, svc.Entries.Count, "Same key but different runner should not collapse");
        }

        [Test]
        public void Collapsing_NullKey_NeverCollapses()
        {
            var svc = CreateService();
            svc.CollapsingEnabled = true;
            svc.Add(MakeEntry(collapseKey: null));
            svc.Add(MakeEntry(collapseKey: null));
            svc.Add(MakeEntry(collapseKey: null));

            Assert.AreEqual(3, svc.Entries.Count, "Null collapse key should never collapse");
        }

        [Test]
        public void Collapsing_Disabled_NeverCollapses()
        {
            var svc = CreateService();
            svc.CollapsingEnabled = false;
            svc.Add(MakeEntry(collapseKey: "k1"));
            svc.Add(MakeEntry(collapseKey: "k1"));
            svc.Add(MakeEntry(collapseKey: "k1"));

            Assert.AreEqual(3, svc.Entries.Count, "With collapsing disabled, same-key entries should not collapse");
        }

        // ─── Ring buffer ─────────────────────────────────────────

        [Test]
        public void RingBuffer_EvictsOldest()
        {
            var svc = CreateService(maxEntries: 3);
            svc.Add(MakeEntry(summary: "a"));
            svc.Add(MakeEntry(summary: "b"));
            svc.Add(MakeEntry(summary: "c"));
            svc.Add(MakeEntry(summary: "d"));

            Assert.AreEqual(3, svc.Entries.Count);
            Assert.AreEqual("b", svc.Entries[0].Summary, "Oldest entry (a) should be evicted");
            Assert.AreEqual("d", svc.Entries[2].Summary);
        }

        // ─── Queries ─────────────────────────────────────────────

        [Test]
        public void GetWarnings_FiltersCorrectly()
        {
            var svc = CreateService();
            svc.Add(MakeEntry(category: EventCategory.Warning, summary: "warn"));
            svc.Add(MakeEntry(category: EventCategory.StateChange, summary: "state"));
            svc.Add(MakeEntry(category: EventCategory.Warning, summary: "warn2"));

            var warnings = svc.GetWarnings();
            Assert.AreEqual(2, warnings.Count);
            Assert.AreEqual("warn2", warnings[0].Summary, "Most recent first");
            Assert.AreEqual("warn", warnings[1].Summary);
        }

        [Test]
        public void GetActivityFeed_FiltersRunnerExcludesLifecycle()
        {
            var svc = CreateService();
            svc.Add(MakeEntry(runnerId: "r1", category: EventCategory.StateChange, summary: "state"));
            svc.Add(MakeEntry(runnerId: "r2", category: EventCategory.StateChange, summary: "other runner"));
            svc.Add(MakeEntry(runnerId: "r1", category: EventCategory.Lifecycle, summary: "lifecycle"));
            svc.Add(MakeEntry(runnerId: "r1", category: EventCategory.Production, summary: "prod"));

            var feed = svc.GetActivityFeed("r1");
            Assert.AreEqual(2, feed.Count, "Should exclude r2 and Lifecycle entries");
            Assert.AreEqual("prod", feed[0].Summary);
            Assert.AreEqual("state", feed[1].Summary);
        }

        [Test]
        public void GetByCategories_MultipleCategories()
        {
            var svc = CreateService();
            svc.Add(MakeEntry(category: EventCategory.Warning, summary: "w"));
            svc.Add(MakeEntry(category: EventCategory.Automation, summary: "a"));
            svc.Add(MakeEntry(category: EventCategory.StateChange, summary: "s"));
            svc.Add(MakeEntry(category: EventCategory.Production, summary: "p"));

            var result = svc.GetByCategories(EventCategory.Warning, EventCategory.Automation);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("a", result[0].Summary);
            Assert.AreEqual("w", result[1].Summary);
        }

        [Test]
        public void GetForRunner_AllCategories()
        {
            var svc = CreateService();
            svc.Add(MakeEntry(runnerId: "r1", category: EventCategory.Warning));
            svc.Add(MakeEntry(runnerId: "r1", category: EventCategory.Lifecycle));
            svc.Add(MakeEntry(runnerId: "r2", category: EventCategory.Warning));

            var result = svc.GetForRunner("r1");
            Assert.AreEqual(2, result.Count, "Should include all categories for runner r1");
        }

        [Test]
        public void Clear_RemovesAll()
        {
            var svc = CreateService();
            svc.Add(MakeEntry());
            svc.Add(MakeEntry());
            svc.Clear();

            Assert.AreEqual(0, svc.Entries.Count);
        }

        // ─── Integration with GameSimulation ─────────────────────

        [Test]
        public void Integration_ItemGathered_LogsEntries()
        {
            var config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                },
            };
            var sim = new GameSimulation(config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Miner" }
                    .WithSkill(SkillType.Mining, 1),
            };
            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f,
                new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f));
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            sim.StartNewGame(defs, map, "mine");
            var runner = sim.CurrentGameState.Runners[0];

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            sim.AssignRunner(runner.Id, assignment);

            // Tick until we get a few items
            int ticks = 0;
            while (runner.Inventory.CountItem("copper_ore") < 3 && ticks < 5000)
            {
                sim.Tick();
                ticks++;
            }

            // With collapsing off (default), each ItemGathered is a separate entry
            int gatheredCount = 0;
            foreach (var entry in sim.EventLog.Entries)
            {
                if (entry.Summary.Contains("ItemGathered"))
                    gatheredCount++;
            }
            Assert.GreaterOrEqual(gatheredCount, 3,
                "Each ItemGathered event should be a separate entry with collapsing off");
        }

        [Test]
        public void Integration_NoMicroRuleMatched_AppearsAsWarning()
        {
            var config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                },
            };
            var sim = new GameSimulation(config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Stuck" }
                    .WithSkill(SkillType.Mining, 1),
            };
            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f,
                new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f));
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            sim.StartNewGame(defs, map, "mine");
            var runner = sim.CurrentGameState.Runners[0];

            // Register an empty micro ruleset to trigger NoMicroRuleMatched
            var emptyMicro = new Ruleset { Id = "empty-micro", Name = "Empty", Category = RulesetCategory.Gathering };
            sim.CurrentGameState.MicroRulesetLibrary.Add(emptyMicro);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            foreach (var step in assignment.Steps)
            {
                if (step.Type == TaskStepType.Work)
                    step.MicroRulesetId = "empty-micro";
            }
            sim.AssignRunner(runner.Id, assignment);

            var warnings = sim.EventLog.GetWarnings();
            Assert.Greater(warnings.Count, 0, "NoMicroRuleMatched should appear in warnings");
            Assert.IsTrue(warnings[0].Summary.Contains("NoMicroRuleMatched"),
                $"Warning summary should contain 'NoMicroRuleMatched', got: {warnings[0].Summary}");
        }
    }
}
