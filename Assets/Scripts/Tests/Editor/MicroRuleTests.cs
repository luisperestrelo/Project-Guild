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
            map.AddNode("mine", "Mine", 0f, 0f, CopperGatherable, TinGatherable);
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
            _runner.MicroRuleset = ruleset; // legacy field for backward compat
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
            // In production, LoadState migrates to defaults before gameplay.
            _runner.MicroRuleset = null;

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
            map.AddNode("mine", "Mine", 0f, 0f, CopperGatherable);
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
            map.AddNode("mine", "Mine", 0f, 0f, hardGatherable);
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
    }
}
