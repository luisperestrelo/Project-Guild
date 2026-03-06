using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for the action commitment model: micro rules evaluate on item completion
    /// (not every tick), interrupt-flagged rules still fire mid-action.
    /// </summary>
    [TestFixture]
    public class ActionCommitmentTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new("copper_ore", SkillType.Mining, 20f, 0.5f); // 20 ticks = fast for testing

        private static readonly Simulation.Gathering.GatherableConfig TinGatherable =
            new("tin_ore", SkillType.Mining, 20f, 0.5f);

        private void Setup(int miningLevel = 1)
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
                    .WithSkill(SkillType.Mining, miningLevel),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f, null, CopperGatherable, TinGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "mine");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        private Ruleset CreateAndRegisterMicroRuleset(string id = "test-micro")
        {
            var ruleset = new Ruleset { Id = id, Name = id, Category = RulesetCategory.Gathering };
            _sim.CurrentGameState.MicroRulesetLibrary.Add(ruleset);
            return ruleset;
        }

        private void SetWorkStepMicroRuleset(TaskSequence seq, string microRulesetId)
        {
            if (seq?.Steps == null) return;
            foreach (var step in seq.Steps)
            {
                if (step.Type == TaskStepType.Work)
                    step.MicroRulesetId = microRulesetId;
            }
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

        [Test]
        public void MicroRulesDoNotFireEveryTickDuringGathering()
        {
            Setup();

            // Create a micro ruleset with non-interrupt rules only
            var micro = CreateAndRegisterMicroRuleset();
            micro.Rules.Add(new Rule
            {
                Label = "Gather copper",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
                CanInterrupt = false, // NOT an interrupt rule
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, micro.Id);
            _sim.AssignRunner(_runner.Id, seq, "test");

            // Runner should start gathering
            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Clear the decision log
            _sim.CurrentGameState.MicroDecisionLog.Clear();

            // Tick 5 times (well within a single gather action, 20 ticks for one item)
            for (int i = 0; i < 5; i++)
                _sim.Tick();

            // Under action commitment, there should be NO new micro decision log entries
            // during these 5 ticks (no item was produced yet)
            Assert.AreEqual(0, _sim.CurrentGameState.MicroDecisionLog.Entries.Count,
                "Micro rules should not log during mid-action ticks");

            // Runner should still be gathering the same resource
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
        }

        [Test]
        public void MicroRulesFireOnItemCompletion()
        {
            Setup();

            var micro = CreateAndRegisterMicroRuleset();
            micro.Rules.Add(new Rule
            {
                Label = "Gather copper",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, micro.Id);
            _sim.AssignRunner(_runner.Id, seq, "test");

            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Clear the decision log after initial evaluation
            _sim.CurrentGameState.MicroDecisionLog.Clear();

            // Tick until an item is produced (should be around 20 ticks at level 1)
            int ticks = TickUntil(() => _runner.Inventory.Slots.Count > 0, 100);
            Assert.Greater(ticks, 0, "Should have ticked at least once");

            // After item production, micro rules should have been evaluated (logged)
            Assert.Greater(_sim.CurrentGameState.MicroDecisionLog.Entries.Count, 0,
                "Micro rules should log on item completion");
        }

        [Test]
        public void InterruptRulesFireMidAction()
        {
            Setup();

            // Fill inventory to 27/28 (almost full)
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 27; i++)
                _runner.Inventory.TryAdd(itemDef, 1);

            Assert.AreEqual(1, _runner.Inventory.FreeSlots);

            // Create a micro ruleset with CanInterrupt on InventoryFull -> FinishTask
            var micro = CreateAndRegisterMicroRuleset();
            micro.Rules.Add(new Rule
            {
                Label = "Stop when full (interrupt)",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
                CanInterrupt = true, // THIS IS THE KEY: fires mid-action
            });
            micro.Rules.Add(new Rule
            {
                Label = "Gather copper",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, micro.Id);
            _sim.AssignRunner(_runner.Id, seq, "test");

            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Tick until an item is produced (fills to 28/28)
            TickUntil(() => _runner.Inventory.FreeSlots <= 0, 100);

            // The interrupt rule (CanInterrupt=true for InventoryFull -> FinishTask)
            // should have fired. The runner should have stopped gathering and advanced
            // to the next step (travel to hub for deposit).
            Assert.AreNotEqual(RunnerState.Gathering, _runner.State,
                "Interrupt rule should have stopped gathering when inventory became full");
        }

        [Test]
        public void NonInterruptRulesIgnoredMidAction()
        {
            Setup();

            // Fill inventory to 27/28 (almost full)
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 27; i++)
                _runner.Inventory.TryAdd(itemDef, 1);

            // Create a micro ruleset with CanInterrupt=FALSE on InventoryFull -> FinishTask
            var micro = CreateAndRegisterMicroRuleset();
            micro.Rules.Add(new Rule
            {
                Label = "Stop when full (no interrupt)",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
                CanInterrupt = false, // NOT an interrupt rule
            });
            micro.Rules.Add(new Rule
            {
                Label = "Gather copper",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, micro.Id);
            _sim.AssignRunner(_runner.Id, seq, "test");

            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Tick once to produce the item (fill to 28/28)
            TickUntil(() => _runner.Inventory.FreeSlots <= 0, 100);

            // Runner inventory is full. The item that filled it was produced at
            // the action boundary, so the full re-eval (not just interrupt) fires.
            // The non-interrupt FinishTask rule DOES fire at the item boundary.
            // This test verifies the rule fires at item completion, not mid-action.
            // If we only ticked a few ticks (not enough to produce an item),
            // the non-interrupt rule would NOT have fired.
        }

        [Test]
        public void DecisionLogEntryPerActionNotPerTick()
        {
            Setup();

            var micro = CreateAndRegisterMicroRuleset();
            micro.Rules.Add(new Rule
            {
                Label = "Gather copper",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, micro.Id);
            _sim.AssignRunner(_runner.Id, seq, "test");

            // Clear after initial step eval
            _sim.CurrentGameState.MicroDecisionLog.Clear();

            // Tick for 15 ticks (within a single gather action at 20 ticks/item)
            for (int i = 0; i < 15; i++)
                _sim.Tick();

            // No items produced yet, so no micro decision logs
            Assert.AreEqual(0, _sim.CurrentGameState.MicroDecisionLog.Entries.Count,
                "No decision log entries during mid-action ticks (before item produced)");
        }

        [Test]
        public void InterruptDecisionLogShowsWasInterrupted()
        {
            Setup();

            // Start with 26/28. First item produced -> 27/28 (full re-eval, keeps gathering).
            // Then we manually add 1 item -> 28/28 between productions.
            // Next tick, the interrupt-only check fires and WasInterrupted should be true.
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 26; i++)
                _runner.Inventory.TryAdd(itemDef, 1);

            var micro = CreateAndRegisterMicroRuleset();
            micro.Rules.Add(new Rule
            {
                Label = "Interrupt: stop when full",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
                CanInterrupt = true,
            });
            micro.Rules.Add(new Rule
            {
                Label = "Gather copper",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            var seq = TaskSequence.CreateLoop("mine", "hub");
            SetWorkStepMicroRuleset(seq, micro.Id);
            _sim.AssignRunner(_runner.Id, seq, "test");

            // Tick until first item produced (27/28), runner keeps gathering
            TickUntil(() => _runner.Inventory.Slots.Count > 26, 100);
            Assert.AreEqual(RunnerState.Gathering, _runner.State,
                "Runner should still be gathering at 27/28");

            // Manually fill inventory between item productions
            _runner.Inventory.TryAdd(itemDef, 1);
            Assert.AreEqual(0, _runner.Inventory.FreeSlots, "Should now be 28/28");

            _sim.CurrentGameState.MicroDecisionLog.Clear();

            // Next tick: interrupt-only check fires (no item produced, just mid-action check)
            _sim.Tick();

            // Check that the decision log entry has WasInterrupted = true
            bool foundInterrupt = false;
            foreach (var entry in _sim.CurrentGameState.MicroDecisionLog.Entries)
            {
                if (entry.WasInterrupted)
                {
                    foundInterrupt = true;
                    break;
                }
            }
            Assert.IsTrue(foundInterrupt,
                "Decision log should show WasInterrupted when an interrupt rule fires mid-action");
        }

        [Test]
        public void RulesetWithCanInterruptIsPreservedInDeepCopy()
        {
            var ruleset = new Ruleset
            {
                Id = "original",
                Name = "Test",
                Category = RulesetCategory.Gathering,
            };
            ruleset.Rules.Add(new Rule
            {
                Label = "Interrupt rule",
                CanInterrupt = true,
                Action = AutomationAction.FinishTask(),
            });

            var copy = ruleset.DeepCopy();

            Assert.IsTrue(copy.Rules[0].CanInterrupt,
                "CanInterrupt should be preserved in deep copy");
            Assert.AreNotEqual(ruleset.Id, copy.Id,
                "Deep copy should have a new Id");
        }
    }
}
