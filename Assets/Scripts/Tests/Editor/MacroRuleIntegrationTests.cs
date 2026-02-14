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
        public void MacroRule_BankThreshold_ChangesAssignment()
        {
            Setup("mine");

            // Macro rule: IF BankContains(copper) >= 28 THEN WorkAt(forest)
            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, _config.InventorySize) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentTrip = false, // immediate
                Enabled = true,
            });

            // Start gathering copper
            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until the runner switches to forest
            TickUntil(() =>
                _runner.Assignment != null
                && _runner.Assignment.TargetNodeId == "forest",
                safetyLimit: 5000);

            Assert.IsNotNull(_runner.Assignment);
            Assert.AreEqual("forest", _runner.Assignment.TargetNodeId,
                "Macro rule should have switched assignment to forest after bank threshold");
        }

        // ─── FinishCurrentTrip defers assignment change ─────────

        [Test]
        public void MacroRule_FinishCurrentTrip_DefersToLoopBoundary()
        {
            Setup("mine");

            // Macro rule: IF BankContains(copper) >= 28 THEN WorkAt(forest), finish trip first
            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Switch to forest (deferred)",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, _config.InventorySize) },
                Action = AutomationAction.WorkAt("forest"),
                FinishCurrentTrip = true, // deferred
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until first deposit — rule should fire but be deferred
            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);
            TickUntil(() => deposited != null);

            Assert.IsNotNull(deposited, "Should have deposited");

            // After deposit (loop boundary), PendingAssignment should have been applied
            // Runner should now be heading to forest or already there
            TickUntil(() =>
                _runner.Assignment != null
                && _runner.Assignment.TargetNodeId == "forest",
                safetyLimit: 100);

            Assert.AreEqual("forest", _runner.Assignment.TargetNodeId,
                "Deferred macro rule should apply at loop boundary (after deposit)");
        }

        [Test]
        public void MacroRule_FinishCurrentTrip_PendingAssignmentStored()
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
                FinishCurrentTrip = true,
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner arrives at mine and starts gathering. Rule should fire on ArrivedAtNode
            // but be deferred since FinishCurrentTrip = true.
            // The rule fires on arrival at mine — bank already has enough copper.
            // PendingAssignment should be set.
            Assert.IsNotNull(_runner.PendingAssignment,
                "FinishCurrentTrip rule should store PendingAssignment");
            Assert.AreEqual("forest", _runner.PendingAssignment.TargetNodeId);
        }

        // ─── Immediate macro rule ───────────────────────────────

        [Test]
        public void MacroRule_Immediate_ChangesAssignmentRightAway()
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
                FinishCurrentTrip = false, // immediate
                Enabled = true,
            });

            // Assign to mine — macro rule should fire on the first arrival and redirect
            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner should NOT be mining — should have been redirected to forest
            // (AssignRunner → TravelTo(mine) → arrive → macro fires → AssignRunner(forest))
            // We need to tick until arrival at mine first
            TickUntil(() => _runner.CurrentNodeId == "mine", safetyLimit: 500);

            // Now macro should have fired and reassigned to forest
            Assert.AreEqual("forest", _runner.Assignment?.TargetNodeId,
                "Immediate macro rule should change assignment on arrival");
        }

        // ─── Macro rule: Idle action clears assignment ──────────

        [Test]
        public void MacroRule_IdleAction_ClearsAssignment()
        {
            Setup("mine");

            _sim.CurrentGameState.Bank.Deposit("copper_ore", 100);

            _runner.MacroRuleset = new Ruleset();
            _runner.MacroRuleset.Rules.Add(new Rule
            {
                Label = "Stop when enough copper",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.Idle(),
                FinishCurrentTrip = false,
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until macro fires and clears assignment
            TickUntil(() => _runner.CurrentNodeId == "mine", safetyLimit: 500);

            Assert.IsNull(_runner.Assignment,
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
                FinishCurrentTrip = false,
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until rule fires
            TickUntil(() => _runner.Assignment?.TargetNodeId == "forest", safetyLimit: 500);

            var entries = _sim.CurrentGameState.DecisionLog.GetForRunner(_runner.Id);
            Assert.Greater(entries.Count, 0, "DecisionLog should have entries");

            var entry = entries[0]; // most recent
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
                FinishCurrentTrip = true,
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until pending assignment is set
            TickUntil(() => _runner.PendingAssignment != null, safetyLimit: 500);

            var entries = _sim.CurrentGameState.DecisionLog.GetForRunner(_runner.Id);
            Assert.Greater(entries.Count, 0, "DecisionLog should have entries");

            var entry = entries[0];
            Assert.AreEqual("Deferred switch", entry.RuleLabel);
            Assert.IsTrue(entry.WasDeferred, "Entry should be marked as deferred");
        }

        // ─── Same-assignment suppression ────────────────────────

        [Test]
        public void MacroRule_SameAssignment_DoesNotReassign()
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
                FinishCurrentTrip = false,
                Enabled = true,
            });

            int assignmentChangedCount = 0;
            _sim.Events.Subscribe<AssignmentChanged>(e => assignmentChangedCount++);

            var assignment = Assignment.CreateLoop("mine", "hub");
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
                FinishCurrentTrip = false,
                Enabled = true,
            });

            // Runner 2: no macro rules (empty, default)
            // Should keep gathering copper

            _sim.AssignRunner(_runner.Id, Assignment.CreateLoop("mine", "hub"));
            _sim.AssignRunner(runner2.Id, Assignment.CreateLoop("mine", "hub"));

            // Tick until runner1 switches
            TickUntil(() => _runner.Assignment?.TargetNodeId == "forest", safetyLimit: 500);

            Assert.AreEqual("forest", _runner.Assignment?.TargetNodeId,
                "Runner 1 should have switched to forest");
            Assert.AreEqual("mine", runner2.Assignment?.TargetNodeId,
                "Runner 2 should still be mining (no macro rules)");
        }
    }
}
