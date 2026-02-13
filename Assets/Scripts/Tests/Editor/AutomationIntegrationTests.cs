using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class AutomationIntegrationTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f);

        private static readonly Simulation.Gathering.GatherableConfig OakGatherable =
            new Simulation.Gathering.GatherableConfig("oak_log", SkillType.Woodcutting, 40f, 0.5f);

        private void SetupSingleRunner(string startNode = "mine", int miningLevel = 1)
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("oak_log", "Oak Log", ItemCategory.Log),
                },
                // Disable periodic checks by default to avoid interference in tests
                AutomationPeriodicCheckInterval = 0,
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "TestRunner" }
                    .WithSkill(SkillType.Mining, miningLevel)
                    .WithSkill(SkillType.Woodcutting, miningLevel),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f, CopperGatherable);
            map.AddNode("forest", "Forest", 10f, 0f, OakGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.AddEdge("hub", "forest", 8f);
            map.AddEdge("mine", "forest", 6f);
            map.Initialize();

            _sim.StartNewGame(defs, map, startNode);
            _runner = _sim.CurrentGameState.Runners[0];
        }

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

        // ─── Default ruleset backward compatibility ──────────────────

        [Test]
        public void DefaultRuleset_ReplicatesAutoReturn()
        {
            SetupSingleRunner();
            _sim.CommandGather(_runner.Id);

            // Tick until inventory full — should start traveling to hub (same as old behavior)
            TickUntilInventoryFull();

            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
            Assert.IsNotNull(_runner.Gathering);
            Assert.AreEqual(GatheringSubState.TravelingToBank, _runner.Gathering.SubState);
        }

        [Test]
        public void DefaultRuleset_FullDepositLoop_ResumesGathering()
        {
            SetupSingleRunner();
            _sim.CommandGather(_runner.Id);

            // Tick until runner completes a full loop: gather → deposit → return → gather
            TickUntil(() =>
                _runner.CurrentNodeId == "mine"
                && _runner.State == RunnerState.Gathering
                && _runner.Gathering?.SubState == GatheringSubState.Gathering
                && _sim.CurrentGameState.Bank.CountItem("copper_ore") > 0);

            Assert.AreEqual("mine", _runner.CurrentNodeId);
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(_config.InventorySize, _sim.CurrentGameState.Bank.CountItem("copper_ore"));
        }

        [Test]
        public void DefaultRuleset_PublishesAutomationRuleFired()
        {
            SetupSingleRunner();
            _sim.CommandGather(_runner.Id);

            AutomationRuleFired? fired = null;
            _sim.Events.Subscribe<AutomationRuleFired>(e => fired = e);

            TickUntilInventoryFull();

            Assert.IsNotNull(fired, "AutomationRuleFired should be published when default rule fires");
            Assert.AreEqual(0, fired.Value.RuleIndex);
            Assert.AreEqual("Deposit when full", fired.Value.RuleLabel);
            Assert.AreEqual(ActionType.DepositAndResume, fired.Value.ActionType);
            Assert.AreEqual("inventory_full", fired.Value.TriggerReason);
            Assert.IsFalse(fired.Value.WasDeferred);
        }

        // ─── Custom rules override defaults ─────────────────────────

        [Test]
        public void CustomRule_OverridesDefaultDepositAndResume()
        {
            SetupSingleRunner();

            // Custom ruleset: when inventory full, go idle instead of depositing
            _runner.Ruleset = new Ruleset();
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Stop when full",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.Idle(),
                FinishCurrentTrip = false,
            });

            _sim.CommandGather(_runner.Id);
            TickUntilInventoryFull();

            // Runner should be idle (not traveling to hub)
            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.IsNull(_runner.Gathering, "Gathering should be cancelled by Idle action");
        }

        [Test]
        public void BankContainsRule_SwitchesGatheringTask()
        {
            SetupSingleRunner();

            // Add rules: if bank has >= 28 copper, switch to forest; default deposit
            _runner.Ruleset = new Ruleset();
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Switch to oak",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 28) },
                Action = AutomationAction.GatherAt("forest"),
                FinishCurrentTrip = true, // finish deposit cycle first
            });
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Deposit when full",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.DepositAndResume(),
                FinishCurrentTrip = false,
            });

            _sim.CommandGather(_runner.Id);

            // After first deposit (28 copper in bank), the BankContains rule should fire
            // on the periodic check or next inventory_full. The runner should eventually
            // end up at the forest.
            TickUntil(() => _runner.CurrentNodeId == "forest" && _runner.State == RunnerState.Gathering,
                safetyLimit: 5000);

            Assert.AreEqual("forest", _runner.CurrentNodeId);
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual("forest", _runner.Gathering.NodeId);
        }

        // ─── Pending action mechanism ────────────────────────────────

        [Test]
        public void PendingAction_FiresAfterDeposit()
        {
            SetupSingleRunner();

            // Rule: always go to forest (FinishCurrentTrip=true) + default deposit
            _runner.Ruleset = new Ruleset();
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Go to forest",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.GatherAt("forest"),
                FinishCurrentTrip = true,
            });

            _sim.CommandGather(_runner.Id);

            // Inventory fills → rule fires with FinishCurrentTrip → PendingAction stored → deposit cycle starts
            TickUntilInventoryFull();

            // Runner should be heading to hub to deposit (FinishCurrentTrip + inventory_full = deposit first)
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
            Assert.IsNotNull(_runner.PendingAction);
            Assert.AreEqual(ActionType.GatherAt, _runner.PendingAction.Type);

            // Tick until pending action executes — runner should end up at forest
            AutomationPendingActionExecuted? pendingExecuted = null;
            _sim.Events.Subscribe<AutomationPendingActionExecuted>(e => pendingExecuted = e);

            TickUntil(() => _runner.CurrentNodeId == "forest" && _runner.State == RunnerState.Gathering,
                safetyLimit: 5000);

            Assert.IsNotNull(pendingExecuted, "AutomationPendingActionExecuted should be published");
            Assert.AreEqual("forest", _runner.CurrentNodeId);
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
        }

        // ─── GatherAt travel-then-gather compound action ────────────

        [Test]
        public void GatherAt_AtTargetNode_StartsGathering()
        {
            SetupSingleRunner();

            // Execute GatherAt for the node the runner is already at
            ActionExecutor.Execute(AutomationAction.GatherAt("mine"), _runner, _sim);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual("mine", _runner.Gathering.NodeId);
        }

        [Test]
        public void GatherAt_DifferentNode_TravelsThenGathers()
        {
            SetupSingleRunner();

            // Execute GatherAt for a different node
            ActionExecutor.Execute(AutomationAction.GatherAt("forest"), _runner, _sim);

            // Should be traveling to forest
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("forest", _runner.Travel.ToNodeId);
            Assert.IsNotNull(_runner.PendingAction);
            Assert.AreEqual(ActionType.GatherAt, _runner.PendingAction.Type);
            Assert.AreEqual("forest", _runner.PendingAction.StringParam);

            // Tick until arrival + gathering starts
            TickUntil(() => _runner.State == RunnerState.Gathering, safetyLimit: 2000);

            Assert.AreEqual("forest", _runner.CurrentNodeId);
            Assert.AreEqual("forest", _runner.Gathering.NodeId);
            Assert.IsNull(_runner.PendingAction, "PendingAction should be cleared after execution");
        }

        // ─── SkillLevel trigger fires on level up ───────────────────

        [Test]
        public void SkillLevelTrigger_FiresOnLevelUp()
        {
            SetupSingleRunner();

            // Rule: when Mining >= 2, switch to forest
            _runner.Ruleset = new Ruleset();
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Switch at Mining 2",
                Conditions = { Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 2) },
                Action = AutomationAction.GatherAt("forest"),
                FinishCurrentTrip = false, // switch immediately
            });
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Deposit when full",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.DepositAndResume(),
                FinishCurrentTrip = false,
            });

            // Set XP close to level 2 so it triggers quickly
            var mining = _runner.GetSkill(SkillType.Mining);
            float xpToNext = mining.GetXpToNextLevel(_config);
            mining.Xp = xpToNext - 1f;

            _sim.CommandGather(_runner.Id);

            AutomationRuleFired? fired = null;
            _sim.Events.Subscribe<AutomationRuleFired>(e =>
            {
                if (e.TriggerReason == "skill_level_up")
                    fired = e;
            });

            // Tick until the level-up rule fires
            TickUntil(() => fired != null, safetyLimit: 200);

            Assert.IsNotNull(fired, "SkillLevel rule should fire on level up");
            Assert.AreEqual("Switch at Mining 2", fired.Value.RuleLabel);
        }

        // ─── FleeToHub ignores FinishCurrentTrip ────────────────────

        [Test]
        public void FleeToHub_IgnoresFinishCurrentTrip()
        {
            SetupSingleRunner();

            // Rule: always flee (even though FinishCurrentTrip is true, FleeToHub ignores it)
            _runner.Ruleset = new Ruleset();
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Flee always",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FleeToHub(),
                FinishCurrentTrip = true, // should be ignored for FleeToHub
            });

            _sim.CommandGather(_runner.Id);

            // Enable periodic check to trigger the rule
            _config.AutomationPeriodicCheckInterval = 1;
            _sim.Tick();

            // FleeToHub should execute immediately — runner should be traveling to hub
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
            Assert.IsNull(_runner.Gathering, "Gathering should be cancelled by FleeToHub");
            Assert.IsNull(_runner.PendingAction, "FleeToHub should not defer");
        }

        // ─── Periodic check catches BankContains ─────────────────────

        [Test]
        public void PeriodicCheck_CatchesBankChanges()
        {
            SetupSingleRunner();

            // Rule: if bank has >= 10 copper, switch to forest
            _runner.Ruleset = new Ruleset();
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Switch on bank threshold",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 10) },
                Action = AutomationAction.GatherAt("forest"),
                FinishCurrentTrip = false,
            });
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Deposit when full",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.DepositAndResume(),
                FinishCurrentTrip = false,
            });

            _sim.CommandGather(_runner.Id);

            // Simulate external deposit to bank
            _sim.CurrentGameState.Bank.Deposit("copper_ore", 10);

            // Enable periodic check
            _config.AutomationPeriodicCheckInterval = 1;

            AutomationRuleFired? fired = null;
            _sim.Events.Subscribe<AutomationRuleFired>(e =>
            {
                if (e.RuleLabel == "Switch on bank threshold")
                    fired = e;
            });

            // Tick — periodic check should catch the bank change
            _sim.Tick();

            Assert.IsNotNull(fired, "Periodic check should detect BankContains change");
        }

        // ─── Null/empty ruleset is safe ──────────────────────────────

        [Test]
        public void NullRuleset_InventoryFull_StillDeposits()
        {
            SetupSingleRunner();
            _runner.Ruleset = null;
            _sim.CommandGather(_runner.Id);

            // Inventory fills — should still deposit (safety fallback)
            TickUntilInventoryFull();

            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
        }

        [Test]
        public void EmptyRuleset_InventoryFull_StillDeposits()
        {
            SetupSingleRunner();
            _runner.Ruleset = new Ruleset(); // empty, no rules
            _sim.CommandGather(_runner.Id);

            TickUntilInventoryFull();

            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
        }

        // ─── Arrival automation for non-gathering travel ─────────────

        [Test]
        public void Arrival_EvaluatesRules_WhenIdleWithNoGathering()
        {
            SetupSingleRunner("hub");

            // Rule: at mine → start gathering
            _runner.Ruleset = new Ruleset();
            _runner.Ruleset.Rules.Add(new Rule
            {
                Label = "Gather at mine",
                Conditions = { Condition.AtNode("mine") },
                Action = AutomationAction.GatherAt("mine"),
                FinishCurrentTrip = false,
            });

            // Send runner to mine via travel
            _sim.CommandTravel(_runner.Id, "mine");

            // Tick until arrival
            TickUntil(() => _runner.CurrentNodeId == "mine", safetyLimit: 2000);

            // On arrival, the AtNode rule should fire and start gathering
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual("mine", _runner.Gathering.NodeId);
        }

        // ─── Decision log integration ────────────────────────────────

        [Test]
        public void DecisionLog_RecordsRuleFirings()
        {
            SetupSingleRunner();
            _sim.CommandGather(_runner.Id);

            TickUntilInventoryFull();

            var log = _sim.CurrentGameState.DecisionLog;
            Assert.Greater(log.Entries.Count, 0, "Decision log should have entries");

            var entry = log.Entries[log.Entries.Count - 1];
            Assert.AreEqual(_runner.Id, entry.RunnerId);
            Assert.AreEqual("Deposit when full", entry.RuleLabel);
            Assert.AreEqual(ActionType.DepositAndResume, entry.ActionType);
            Assert.AreEqual("inventory_full", entry.TriggerReason);
        }

        [Test]
        public void DecisionLog_FilterByRunner()
        {
            SetupSingleRunner();
            _sim.CommandGather(_runner.Id);

            TickUntilInventoryFull();

            var entries = _sim.CurrentGameState.DecisionLog.GetForRunner(_runner.Id);
            Assert.Greater(entries.Count, 0);

            var nonexistent = _sim.CurrentGameState.DecisionLog.GetForRunner("nonexistent");
            Assert.AreEqual(0, nonexistent.Count);
        }
    }
}
