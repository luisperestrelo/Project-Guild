using NUnit.Framework;
using ProjectGuild.Simulation.Automation;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class DecisionLogTests
    {
        // ─── Basic add/retrieve ──────────────────────────────────────

        [Test]
        public void Add_StoresEntry()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "runner1", "Test Rule"));

            Assert.AreEqual(1, log.Entries.Count);
            Assert.AreEqual("runner1", log.Entries[0].RunnerId);
        }

        [Test]
        public void Add_MultipleEntries_PreservesOrder()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "r1", "Rule A"));
            log.Add(MakeEntry(2, "r2", "Rule B"));
            log.Add(MakeEntry(3, "r1", "Rule C"));

            Assert.AreEqual(3, log.Entries.Count);
            Assert.AreEqual(1, log.Entries[0].TickNumber);
            Assert.AreEqual(3, log.Entries[2].TickNumber);
        }

        // ─── Ring buffer eviction ────────────────────────────────────

        [Test]
        public void Eviction_RemovesOldestEntries()
        {
            var log = new DecisionLog();
            log.SetMaxEntries(3);

            log.Add(MakeEntry(1, "r1", "A"));
            log.Add(MakeEntry(2, "r1", "B"));
            log.Add(MakeEntry(3, "r1", "C"));
            log.Add(MakeEntry(4, "r1", "D"));

            Assert.AreEqual(3, log.Entries.Count, "Should evict oldest to stay at max");
            Assert.AreEqual(2, log.Entries[0].TickNumber, "Oldest remaining should be tick 2");
            Assert.AreEqual(4, log.Entries[2].TickNumber, "Newest should be tick 4");
        }

        [Test]
        public void SetMaxEntries_EvictsExisting()
        {
            var log = new DecisionLog();
            for (int i = 0; i < 10; i++)
                log.Add(MakeEntry(i, "r1", $"Rule {i}"));

            Assert.AreEqual(10, log.Entries.Count);

            log.SetMaxEntries(5);

            Assert.AreEqual(5, log.Entries.Count);
            Assert.AreEqual(5, log.Entries[0].TickNumber);
        }

        // ─── Generation counter ────────────────────────────────────────

        [Test]
        public void GenerationCounter_IncrementsOnEachAdd()
        {
            var log = new DecisionLog();
            Assert.AreEqual(0, log.GenerationCounter);

            log.Add(MakeEntry(1, "r1", "A"));
            Assert.AreEqual(1, log.GenerationCounter);

            log.Add(MakeEntry(2, "r1", "B"));
            Assert.AreEqual(2, log.GenerationCounter);
        }

        [Test]
        public void GenerationCounter_ContinuesIncrementingWhenBufferFull()
        {
            var log = new DecisionLog();
            log.SetMaxEntries(2);

            log.Add(MakeEntry(1, "r1", "A"));
            log.Add(MakeEntry(2, "r1", "B"));
            Assert.AreEqual(2, log.GenerationCounter);
            Assert.AreEqual(2, log.Entries.Count);

            // Buffer full — eviction happens but generation keeps going
            log.Add(MakeEntry(3, "r1", "C"));
            Assert.AreEqual(3, log.GenerationCounter);
            Assert.AreEqual(2, log.Entries.Count, "Count stays at max");
        }

        // ─── GetAll ────────────────────────────────────────────────────

        [Test]
        public void GetAll_ReturnsAllMostRecentFirst()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "r1", "A"));
            log.Add(MakeEntry(2, "r2", "B"));
            log.Add(MakeEntry(3, "r1", "C"));

            var result = log.GetAll();
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(3, result[0].TickNumber);
            Assert.AreEqual(1, result[2].TickNumber);
        }

        [Test]
        public void GetAll_FiltersByLayer()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "r1", "A", layer: DecisionLayer.Macro));
            log.Add(MakeEntry(2, "r1", "B", layer: DecisionLayer.Micro));
            log.Add(MakeEntry(3, "r1", "C", layer: DecisionLayer.Macro));

            var macroOnly = log.GetAll(DecisionLayer.Macro);
            Assert.AreEqual(2, macroOnly.Count);
            Assert.AreEqual(3, macroOnly[0].TickNumber);
            Assert.AreEqual(1, macroOnly[1].TickNumber);

            var microOnly = log.GetAll(DecisionLayer.Micro);
            Assert.AreEqual(1, microOnly.Count);
            Assert.AreEqual(2, microOnly[0].TickNumber);
        }

        // ─── GetForNode ──────────────────────────────────────────────

        [Test]
        public void GetForNode_FiltersCorrectly()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "r1", "A", nodeId: "mine"));
            log.Add(MakeEntry(2, "r2", "B", nodeId: "forest"));
            log.Add(MakeEntry(3, "r1", "C", nodeId: "mine"));
            log.Add(MakeEntry(4, "r2", "D", nodeId: "forest"));

            var mineEntries = log.GetForNode("mine");
            Assert.AreEqual(2, mineEntries.Count);
            Assert.AreEqual(3, mineEntries[0].TickNumber);
            Assert.AreEqual(1, mineEntries[1].TickNumber);
        }

        [Test]
        public void GetForNode_WithLayerFilter()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "r1", "A", nodeId: "mine", layer: DecisionLayer.Macro));
            log.Add(MakeEntry(2, "r1", "B", nodeId: "mine", layer: DecisionLayer.Micro));
            log.Add(MakeEntry(3, "r2", "C", nodeId: "mine", layer: DecisionLayer.Macro));
            log.Add(MakeEntry(4, "r1", "D", nodeId: "forest", layer: DecisionLayer.Macro));

            var mineMacro = log.GetForNode("mine", DecisionLayer.Macro);
            Assert.AreEqual(2, mineMacro.Count);
            Assert.AreEqual(3, mineMacro[0].TickNumber);
            Assert.AreEqual(1, mineMacro[1].TickNumber);

            var mineMicro = log.GetForNode("mine", DecisionLayer.Micro);
            Assert.AreEqual(1, mineMicro.Count);
            Assert.AreEqual(2, mineMicro[0].TickNumber);
        }

        // ─── Filter by runner ────────────────────────────────────────

        [Test]
        public void GetForRunner_FiltersCorrectly()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "r1", "A"));
            log.Add(MakeEntry(2, "r2", "B"));
            log.Add(MakeEntry(3, "r1", "C"));
            log.Add(MakeEntry(4, "r2", "D"));

            var r1Entries = log.GetForRunner("r1");
            Assert.AreEqual(2, r1Entries.Count);
            // Most recent first
            Assert.AreEqual(3, r1Entries[0].TickNumber);
            Assert.AreEqual(1, r1Entries[1].TickNumber);
        }

        [Test]
        public void GetForRunner_NoMatches_ReturnsEmpty()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "r1", "A"));

            var result = log.GetForRunner("nonexistent");
            Assert.AreEqual(0, result.Count);
        }

        // ─── Filter by tick range ────────────────────────────────────

        [Test]
        public void GetInRange_FiltersCorrectly()
        {
            var log = new DecisionLog();
            for (int i = 1; i <= 10; i++)
                log.Add(MakeEntry(i, "r1", $"Rule {i}"));

            var result = log.GetInRange(3, 7);
            Assert.AreEqual(5, result.Count);
            // Most recent first
            Assert.AreEqual(7, result[0].TickNumber);
            Assert.AreEqual(3, result[4].TickNumber);
        }

        [Test]
        public void GetInRange_NoMatches_ReturnsEmpty()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(5, "r1", "A"));

            var result = log.GetInRange(10, 20);
            Assert.AreEqual(0, result.Count);
        }

        // ─── Clear ───────────────────────────────────────────────────

        [Test]
        public void Clear_RemovesAllEntries()
        {
            var log = new DecisionLog();
            log.Add(MakeEntry(1, "r1", "A"));
            log.Add(MakeEntry(2, "r1", "B"));

            log.Clear();

            Assert.AreEqual(0, log.Entries.Count);
        }

        // ─── Helper ──────────────────────────────────────────────────

        private static DecisionLogEntry MakeEntry(long tick, string runnerId, string label,
            string nodeId = null, DecisionLayer layer = DecisionLayer.Macro)
        {
            return new DecisionLogEntry
            {
                TickNumber = tick,
                GameTime = tick * 0.1f,
                RunnerId = runnerId,
                RunnerName = runnerId,
                NodeId = nodeId,
                Layer = layer,
                RuleIndex = 0,
                RuleLabel = label,
                TriggerReason = "test",
                ActionType = ActionType.Idle,
                ActionDetail = "test",
                ConditionSnapshot = "test",
            };
        }
    }
}
