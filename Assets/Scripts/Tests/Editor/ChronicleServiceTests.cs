using NUnit.Framework;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class ChronicleServiceTests
    {
        private GameState _state;

        private ChronicleService CreateService(int maxEntries = 100)
        {
            _state = new GameState { TickCount = 1 };
            _state.Map = new WorldMap();
            return new ChronicleService(maxEntries, () => _state);
        }

        private Runner AddRunner(string id, string name, string nodeId = "node1")
        {
            var runner = new Runner { Id = id, Name = name, CurrentNodeId = nodeId };
            _state.Runners.Add(runner);
            return runner;
        }

        private WorldNode AddNode(string id, string name)
        {
            var node = new WorldNode { Id = id, Name = name };
            _state.Map.Nodes.Add(node);
            _state.Map.Initialize();
            return node;
        }

        // ─── Basic add/retrieve ───────────────────────────────────

        [Test]
        public void Add_StoresEntry()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry
            {
                TickNumber = 1,
                Category = EventCategory.Production,
                RunnerId = "r1",
                RunnerName = "Kira",
                Text = "Kira gathered Copper Ore",
            });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual("Kira gathered Copper Ore", svc.Entries[0].Text);
            Assert.AreEqual(1, svc.Entries[0].RepeatCount);
        }

        [Test]
        public void GetAll_ReturnsMostRecentFirst()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { TickNumber = 1, Text = "first" });
            svc.Add(new ChronicleEntry { TickNumber = 2, Text = "second" });

            var all = svc.GetAll();
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual("second", all[0].Text);
            Assert.AreEqual("first", all[1].Text);
        }

        // ─── Collapsing ──────────────────────────────────────────

        [Test]
        public void Collapsing_SameKeyAndRunner_IncrementsCount()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { RunnerId = "r1", CollapseKey = "k1", Text = "a" });
            svc.Add(new ChronicleEntry { RunnerId = "r1", CollapseKey = "k1", Text = "a" });
            svc.Add(new ChronicleEntry { RunnerId = "r1", CollapseKey = "k1", Text = "a" });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual(3, svc.Entries[0].RepeatCount);
        }

        [Test]
        public void Collapsing_DifferentKey_NoCollapse()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { RunnerId = "r1", CollapseKey = "k1", Text = "a" });
            svc.Add(new ChronicleEntry { RunnerId = "r1", CollapseKey = "k2", Text = "b" });

            Assert.AreEqual(2, svc.Entries.Count);
        }

        [Test]
        public void Collapsing_SameKeyDifferentRunner_NoCollapse()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { RunnerId = "r1", CollapseKey = "k1", Text = "a" });
            svc.Add(new ChronicleEntry { RunnerId = "r2", CollapseKey = "k1", Text = "b" });

            Assert.AreEqual(2, svc.Entries.Count);
        }

        [Test]
        public void Collapsing_NullKey_NeverCollapses()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { RunnerId = "r1", CollapseKey = null, Text = "a" });
            svc.Add(new ChronicleEntry { RunnerId = "r1", CollapseKey = null, Text = "a" });

            Assert.AreEqual(2, svc.Entries.Count);
        }

        // ─── Ring buffer eviction ─────────────────────────────────

        [Test]
        public void Eviction_RemovesOldestWhenFull()
        {
            var svc = CreateService(maxEntries: 3);
            svc.Add(new ChronicleEntry { Text = "a" });
            svc.Add(new ChronicleEntry { Text = "b" });
            svc.Add(new ChronicleEntry { Text = "c" });
            svc.Add(new ChronicleEntry { Text = "d" });

            Assert.AreEqual(3, svc.Entries.Count);
            Assert.AreEqual("b", svc.Entries[0].Text);
            Assert.AreEqual("d", svc.Entries[2].Text);
        }

        // ─── Query: GetForRunner ──────────────────────────────────

        [Test]
        public void GetForRunner_FiltersCorrectly()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { RunnerId = "r1", Text = "a" });
            svc.Add(new ChronicleEntry { RunnerId = "r2", Text = "b" });
            svc.Add(new ChronicleEntry { RunnerId = "r1", Text = "c" });

            var result = svc.GetForRunner("r1");
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("c", result[0].Text);
            Assert.AreEqual("a", result[1].Text);
        }

        // ─── Query: GetForNode ────────────────────────────────────

        [Test]
        public void GetForNode_FiltersCorrectly()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { NodeId = "mine", Text = "a" });
            svc.Add(new ChronicleEntry { NodeId = "forest", Text = "b" });
            svc.Add(new ChronicleEntry { NodeId = "mine", Text = "c" });

            var result = svc.GetForNode("mine");
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("c", result[0].Text);
            Assert.AreEqual("a", result[1].Text);
        }

        // ─── Query: GetRecent ─────────────────────────────────────

        [Test]
        public void GetRecent_ReturnsLastN()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { Text = "a" });
            svc.Add(new ChronicleEntry { Text = "b" });
            svc.Add(new ChronicleEntry { Text = "c" });

            var result = svc.GetRecent(2);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("c", result[0].Text);
            Assert.AreEqual("b", result[1].Text);
        }

        [Test]
        public void GetRecent_MoreThanAvailable_ReturnsAll()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { Text = "a" });

            var result = svc.GetRecent(100);
            Assert.AreEqual(1, result.Count);
        }

        // ─── Event handlers (integration via EventBus) ────────────

        [Test]
        public void ItemGatheredEvent_ProducesPlayerFriendlyText()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "mine");

            events.Publish(new ItemGathered
            {
                RunnerId = "r1",
                ItemId = "copper_ore",
                InventoryFreeSlots = 23,
            });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual("Kira gathered copper_ore (23 slots free)", svc.Entries[0].Text);
            Assert.AreEqual("Kira", svc.Entries[0].RunnerName);
            Assert.AreEqual("mine", svc.Entries[0].NodeId);
            Assert.AreEqual(EventCategory.Production, svc.Entries[0].Category);
        }

        [Test]
        public void GatheringFailedEvent_NotEnoughSkill_ProducesWarning()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "mine");

            events.Publish(new GatheringFailed
            {
                RunnerId = "r1",
                NodeId = "mine",
                Reason = GatheringFailureReason.NotEnoughSkill,
                Skill = SkillType.Mining,
                CurrentLevel = 3,
                RequiredLevel = 10,
            });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual("Kira: Mining level too low (3/10)", svc.Entries[0].Text);
            Assert.AreEqual(EventCategory.Warning, svc.Entries[0].Category);
            Assert.AreEqual("mine", svc.Entries[0].NodeId);
        }

        [Test]
        public void LevelUpEvent_ProducesProductionEntry()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "mine");

            events.Publish(new RunnerSkillLeveledUp
            {
                RunnerId = "r1",
                Skill = SkillType.Mining,
                NewLevel = 15,
            });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual("Kira leveled up Mining to 15!", svc.Entries[0].Text);
            Assert.AreEqual(EventCategory.Production, svc.Entries[0].Category);
        }

        [Test]
        public void StartedTravelEvent_ResolvesNodeName()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "hub");
            AddNode("mine", "Copper Mine");

            events.Publish(new RunnerStartedTravel
            {
                RunnerId = "r1",
                FromNodeId = "hub",
                ToNodeId = "mine",
                EstimatedDurationSeconds = 12.3f,
            });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.That(svc.Entries[0].Text, Does.Contain("Copper Mine"));
            Assert.That(svc.Entries[0].Text, Does.Contain("12.3s"));
        }

        [Test]
        public void ArrivedEvent_ResolvesNodeName()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "mine");
            AddNode("mine", "Copper Mine");

            events.Publish(new RunnerArrivedAtNode
            {
                RunnerId = "r1",
                NodeId = "mine",
            });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual("Kira arrived at Copper Mine", svc.Entries[0].Text);
            Assert.AreEqual("mine", svc.Entries[0].NodeId);
        }

        [Test]
        public void DepositedEvent_ProducesStateChange()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "hub");

            events.Publish(new RunnerDeposited
            {
                RunnerId = "r1",
                ItemsDeposited = 28,
            });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual("Kira deposited 28 items", svc.Entries[0].Text);
            Assert.AreEqual(EventCategory.StateChange, svc.Entries[0].Category);
        }

        [Test]
        public void InventoryFullEvent_ProducesStateChange()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "mine");

            events.Publish(new InventoryFull { RunnerId = "r1" });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.AreEqual("Kira's inventory is full", svc.Entries[0].Text);
        }

        [Test]
        public void ItemGathered_CollapsesByDefault()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "mine");

            events.Publish(new ItemGathered { RunnerId = "r1", ItemId = "copper_ore", InventoryFreeSlots = 27 });
            events.Publish(new ItemGathered { RunnerId = "r1", ItemId = "copper_ore", InventoryFreeSlots = 26 });
            events.Publish(new ItemGathered { RunnerId = "r1", ItemId = "copper_ore", InventoryFreeSlots = 25 });

            Assert.AreEqual(1, svc.Entries.Count, "Consecutive same-item gathers should collapse");
            Assert.AreEqual(3, svc.Entries[0].RepeatCount);
        }

        [Test]
        public void NoMicroRuleMatched_ProducesWarning()
        {
            var svc = CreateService();
            var events = new EventBus();
            svc.SubscribeAll(events);

            AddRunner("r1", "Kira", "mine");

            events.Publish(new NoMicroRuleMatched
            {
                RunnerId = "r1",
                RunnerName = "Kira",
                NodeId = "mine",
                RulesetIsEmpty = true,
                RuleCount = 0,
            });

            Assert.AreEqual(1, svc.Entries.Count);
            Assert.That(svc.Entries[0].Text, Does.Contain("no micro rules configured"));
            Assert.AreEqual(EventCategory.Warning, svc.Entries[0].Category);
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            var svc = CreateService();
            svc.Add(new ChronicleEntry { Text = "a" });
            svc.Add(new ChronicleEntry { Text = "b" });
            svc.Clear();

            Assert.AreEqual(0, svc.Entries.Count);
        }

        [Test]
        public void SetMaxEntries_EvictsExcess()
        {
            var svc = CreateService(maxEntries: 100);
            svc.Add(new ChronicleEntry { Text = "a" });
            svc.Add(new ChronicleEntry { Text = "b" });
            svc.Add(new ChronicleEntry { Text = "c" });

            svc.SetMaxEntries(2);

            Assert.AreEqual(2, svc.Entries.Count);
            Assert.AreEqual("b", svc.Entries[0].Text);
            Assert.AreEqual("c", svc.Entries[1].Text);
        }
    }
}
