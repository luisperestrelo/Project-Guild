using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class MacroRuleIntegrationTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f);

        private static readonly Simulation.Gathering.GatherableConfig OakGatherable =
            new Simulation.Gathering.GatherableConfig("oak_log", SkillType.Woodcutting, 40f, 0.5f);

        private void Setup(string startNodeId = "hub")
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("oak_log", "Oak Log", ItemCategory.Log),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
                    .WithSkill(SkillType.Mining, 1)
                    .WithSkill(SkillType.Woodcutting, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f, CopperGatherable);
            map.AddNode("forest", "Forest", 10f, 0f, OakGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.AddEdge("hub", "forest", 8f);
            map.AddEdge("mine", "forest", 10f);
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

        // ─── Macro rule fires on condition ──────────────────────

        [Test]
        public void MacroRule_BankThreshold_ChangesTaskSequence()
        {
            Setup("mine");

            // Macro rule: IF BankContains(copper) >= 28 THEN WorkAt(forest)
            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, _config.InventorySize) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = false, // immediate
                Enabled = true,
            });

            // Start gathering copper
            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until the runner switches to forest
            TickUntil(() =>
                _runner.TaskSequence != null
                && _runner.TaskSequence.TargetNodeId == "forest",
                safetyLimit: 5000);

            Assert.IsNotNull(_runner.TaskSequence);
            Assert.AreEqual("forest", _runner.TaskSequence.TargetNodeId,
                "Macro rule should have switched assignment to forest after bank threshold");
        }

        // ─── FinishCurrentSequence defers assignment change ─────────

        [Test]
        public void MacroRule_FinishCurrentSequence_DefersToLoopBoundary()
        {
            Setup("mine");

            // Macro rule: IF BankContains(copper) >= 28 THEN WorkAt(forest), finish trip first
            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest (deferred)",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, _config.InventorySize) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = true, // deferred
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until first deposit — rule should fire but be deferred
            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);
            TickUntil(() => deposited != null);

            Assert.IsNotNull(deposited, "Should have deposited");

            // After deposit (loop boundary), PendingTaskSequence should have been applied
            // Runner should now be heading to forest or already there
            TickUntil(() =>
                _runner.TaskSequence != null
                && _runner.TaskSequence.TargetNodeId == "forest",
                safetyLimit: 100);

            Assert.AreEqual("forest", _runner.TaskSequence.TargetNodeId,
                "Deferred macro rule should apply at loop boundary (after deposit)");
        }

        [Test]
        public void MacroRule_FinishCurrentSequence_PendingTaskSequenceStored()
        {
            Setup("mine");

            // Add enough to bank so the rule fires immediately on deposit
            _sim.CurrentGameState.Bank.Deposit("copper_ore", _config.InventorySize);

            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, _config.InventorySize) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = true,
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner arrives at mine and starts gathering. Rule should fire on ArrivedAtNode
            // but be deferred since FinishCurrentSequence = true.
            // The rule fires on arrival at mine — bank already has enough copper.
            // PendingTaskSequence should be set.
            Assert.IsNotNull(_runner.PendingTaskSequence,
                "FinishCurrentSequence rule should store PendingTaskSequence");
            Assert.AreEqual("forest", _runner.PendingTaskSequence.TargetNodeId);
        }

        // ─── Immediate macro rule ───────────────────────────────

        [Test]
        public void MacroRule_Immediate_ChangesTaskSequenceRightAway()
        {
            Setup("mine");

            // Pre-fill bank so rule fires immediately
            _sim.CurrentGameState.Bank.Deposit("copper_ore", 100);

            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest now",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = false, // immediate
                Enabled = true,
            });

            // Assign to mine — macro rule should fire on the first arrival and redirect
            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner should NOT be mining — should have been redirected to forest
            // (AssignRunner → TravelTo(mine) → arrive → macro fires → AssignRunner(forest))
            // We need to tick until arrival at mine first
            TickUntil(() => _runner.CurrentNodeId == "mine", safetyLimit: 500);

            // Now macro should have fired and reassigned to forest
            Assert.AreEqual("forest", _runner.TaskSequence?.TargetNodeId,
                "Immediate macro rule should change assignment on arrival");
        }

        // ─── Macro rule: Idle action clears assignment ──────────

        [Test]
        public void MacroRule_IdleAction_ClearsTaskSequence()
        {
            Setup("mine");

            _sim.CurrentGameState.Bank.Deposit("copper_ore", 100);

            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Stop when enough copper",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.Idle(),
                FinishCurrentSequence = false,
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until macro fires and clears assignment
            TickUntil(() => _runner.CurrentNodeId == "mine", safetyLimit: 500);

            Assert.IsNull(_runner.TaskSequence,
                "Idle macro rule should clear assignment");
            Assert.AreEqual(RunnerState.Idle, _runner.State);
        }

        // ─── DecisionLog records macro fires ────────────────────

        [Test]
        public void DecisionLog_RecordsMacroRuleFire()
        {
            Setup("mine");

            _sim.CurrentGameState.Bank.Deposit("copper_ore", 100);

            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = false,
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until rule fires
            TickUntil(() => _runner.TaskSequence?.TargetNodeId == "forest", safetyLimit: 500);

            var entries = _sim.CurrentGameState.DecisionLog.GetForRunner(_runner.Id, DecisionLayer.Macro);
            Assert.Greater(entries.Count, 0, "DecisionLog should have macro entries");

            var entry = entries[0]; // most recent macro
            Assert.AreEqual("Switch to forest", entry.RuleLabel);
            Assert.AreEqual(ActionType.WorkAt, entry.ActionType);
            Assert.IsFalse(entry.WasDeferred);
        }

        [Test]
        public void DecisionLog_RecordsDeferredFire()
        {
            Setup("mine");

            _sim.CurrentGameState.Bank.Deposit("copper_ore", 100);

            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Deferred switch",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = true,
                Enabled = true,
            });

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until pending assignment is set
            TickUntil(() => _runner.PendingTaskSequence != null, safetyLimit: 500);

            var entries = _sim.CurrentGameState.DecisionLog.GetForRunner(_runner.Id, DecisionLayer.Macro);
            Assert.Greater(entries.Count, 0, "DecisionLog should have macro entries");

            var entry = entries[0];
            Assert.AreEqual("Deferred switch", entry.RuleLabel);
            Assert.IsTrue(entry.WasDeferred, "Entry should be marked as deferred");
        }

        // ─── Same-assignment suppression ────────────────────────

        [Test]
        public void MacroRule_SameSequence_DoesNotReassign()
        {
            Setup("mine");

            _sim.CurrentGameState.Bank.Deposit("copper_ore", 100);

            // Rule says "gather at mine" — same as current assignment
            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Stay at mine",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.WorkAt("mine"),
                FinishCurrentSequence = false,
                Enabled = true,
            });

            int assignmentChangedCount = 0;
            _sim.Events.Subscribe<TaskSequenceChanged>(e => assignmentChangedCount++);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            int initialCount = assignmentChangedCount;

            // Tick through a full gather loop
            TickUntil(() => _sim.CurrentGameState.Bank.CountItem("copper_ore") > 100, safetyLimit: 5000);

            // Should not have re-assigned (beyond the initial AssignRunner call)
            Assert.AreEqual(initialCount, assignmentChangedCount,
                "Macro rule matching current assignment should not trigger reassignment");
        }

        // ─── Multiple runners evaluate independently ────────────

        [Test]
        public void MacroRules_MultipleRunners_EvaluateIndependently()
        {
            Setup("mine");

            // Add a second runner
            var runner2 = RunnerFactory.CreateFromDefinition(
                new RunnerFactory.RunnerDefinition { Name = "Runner2" }
                    .WithSkill(SkillType.Mining, 1)
                    .WithSkill(SkillType.Woodcutting, 1),
                "mine", _config.InventorySize, config: _config);
            _sim.AddRunner(runner2);

            _sim.CurrentGameState.Bank.Deposit("copper_ore", 100);

            // Runner 1: macro rule to switch to forest
            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = false,
                Enabled = true,
            });

            // Runner 2: no macro rules (empty, default)
            // Should keep gathering copper

            _sim.AssignRunner(_runner.Id, TaskSequence.CreateLoop("mine", "hub"));
            _sim.AssignRunner(runner2.Id, TaskSequence.CreateLoop("mine", "hub"));

            // Tick until runner1 switches
            TickUntil(() => _runner.TaskSequence?.TargetNodeId == "forest", safetyLimit: 500);

            Assert.AreEqual("forest", _runner.TaskSequence?.TargetNodeId,
                "Runner 1 should have switched to forest");
            Assert.AreEqual("mine", runner2.TaskSequence?.TargetNodeId,
                "Runner 2 should still be mining (no macro rules)");
        }

        // ─── FinishCurrentSequence degrade ────────────────────

        [Test]
        public void MacroRule_FinishCurrentSequence_DegradesToImmediate_WhenNoActiveSequence()
        {
            // Idle runner, no task sequence. Macro rule with FinishCurrentSequence=true fires
            // on the idle tick. Since there's no active sequence to "finish," it should
            // degrade to immediate and assign right away.
            Setup("hub");

            Assert.IsNull(_runner.TaskSequence);

            // Pre-fill bank so rule condition matches
            for (int i = 0; i < 50; i++)
                _sim.CurrentGameState.Bank.Deposit("copper_ore", 1);

            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Deferred but no sequence",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = true, // Should degrade to immediate
                Enabled = true,
            });

            // One tick — idle runner evaluates macro rules, rule fires, degrades to immediate
            _sim.Tick();

            Assert.AreEqual("forest", _runner.TaskSequence?.TargetNodeId,
                "Deferred rule with no active sequence should degrade to immediate");
            Assert.IsNull(_runner.PendingTaskSequence,
                "Should not have stored as pending (applied immediately)");
        }

        // ─── Non-looping sequence completion ──────────────────

        [Test]
        public void NonLoopingSequence_PublishesCompletedEvent()
        {
            Setup("hub");

            TaskSequenceCompleted? completed = null;
            _sim.Events.Subscribe<TaskSequenceCompleted>(e => completed = e);

            // Create a non-looping sequence: TravelTo(hub) — runner is already at hub, instant
            var seq = new TaskSequence
            {
                Name = "One-trip",
                TargetNodeId = "hub",
                Loop = false,
                CurrentStepIndex = 0,
                Steps = new System.Collections.Generic.List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "hub"),
                },
            };
            _sim.AssignRunner(_runner.Id, seq);

            // Runner is at hub, TravelTo(hub) skips, AdvanceStep returns false → completed
            Assert.IsNotNull(completed, "TaskSequenceCompleted should fire for non-looping sequence");
            Assert.AreEqual("One-trip", completed.Value.SequenceName);
            Assert.IsNull(_runner.TaskSequence, "TaskSequence should be cleared after completion");
            Assert.AreEqual(RunnerState.Idle, _runner.State);
        }

        // ─── Decision Log: micro entries ──────────────────────

        [Test]
        public void DecisionLog_RecordsMicroDecisions()
        {
            Setup("mine");

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner should start gathering — micro rule fires GatherHere
            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            var microEntries = _sim.CurrentGameState.DecisionLog.GetForRunner(
                _runner.Id, DecisionLayer.Micro);
            Assert.Greater(microEntries.Count, 0, "Micro decisions should be logged");
            Assert.AreEqual(DecisionLayer.Micro, microEntries[0].Layer);
            Assert.IsTrue(microEntries[0].ActionDetail.Contains("GatherHere"),
                "Micro decision should show GatherHere action");
        }

        // ─── Macro rules fire every tick (mid-gather interrupt) ───

        [Test]
        public void MacroRule_Immediate_InterruptsMidGather()
        {
            Setup("mine");

            // Start gathering at mine
            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);
            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Tick a few times to confirm gathering is happening (inventory not full yet)
            for (int i = 0; i < 5; i++)
                _sim.Tick();
            Assert.AreEqual(RunnerState.Gathering, _runner.State,
                "Runner should still be gathering (inventory not full)");
            Assert.Less(_runner.Inventory.CountItem("copper_ore"), _config.InventorySize,
                "Inventory should not be full yet");

            // Now add an Immediate macro rule with a condition that's already true
            _sim.CurrentGameState.Bank.Deposit("copper_ore", 100);
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest now",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentSequence = false, // Immediate
                Enabled = true,
            });

            // One tick — macro eval fires at top of TickRunner, interrupts gathering
            _sim.Tick();

            Assert.AreEqual("forest", _runner.TaskSequence?.TargetNodeId,
                "Immediate macro rule should interrupt mid-gather and switch to forest");
            Assert.AreNotEqual(RunnerState.Gathering, _runner.State,
                "Runner should no longer be gathering at the mine");
        }
    }
}
