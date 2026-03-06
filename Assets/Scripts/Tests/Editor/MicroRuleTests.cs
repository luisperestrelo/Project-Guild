using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class MicroRuleTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f);

        private static readonly Simulation.Gathering.GatherableConfig TinGatherable =
            new Simulation.Gathering.GatherableConfig("tin_ore", SkillType.Mining, 40f, 0.5f);

        private void Setup(string startNodeId = "mine")
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("tin_ore", "Tin Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);


            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f, null, CopperGatherable, TinGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, startNodeId);
            _runner = _sim.CurrentGameState.Runners[0];
        }

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

        /// <summary>
        /// Create a micro ruleset and register it in the library.
        /// Call SetWorkStepMicroRuleset on the TaskSequence BEFORE AssignRunner to use it.
        /// </summary>
        private Ruleset CreateAndRegisterMicroRuleset(string id = "test-micro")
        {
            var ruleset = new Ruleset { Id = id, Name = id, Category = RulesetCategory.Gathering };
            _sim.CurrentGameState.MicroRulesetLibrary.Add(ruleset);
            return ruleset;
        }

        /// <summary>Set the MicroRulesetId on Work steps of a task sequence. Call BEFORE AssignRunner.</summary>
        private void SetWorkStepMicroRuleset(TaskSequence seq, string microRulesetId)
        {
            if (seq?.Steps == null) return;
            foreach (var step in seq.Steps)
            {
                if (step.Type == TaskStepType.Work)
                    step.MicroRulesetId = microRulesetId;
            }
        }

        // ─── Default micro rule ─────────────────────────────────

        [Test]
        public void DefaultMicro_GathersValidResource()
        {
            Setup("mine"); // mine has 2 gatherables (copper, tin)

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.That(_runner.Gathering.GatherableIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(2),
                "Default micro rule (Always → GatherAny) should select a valid gatherable index");
        }

        // ─── Custom micro rule selects specific resource ──────

        [Test]
        public void CustomMicro_GathersSpecificIndex()
        {
            Setup("mine");

            // Micro rule: always gather index 1 (tin)
            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(1),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(1, _runner.Gathering.GatherableIndex,
                "Custom micro rule should select index 1 (tin)");
        }

        // ─── Multi-resource switching ───────────────────────────

        [Test]
        public void MultiResource_SwitchesBasedOnInventory()
        {
            Setup("mine");

            // Rules: gather copper until 3, then gather tin until 3, then FinishTask
            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Label = "Copper until 3",
                Conditions = { Condition.InventoryContains("copper_ore", ComparisonOperator.LessThan, 3) },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            microRuleset.Rules.Add(new Rule
            {
                Label = "Tin until 3",
                Conditions = { Condition.InventoryContains("tin_ore", ComparisonOperator.LessThan, 3) },
                Action = AutomationAction.GatherHere(1),
                Enabled = true,
            });
            microRuleset.Rules.Add(new Rule
            {
                Label = "Done",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            // Should start gathering copper (index 0)
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex);

            // Tick until we have 3 copper — should switch to tin
            TickUntil(() => _runner.Inventory.CountItem("copper_ore") >= 3);
            Assert.AreEqual(3, _runner.Inventory.CountItem("copper_ore"));

            // Runner should now be gathering tin (index 1) — or about to after re-eval
            // Give it a tick to process the switch
            if (_runner.State == RunnerState.Gathering)
            {
                Assert.AreEqual(1, _runner.Gathering.GatherableIndex,
                    "After 3 copper, micro rules should switch to tin (index 1)");
            }

            // Tick until we have 3 tin
            TickUntil(() => _runner.Inventory.CountItem("tin_ore") >= 3);
            Assert.AreEqual(3, _runner.Inventory.CountItem("tin_ore"));

            // FinishTask should have fired — runner advances past Gather step
            // With a looping gather assignment, next step is TravelTo(hub)
            // The runner should be traveling to hub or depositing
            Assert.AreNotEqual(RunnerState.Gathering, _runner.State,
                "After 3 copper + 3 tin, FinishTask should stop gathering and advance macro");
        }

        // ─── FinishTask action ──────────────────────────────────

        [Test]
        public void FinishTask_StopsGatheringAndAdvancesMacro()
        {
            Setup("mine");

            // Micro rule: always FinishTask (never actually gathers)
            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            // ExecuteGatherStep evaluates micro → FinishTask → advance macro
            // Next step after Gather is TravelTo(hub), so runner should be traveling
            Assert.AreEqual(RunnerState.Traveling, _runner.State,
                "FinishTask should skip gathering and advance to next macro step (TravelTo hub)");
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
        }

        [Test]
        public void FinishTask_MidGathering_StopsAndAdvancesMacro()
        {
            Setup("mine");

            // Start with normal micro (gather copper), then switch to FinishTask after 2 items
            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Label = "Copper until 2",
                Conditions = { Condition.InventoryContains("copper_ore", ComparisonOperator.LessThan, 2) },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            microRuleset.Rules.Add(new Rule
            {
                Label = "Done after 2",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Tick until FinishTask fires (after 2 copper)
            TickUntil(() => _runner.State != RunnerState.Gathering);

            Assert.AreEqual(2, _runner.Inventory.CountItem("copper_ore"));
            // Should be traveling to hub (next macro step after Gather)
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
        }

        // ─── Empty/null micro ruleset: "let it break" vs data fallback ──

        [Test]
        public void EmptyMicroRuleset_RunnerStaysIdle()
        {
            Setup("mine");

            // Empty micro ruleset — player deleted all rules. Runner should be stuck.
            var emptyMicro = CreateAndRegisterMicroRuleset("empty-micro");

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "empty-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner should NOT be gathering — no micro rule matched, stuck at Work step
            Assert.AreEqual(RunnerState.Idle, _runner.State,
                "Empty micro ruleset should leave runner stuck (let it break)");
            Assert.IsNull(_runner.Gathering);
        }

        [Test]
        public void NullMicroRuleset_RunnerStaysIdle()
        {
            Setup("mine");

            // Null/missing micro ruleset = broken data. Runner should get stuck.
            var assignment = TaskSequence.CreateLoop("mine", "hub");
            // Set Work step to point to a non-existent ruleset — simulates bad data
            SetWorkStepMicroRuleset(assignment, "nonexistent-ruleset");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Idle, _runner.State,
                "Null micro ruleset should leave runner stuck (let it break)");
            Assert.IsNull(_runner.Gathering);
        }

        // ─── Micro rule with out-of-bounds index ────────────────

        [Test]
        public void MicroRule_InvalidIndex_RunnerStaysIdle()
        {
            Setup("mine");

            // Micro rule points to index 99 which doesn't exist — broken rule
            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(99),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner should NOT be gathering — rule is broken, stuck at Work step
            Assert.AreEqual(RunnerState.Idle, _runner.State,
                "Invalid micro rule index should leave runner stuck (let it break)");
            Assert.IsNull(_runner.Gathering);
        }

        // ─── NoMicroRuleMatched event ─────────────────────────────

        [Test]
        public void EmptyMicroRuleset_FiresNoMicroRuleMatched()
        {
            Setup("mine");

            var emptyMicro = CreateAndRegisterMicroRuleset("empty-micro-event");

            NoMicroRuleMatched? received = null;
            _sim.Events.Subscribe<NoMicroRuleMatched>(e => received = e);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "empty-micro-event");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.IsNotNull(received, "NoMicroRuleMatched should fire for empty ruleset");
            Assert.AreEqual(_runner.Id, received.Value.RunnerId);
            Assert.IsTrue(received.Value.RulesetIsEmpty, "RulesetIsEmpty should be true for empty ruleset");
            Assert.AreEqual(0, received.Value.RuleCount);
        }

        [Test]
        public void InvalidIndex_FiresNoMicroRuleMatched()
        {
            Setup("mine");

            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(99),
                Enabled = true,
            });

            NoMicroRuleMatched? received = null;
            _sim.Events.Subscribe<NoMicroRuleMatched>(e => received = e);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.IsNotNull(received, "NoMicroRuleMatched should fire for invalid index");
            Assert.IsFalse(received.Value.RulesetIsEmpty, "RulesetIsEmpty should be false when rules exist");
            Assert.AreEqual(1, received.Value.RuleCount);
        }

        [Test]
        public void NoMatchingConditions_FiresNoMicroRuleMatched()
        {
            Setup("mine");

            // Rule that never matches: requires 999 copper in inventory
            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.InventoryContains("copper_ore", ComparisonOperator.GreaterOrEqual, 999) },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            NoMicroRuleMatched? received = null;
            _sim.Events.Subscribe<NoMicroRuleMatched>(e => received = e);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.IsNotNull(received, "NoMicroRuleMatched should fire when no conditions match");
            Assert.IsFalse(received.Value.RulesetIsEmpty);
            Assert.AreEqual(1, received.Value.RuleCount);
            Assert.AreEqual("mine", received.Value.NodeId);
        }

        [Test]
        public void MidGathering_NoMatch_FiresNoMicroRuleMatched()
        {
            Setup("mine");

            // Start with copper until 2, then no fallback → stuck
            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Label = "Copper until 2",
                Conditions = { Condition.InventoryContains("copper_ore", ComparisonOperator.LessThan, 2) },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            NoMicroRuleMatched? received = null;

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State, "Should start gathering");

            // Subscribe after assignment so we don't catch the initial eval
            _sim.Events.Subscribe<NoMicroRuleMatched>(e => received = e);

            // Tick until we have 2 copper — then the rule stops matching
            TickUntil(() => _runner.Inventory.CountItem("copper_ore") >= 2);

            // Re-eval mid-gathering should fire NoMicroRuleMatched
            Assert.IsNotNull(received,
                "NoMicroRuleMatched should fire from ReevaluateMicroDuringGathering when rules stop matching");
            Assert.AreEqual(RunnerState.Idle, _runner.State, "Runner should be stuck");
        }

        [Test]
        public void ValidMicroRule_DoesNotFireNoMicroRuleMatched()
        {
            Setup("mine");

            // Default micro rule: Always → GatherHere(0)
            // Should NOT fire NoMicroRuleMatched

            NoMicroRuleMatched? received = null;
            _sim.Events.Subscribe<NoMicroRuleMatched>(e => received = e);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick a few times to make sure it never fires
            for (int i = 0; i < 50; i++)
                _sim.Tick();

            Assert.IsNull(received, "NoMicroRuleMatched should NOT fire when a valid rule matches");
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
        }

        // ─── InventoryFull as micro rule (not hardcoded) ──────

        [Test]
        public void InventoryFull_MicroRule_TriggersFinishTask()
        {
            Setup("mine");

            // Default micro: [InventoryFull → FinishTask, Always → GatherHere(0)]
            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Fill inventory to capacity minus 1
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < _sim.Config.InventorySize - 1; i++)
                _runner.Inventory.TryAdd(itemDef);

            // Tick until inventory fills — the micro rule should trigger FinishTask
            TickUntil(() => _runner.State != RunnerState.Gathering, safetyLimit: 500);

            // Runner should have advanced past Work step (FinishTask) and be traveling to hub
            Assert.AreEqual(RunnerState.Traveling, _runner.State,
                "InventoryFull micro rule should trigger FinishTask → travel to hub");
        }

        [Test]
        public void InventoryFull_MicroRuleDeleted_RunnerKeepsGathering()
        {
            Setup("mine");

            // Remove the InventoryFull rule — only keep GatherHere
            var microRuleset = CreateAndRegisterMicroRuleset();
            microRuleset.Rules.Add(new Rule
            {
                Label = "Gather only",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "test-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            // Fill inventory almost full
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < _sim.Config.InventorySize - 1; i++)
                _runner.Inventory.TryAdd(itemDef);

            // Tick enough for inventory to fill
            for (int i = 0; i < 500; i++)
                _sim.Tick();

            // Without the InventoryFull rule, runner should still be gathering (stuck trying to add)
            // or at least still at the mine on the Work step — NOT traveling to hub
            Assert.AreEqual("mine", _runner.CurrentNodeId,
                "Without InventoryFull rule, runner should not auto-return to hub");
        }

        // ─── GatheringFailed with Reason ──────────────────────

        [Test]
        public void GatheringFailed_NoGatherablesAtNode_StaysStuck()
        {
            // Setup with runner at hub (which has no gatherables)
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
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub"); // No gatherables!
            map.AddNode("mine", "Mine", 0f, 0f, null, CopperGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "hub");
            _runner = _sim.CurrentGameState.Runners[0];

            GatheringFailed? failed = null;
            _sim.Events.Subscribe<GatheringFailed>(e => failed = e);

            // Create sequence that puts runner at hub and tries to work
            var seq = new TaskSequence
            {
                Name = "Work at hub",
                TargetNodeId = "hub",
                Loop = true,
                Steps = new System.Collections.Generic.List<TaskStep>
                {
                    new TaskStep(TaskStepType.Work, microRulesetId: DefaultRulesets.DefaultMicroId),
                },
            };
            _sim.AssignRunner(_runner.Id, seq);

            Assert.IsNotNull(failed, "GatheringFailed should fire when node has no gatherables");
            Assert.AreEqual(GatheringFailureReason.NoGatherablesAtNode, failed.Value.Reason);
            Assert.AreEqual("hub", failed.Value.NodeId);

            // Runner should NOT have advanced — stays stuck at Work step
            Assert.AreEqual(0, _runner.TaskSequenceCurrentStepIndex,
                "Runner should stay stuck at Work step (let it break)");
        }

        [Test]
        public void GatheringFailed_NotEnoughSkill_StaysStuck()
        {
            Setup("mine");

            // Create a high-level gatherable the runner can't harvest
            var hardGatherable = new Simulation.Gathering.GatherableConfig("gold_ore", SkillType.Mining, 40f, 0.5f, 50);

            // Replace the mine node with the hard gatherable
            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f, null, hardGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("gold_ore", "Gold Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 1), // Level 1, needs 50
            };

            _sim.StartNewGame(defs, map, "mine");
            _runner = _sim.CurrentGameState.Runners[0];

            GatheringFailed? failed = null;
            _sim.Events.Subscribe<GatheringFailed>(e => failed = e);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.IsNotNull(failed, "GatheringFailed should fire when skill level is too low");
            Assert.AreEqual(GatheringFailureReason.NotEnoughSkill, failed.Value.Reason);
            Assert.AreEqual(50, failed.Value.RequiredLevel);
            Assert.AreEqual(1, failed.Value.CurrentLevel);
        }

        // ─── GatherAny tests ──────────────────────────────────────

        [Test]
        public void GatherAny_ProducesVariedResources()
        {
            Setup("mine"); // mine has copper + tin

            // Use GatherAny in the micro ruleset (no FinishTask — gather until we track enough items)
            var microRuleset = CreateAndRegisterMicroRuleset("gather-any-micro");
            microRuleset.Rules.Add(new Rule
            {
                Label = "Gather anything",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherAny(),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "gather-any-micro");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Track gathered items via events
            int copperGathered = 0;
            int tinGathered = 0;
            _sim.Events.Subscribe<ItemGathered>(e =>
            {
                if (e.ItemId == "copper_ore") copperGathered++;
                else if (e.ItemId == "tin_ore") tinGathered++;
            });

            // Tick until we've gathered at least 20 items total (enough for statistical confidence)
            TickUntil(() => copperGathered + tinGathered >= 20, 50000);

            Assert.GreaterOrEqual(copperGathered + tinGathered, 20,
                "Should have gathered at least 20 items");
            Assert.Greater(copperGathered, 0, "GatherAny should produce at least some copper");
            Assert.Greater(tinGathered, 0, "GatherAny should produce at least some tin");
        }

        [Test]
        public void GatherAny_StableMidGather()
        {
            Setup("mine");

            var microRuleset = CreateAndRegisterMicroRuleset("gather-any-stable");
            microRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherAny(),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(assignment, "gather-any-stable");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            int initialIndex = _runner.Gathering.GatherableIndex;

            // Tick a few times (not enough to produce an item) — index should stay the same
            for (int i = 0; i < 3; i++)
            {
                _sim.Tick();
                if (_runner.State != RunnerState.Gathering) break;
                Assert.AreEqual(initialIndex, _runner.Gathering.GatherableIndex,
                    $"GatherAny should not switch resources mid-gather (tick {i + 1})");
            }
        }

        [Test]
        public void GatherAny_EmptyNode_LetItBreak()
        {
            // Create a node with no gatherables
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("empty", "Empty Field"); // no gatherables
            map.AddEdge("hub", "empty", 8f);
            map.Initialize();

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 1),
            };

            _sim.StartNewGame(defs, map, "empty");
            _runner = _sim.CurrentGameState.Runners[0];

            GatheringFailed? failed = null;
            _sim.Events.Subscribe<GatheringFailed>(e => failed = e);

            var microRuleset = CreateAndRegisterMicroRuleset("gather-any-empty");
            microRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherAny(),
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("empty", "hub");
            SetWorkStepMicroRuleset(assignment, "gather-any-empty");
            _sim.AssignRunner(_runner.Id, assignment);

            // Should fail because node has no gatherables
            Assert.IsNotNull(failed, "GatheringFailed should fire when node has no gatherables");
        }

        [Test]
        public void DefaultMicro_UsesGatherAny()
        {
            var defaultMicro = DefaultRulesets.CreateDefaultMicro();
            Assert.AreEqual(2, defaultMicro.Rules.Count);

            var gatherRule = defaultMicro.Rules[1]; // second rule: Always → Gather
            Assert.AreEqual(ActionType.GatherHere, gatherRule.Action.Type);
            Assert.AreEqual(-1, gatherRule.Action.IntParam,
                "Default micro should use GatherAny (IntParam = -1)");
        }

        // ─── GatherAny + MinLevel interaction ────────────────────

        [Test]
        public void GatherAny_OnlyPicksEligibleGatherables()
        {
            // Node has copper (no MinLevel) and gold (MinLevel 50). Runner has Mining 1.
            // GatherAny should only ever pick copper — "any" means "any I'm capable of."
            var hardGatherable = new Simulation.Gathering.GatherableConfig("gold_ore", SkillType.Mining, 40f, 0.5f, 50);

            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("gold_ore", "Gold Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mixed", "Mixed Mine", 0f, 0f, null, CopperGatherable, hardGatherable);
            map.AddEdge("hub", "mixed", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "mixed");
            _runner = _sim.CurrentGameState.Runners[0];

            var seq = TaskSequence.CreateLoop("mixed", "hub");
            _sim.AssignRunner(_runner.Id, seq, "Test");

            // Should start gathering immediately (already at node)
            Assert.AreEqual(RunnerState.Gathering, _runner.State, "Runner should be gathering");
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex,
                "GatherAny should pick copper (index 0), not gold (index 1) which requires level 50");

            // Tick through several items to verify it never switches to gold
            for (int i = 0; i < 500; i++) _sim.Tick();

            if (_runner.State == RunnerState.Gathering)
            {
                Assert.AreEqual(0, _runner.Gathering.GatherableIndex,
                    "After multiple items, GatherAny should still be on copper, never gold");
            }
        }

        [Test]
        public void GatherAny_AllAboveMinLevel_RunnerGetsStuck()
        {
            // Node has ONLY gold (MinLevel 50). Runner has Mining 1.
            // GatherAny should find no eligible gatherables → NoMatch → stuck.
            var hardGatherable = new Simulation.Gathering.GatherableConfig("gold_ore", SkillType.Mining, 40f, 0.5f, 50);

            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("gold_ore", "Gold Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("hard", "Hard Mine", 0f, 0f, null, hardGatherable);
            map.AddEdge("hub", "hard", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "hard");
            _runner = _sim.CurrentGameState.Runners[0];

            GatheringFailed? failed = null;
            _sim.Events.Subscribe<GatheringFailed>(e => failed = e);

            var seq = TaskSequence.CreateLoop("hard", "hub");
            _sim.AssignRunner(_runner.Id, seq, "Test");

            // Tick to reach Work step
            for (int i = 0; i < 10; i++) _sim.Tick();

            // Runner should be stuck — either GatheringFailed (NotEnoughSkill) or NoMicroRuleMatched
            Assert.AreNotEqual(RunnerState.Gathering, _runner.State,
                "Runner should NOT be gathering gold with Mining level 1");
        }

        [Test]
        public void MidGatherResourceSwitch_ChecksMinLevel()
        {
            // Explicit GatherHere targeting gold (index 1, MinLevel 50) mid-gather should fail.
            var hardGatherable = new Simulation.Gathering.GatherableConfig("gold_ore", SkillType.Mining, 40f, 0.5f, 50);

            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("gold_ore", "Gold Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mixed", "Mixed Mine", 0f, 0f, null, CopperGatherable, hardGatherable);
            map.AddEdge("hub", "mixed", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "mixed");
            _runner = _sim.CurrentGameState.Runners[0];

            // Create micro ruleset that targets gold explicitly (index 1)
            var microRuleset = new Ruleset
            {
                Id = "gold-only",
                Name = "Gold Only",
                Category = RulesetCategory.Gathering,
            };
            microRuleset.Rules.Add(new Rule
            {
                Label = "Gather Gold",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(1),
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(microRuleset);

            // Start with default micro (GatherAny → picks copper)
            var seq = TaskSequence.CreateLoop("mixed", "hub");
            _sim.AssignRunner(_runner.Id, seq, "Test");

            // Tick until gathering
            int ticks = 0;
            while (_runner.State != RunnerState.Gathering && ticks < 50)
            {
                _sim.Tick();
                ticks++;
            }
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex, "Should start on copper");

            // Now swap the Work step's micro ruleset to gold-only mid-gather
            var activeSeq = _sim.CurrentGameState.TaskSequenceLibrary
                .Find(s => s.Id == _runner.TaskSequenceId);
            foreach (var step in activeSeq.Steps)
            {
                if (step.Type == TaskStepType.Work)
                    step.MicroRulesetId = "gold-only";
            }

            GatheringFailed? failed = null;
            _sim.Events.Subscribe<GatheringFailed>(e => failed = e);

            // Tick — micro re-evaluation should try to switch to gold, fail MinLevel check
            for (int i = 0; i < 100; i++) _sim.Tick();

            // Runner should have been stopped by the MinLevel check
            Assert.IsNotNull(failed, "GatheringFailed should fire when mid-gather switch hits MinLevel");
            Assert.AreEqual(GatheringFailureReason.NotEnoughSkill, failed.Value.Reason);
        }

        // ─── GatherBestAvailable tests ───────────────────────────

        private static readonly Simulation.Gathering.GatherableConfig IronGatherable =
            new Simulation.Gathering.GatherableConfig("iron_ore", SkillType.Mining, 40f, 0.5f, 15);

        private static readonly Simulation.Gathering.GatherableConfig MithrilGatherable =
            new Simulation.Gathering.GatherableConfig("mithril_ore", SkillType.Mining, 40f, 0.5f, 30);

        /// <summary>
        /// Helper: set up sim with a multi-tier mine for GatherBestAvailable tests.
        /// Copper (MinLevel 0), Iron (MinLevel 15), Mithril (MinLevel 30).
        /// </summary>
        private void SetupMultiTierMine(int miningLevel)
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("iron_ore", "Iron Ore", ItemCategory.Ore),
                    new ItemDefinition("mithril_ore", "Mithril Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, miningLevel),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Multi Mine", 0f, 0f, null, CopperGatherable, IronGatherable, MithrilGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "mine");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        [Test]
        public void GatherBestAvailable_PicksHighestEligible()
        {
            SetupMultiTierMine(miningLevel: 20); // qualifies for copper (0) and iron (15), not mithril (30)

            var micro = CreateAndRegisterMicroRuleset("best-mining");
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherBestAvailable(SkillType.Mining),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "best-mining");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(1, _runner.Gathering.GatherableIndex,
                "Mining 20 should pick iron (index 1, MinLevel 15) — highest eligible");
        }

        [Test]
        public void GatherBestAvailable_LowSkill_PicksOnlyOption()
        {
            SetupMultiTierMine(miningLevel: 1); // qualifies only for copper (MinLevel 0)

            var micro = CreateAndRegisterMicroRuleset("best-mining-low");
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherBestAvailable(SkillType.Mining),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "best-mining-low");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex,
                "Mining 1 should pick copper (index 0, MinLevel 0) — only eligible");
        }

        [Test]
        public void GatherBestAvailable_HighSkill_PicksBest()
        {
            SetupMultiTierMine(miningLevel: 50); // qualifies for all three

            var micro = CreateAndRegisterMicroRuleset("best-mining-high");
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherBestAvailable(SkillType.Mining),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "best-mining-high");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(2, _runner.Gathering.GatherableIndex,
                "Mining 50 should pick mithril (index 2, MinLevel 30) — highest eligible");
        }

        [Test]
        public void GatherBestAvailable_NoMatchingSkill_GetsStuck()
        {
            SetupMultiTierMine(miningLevel: 10);

            // Use Woodcutting — no gatherables at this mine require Woodcutting
            var micro = CreateAndRegisterMicroRuleset("best-woodcutting");
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherBestAvailable(SkillType.Woodcutting),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "best-woodcutting");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreNotEqual(RunnerState.Gathering, _runner.State,
                "No Woodcutting gatherables at mine — runner should be stuck");
        }

        [Test]
        public void GatherBestAvailable_MidGatherStability()
        {
            SetupMultiTierMine(miningLevel: 20);

            var micro = CreateAndRegisterMicroRuleset("best-stable");
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherBestAvailable(SkillType.Mining),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "best-stable");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            int initialIndex = _runner.Gathering.GatherableIndex;

            // Tick a few times (not enough to produce an item) — index should stay the same
            for (int i = 0; i < 3; i++)
            {
                _sim.Tick();
                if (_runner.State != RunnerState.Gathering) break;
                Assert.AreEqual(initialIndex, _runner.Gathering.GatherableIndex,
                    $"GatherBestAvailable should not switch resources mid-gather (tick {i + 1})");
            }
        }

        [Test]
        public void GatherBestAvailable_TiesBreakByLowestIndex()
        {
            // Two gatherables with the same MinLevel and same skill — should pick lowest index
            var copperA = new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f, 10);
            var copperB = new Simulation.Gathering.GatherableConfig("iron_ore", SkillType.Mining, 40f, 0.5f, 10);

            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("iron_ore", "Iron Ore", ItemCategory.Ore),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 10),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Tied Mine", 0f, 0f, null, copperA, copperB);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "mine");
            _runner = _sim.CurrentGameState.Runners[0];

            var micro = CreateAndRegisterMicroRuleset("best-tied");
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherBestAvailable(SkillType.Mining),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "best-tied");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex,
                "When MinLevel is tied, should pick lowest index (0)");
        }

        // ─── Micro Override (Hot-Wiring) ──────────────────────────────

        [Test]
        public void MicroOverride_TakesPrecedenceOverStepMicro()
        {
            Setup("mine");

            // Create two micro rulesets: one gathers copper (index 0), one gathers tin (index 1)
            var copperMicro = CreateAndRegisterMicroRuleset("copper-micro");
            copperMicro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var tinMicro = CreateAndRegisterMicroRuleset("tin-micro");
            tinMicro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(1),
                Enabled = true,
            });

            // Sequence configured to use copper micro on Work step
            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "copper-micro");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex,
                "Without override, should gather copper (index 0)");

            // Now set override to tin micro
            int workStepIndex = -1;
            for (int i = 0; i < seq.Steps.Count; i++)
                if (seq.Steps[i].Type == TaskStepType.Work) { workStepIndex = i; break; }

            _sim.CommandSetMicroOverride(_runner.Id, workStepIndex, "tin-micro");

            // Action commitment: micro re-eval fires on item completion, not every tick.
            // Tick until an item is produced and the override takes effect.
            int ticks = 0;
            while (_runner.Gathering != null && _runner.Gathering.GatherableIndex == 0
                && ticks < 200)
            {
                _sim.Tick();
                ticks++;
            }

            Assert.AreEqual(1, _runner.Gathering.GatherableIndex,
                "Override should cause runner to gather tin (index 1)");
        }

        [Test]
        public void MicroOverride_NoOverrideFallsBackToStepMicro()
        {
            Setup("mine");

            var copperMicro = CreateAndRegisterMicroRuleset("copper-only");
            copperMicro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "copper-only");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex,
                "No override set — should use step's micro and gather copper");
            Assert.IsFalse(_sim.RunnerHasMicroOverrides(_runner));
        }

        [Test]
        public void MicroOverride_ClearedOnAssignRunner()
        {
            Setup("mine");

            var micro = CreateAndRegisterMicroRuleset("some-micro");
            micro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "some-micro");
            _sim.AssignRunner(_runner.Id, seq);

            // Set override
            _sim.CommandSetMicroOverride(_runner.Id, 1, "some-override-id");
            Assert.IsTrue(_sim.RunnerHasMicroOverrides(_runner));

            // Re-assign (same or different sequence) — overrides cleared
            _sim.AssignRunner(_runner.Id, seq);
            Assert.IsFalse(_sim.RunnerHasMicroOverrides(_runner));
        }

        [Test]
        public void MicroOverride_PersistsAcrossLoops()
        {
            Setup("mine");

            var copperMicro = CreateAndRegisterMicroRuleset("copper-loop");
            copperMicro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var tinMicro = CreateAndRegisterMicroRuleset("tin-loop");
            tinMicro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(1),
                Enabled = true,
            });

            // Create a looping sequence and assign
            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "copper-loop");
            _sim.AssignRunner(_runner.Id, seq);

            // Find the Work step index
            int workStepIndex = -1;
            for (int i = 0; i < seq.Steps.Count; i++)
                if (seq.Steps[i].Type == TaskStepType.Work) { workStepIndex = i; break; }

            // Override to tin
            _sim.CommandSetMicroOverride(_runner.Id, workStepIndex, "tin-loop");

            // Fill inventory to trigger FinishTask → deposit → loop back
            var fillItem = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < _sim.Config.InventorySize - 1; i++)
                _runner.Inventory.TryAdd(fillItem);
            // Tick through: inventory fills → FinishTask → travel to hub → deposit → travel back → gather again
            TickUntil(() => _runner.State == RunnerState.Gathering && _runner.CompletedAtLeastOneCycle, 5000);

            // After looping, override should still be active
            Assert.IsNotNull(_sim.GetRunnerMicroOverrideForStep(_runner, workStepIndex),
                "Override should persist after sequence loops");
            Assert.AreEqual(1, _runner.Gathering.GatherableIndex,
                "After loop, override should still cause tin gathering");
        }

        [Test]
        public void MicroOverride_PerStepIndexIndependence()
        {
            Setup("mine");

            // Set overrides on different step indices
            _sim.CommandSetMicroOverride(_runner.Id, 0, "micro-a");
            _sim.CommandSetMicroOverride(_runner.Id, 2, "micro-b");

            Assert.AreEqual("micro-a", _sim.GetRunnerMicroOverrideForStep(_runner, 0));
            Assert.IsNull(_sim.GetRunnerMicroOverrideForStep(_runner, 1),
                "Step 1 has no override");
            Assert.AreEqual("micro-b", _sim.GetRunnerMicroOverrideForStep(_runner, 2));
        }

        [Test]
        public void CommandClearMicroOverride_RemovesSpecificOverride()
        {
            Setup("mine");

            _sim.CommandSetMicroOverride(_runner.Id, 0, "micro-a");
            _sim.CommandSetMicroOverride(_runner.Id, 2, "micro-b");

            _sim.CommandClearMicroOverride(_runner.Id, 0);

            Assert.IsNull(_sim.GetRunnerMicroOverrideForStep(_runner, 0),
                "Cleared override should be null");
            Assert.AreEqual("micro-b", _sim.GetRunnerMicroOverrideForStep(_runner, 2),
                "Other override should remain");
        }

        [Test]
        public void CommandClearAllMicroOverrides_RemovesAll()
        {
            Setup("mine");

            _sim.CommandSetMicroOverride(_runner.Id, 0, "micro-a");
            _sim.CommandSetMicroOverride(_runner.Id, 1, "micro-b");
            _sim.CommandSetMicroOverride(_runner.Id, 2, "micro-c");

            _sim.CommandClearAllMicroOverrides(_runner.Id);

            Assert.IsFalse(_sim.RunnerHasMicroOverrides(_runner));
        }

        [Test]
        public void CommandSetMicroOverride_ReplacesExistingForSameStep()
        {
            Setup("mine");

            _sim.CommandSetMicroOverride(_runner.Id, 1, "first-micro");
            Assert.AreEqual("first-micro", _sim.GetRunnerMicroOverrideForStep(_runner, 1));

            _sim.CommandSetMicroOverride(_runner.Id, 1, "second-micro");
            Assert.AreEqual("second-micro", _sim.GetRunnerMicroOverrideForStep(_runner, 1));
            Assert.AreEqual(1, _runner.MicroOverrides.Count,
                "Should replace, not add a second entry");
        }

        [Test]
        public void MicroOverride_InvalidOverrideCausesNoMatch()
        {
            Setup("mine");

            var copperMicro = CreateAndRegisterMicroRuleset("valid-micro");
            copperMicro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, "valid-micro");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Override with a non-existent micro ID → let it break
            int workStepIndex = -1;
            for (int i = 0; i < seq.Steps.Count; i++)
                if (seq.Steps[i].Type == TaskStepType.Work) { workStepIndex = i; break; }

            _sim.CommandSetMicroOverride(_runner.Id, workStepIndex, "non-existent-micro");

            // Action commitment: micro re-eval fires on item completion, not every tick.
            // Tick until the runner produces an item and the broken override is detected.
            int ticks = 0;
            while (_runner.State == RunnerState.Gathering && ticks < 200)
            {
                _sim.Tick();
                ticks++;
            }

            Assert.IsNotNull(_runner.ActiveWarning,
                "Invalid override micro should cause a warning (let it break)");
        }

        [Test]
        public void CommandForkTaskSequenceWithOverrides_BakesOverridesIntoNewSequence()
        {
            Setup("mine");

            var copperMicro = CreateAndRegisterMicroRuleset("fork-copper");
            copperMicro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var tinMicro = CreateAndRegisterMicroRuleset("fork-tin");
            tinMicro.Rules.Add(new Rule
            {
                Conditions = { new Condition { Type = ConditionType.Always } },
                Action = AutomationAction.GatherHere(1),
                Enabled = true,
            });

            // Create sequence with copper micro
            var seq = TaskSequence.CreateLoop("mine", "hub");
            seq.Name = "Mining Loop";
            SetWorkStepMicroRuleset(seq, "fork-copper");
            _sim.AssignRunner(_runner.Id, seq);

            string originalSeqId = _runner.TaskSequenceId;

            // Override Work step to tin
            int workStepIndex = -1;
            for (int i = 0; i < seq.Steps.Count; i++)
                if (seq.Steps[i].Type == TaskStepType.Work) { workStepIndex = i; break; }

            _sim.CommandSetMicroOverride(_runner.Id, workStepIndex, "fork-tin");

            // Fork
            _sim.CommandForkTaskSequenceWithOverrides(_runner.Id);

            // Runner should be on a NEW sequence
            Assert.AreNotEqual(originalSeqId, _runner.TaskSequenceId,
                "Runner should be assigned to new forked sequence");

            // Overrides should be cleared
            Assert.IsFalse(_sim.RunnerHasMicroOverrides(_runner),
                "Overrides should be cleared after fork");

            // New sequence's Work step should have tin micro baked in
            var newSeq = _sim.GetRunnerTaskSequence(_runner);
            Assert.IsNotNull(newSeq);
            Assert.That(newSeq.Name, Does.Contain("Tester"),
                "Forked sequence name should contain runner name");

            for (int i = 0; i < newSeq.Steps.Count; i++)
            {
                if (newSeq.Steps[i].Type == TaskStepType.Work)
                {
                    Assert.AreEqual("fork-tin", newSeq.Steps[i].MicroRulesetId,
                        "Forked sequence's Work step should have the override micro baked in");
                }
            }

            // Original sequence should be unchanged
            var origSeq = _sim.FindTaskSequenceInLibrary(originalSeqId);
            Assert.IsNotNull(origSeq, "Original sequence should still exist");
            for (int i = 0; i < origSeq.Steps.Count; i++)
            {
                if (origSeq.Steps[i].Type == TaskStepType.Work)
                {
                    Assert.AreEqual("fork-copper", origSeq.Steps[i].MicroRulesetId,
                        "Original sequence should be unchanged");
                }
            }
        }
    }
}
