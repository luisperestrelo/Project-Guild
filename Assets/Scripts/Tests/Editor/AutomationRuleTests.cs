using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class AutomationRuleTests
    {
        private Runner _runner;
        private GameState _gameState;
        private SimulationConfig _config;
        private EvaluationContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _config = new SimulationConfig
            {
                InventorySize = 28,
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                },
            };

            _runner = new Runner
            {
                Id = "test-runner",
                Name = "TestRunner",
                State = RunnerState.Idle,
                CurrentNodeId = "mine",
                Inventory = new Inventory(28),
            };

            _gameState = new GameState();
            _gameState.Runners.Add(_runner);

            _ctx = new EvaluationContext(_runner, _gameState, _config);
        }

        // ─── Single rule evaluation ─────────────────────────

        [Test]
        public void EvaluateRule_AllConditionsTrue_ReturnsTrue()
        {
            _runner.CurrentNodeId = "mine";
            _runner.GetSkill(SkillType.Mining).Level = 15;

            var rule = new Rule
            {
                Conditions =
                {
                    Condition.AtNode("mine"),
                    Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 10),
                },
                Action = AutomationAction.WorkAt("deep_mine"),
            };

            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateRule(rule, _ctx));
        }

        [Test]
        public void EvaluateRule_OneConditionFalse_ReturnsFalse()
        {
            _runner.CurrentNodeId = "mine";
            _runner.GetSkill(SkillType.Mining).Level = 5;

            var rule = new Rule
            {
                Conditions =
                {
                    Condition.AtNode("mine"),
                    Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 10),
                },
                Action = AutomationAction.WorkAt("deep_mine"),
            };

            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsFalse(RuleEvaluator.EvaluateRule(rule, _ctx));
        }

        [Test]
        public void EvaluateRule_EmptyConditions_ReturnsTrue()
        {
            var rule = new Rule
            {
                Conditions = { },
                Action = AutomationAction.Idle(),
            };

            Assert.IsTrue(RuleEvaluator.EvaluateRule(rule, _ctx));
        }

        // ─── Ruleset evaluation ─────────────────────────────

        [Test]
        public void EvaluateRuleset_FirstMatchWins()
        {
            var ruleset = new Ruleset();

            // Rule 0: Always -> TravelTo mine (should match first)
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.WorkAt("mine"),
            });

            // Rule 1: Always -> TravelTo forest (should never match)
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.WorkAt("forest"),
            });

            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(0, result);
            Assert.AreEqual("mine", ruleset.Rules[result].Action.StringParam);
        }

        [Test]
        public void EvaluateRuleset_SkipsDisabledRules()
        {
            var ruleset = new Ruleset();

            // Rule 0: Disabled
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.WorkAt("mine"),
                Enabled = false,
            });

            // Rule 1: Enabled, should match
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.WorkAt("forest"),
            });

            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(1, result);
        }

        [Test]
        public void EvaluateRuleset_NoMatch_ReturnsNegativeOne()
        {
            _runner.CurrentNodeId = "hub";

            var ruleset = new Ruleset();
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.AtNode("mine") },
                Action = AutomationAction.Idle(),
            });

            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(-1, result);
        }

        [Test]
        public void EvaluateRuleset_NullRuleset_ReturnsNegativeOne()
        {
            int result = RuleEvaluator.EvaluateRuleset(null, _ctx);
            Assert.AreEqual(-1, result);
        }

        [Test]
        public void EvaluateRuleset_EmptyRuleset_ReturnsNegativeOne()
        {
            var ruleset = new Ruleset();
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(-1, result);
        }

        [Test]
        public void EvaluateRuleset_FallbackAlwaysRule_MatchesLast()
        {
            _runner.CurrentNodeId = "hub";

            var ruleset = new Ruleset();

            // Rule 0: AtNode(mine) -> won't match
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.AtNode("mine") },
                Action = AutomationAction.WorkAt("mine"),
            });

            // Rule 1: Always -> fallback
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.Idle(),
            });

            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(1, result);
        }

        [Test]
        public void EvaluateRuleset_HigherPriorityRuleSuppressesLower()
        {
            // Simulate the "suppressor rule" pattern from the design doc:
            // Rule 0: Specific condition -> suppresses Rule 1
            // Rule 1: Broader condition -> only fires when Rule 0 doesn't

            _runner.GetSkill(SkillType.Mining).Level = 20;
            _gameState.Bank.Deposit("copper_ore", 60);

            var ruleset = new Ruleset();

            // Rule 0: IF bank has >= 50 copper -> TravelTo forest (higher priority)
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50) },
                Action = AutomationAction.WorkAt("forest"),
            });

            // Rule 1: Always -> WorkAt mine (lower priority, suppressed)
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.WorkAt("mine"),
            });

            _ctx = new EvaluationContext(_runner, _gameState, _config);
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(0, result);
            Assert.AreEqual(ActionType.WorkAt, ruleset.Rules[result].Action.Type);
        }

        [Test]
        public void EvaluateRuleset_CompoundAND_AllMustMatch()
        {
            _runner.CurrentNodeId = "mine";
            _runner.GetSkill(SkillType.Mining).Level = 20;
            _gameState.Bank.Deposit("copper_ore", 60);

            var ruleset = new Ruleset();

            // Compound: AtNode(mine) AND BankContains(copper >= 50) AND SkillLevel(Mining >= 15)
            ruleset.Rules.Add(new Rule
            {
                Conditions =
                {
                    Condition.AtNode("mine"),
                    Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50),
                    Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 15),
                },
                Action = AutomationAction.WorkAt("deep_mine"),
            });

            _ctx = new EvaluationContext(_runner, _gameState, _config);
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void EvaluateRuleset_CompoundAND_FailsIfAnyFalse()
        {
            _runner.CurrentNodeId = "mine";
            _runner.GetSkill(SkillType.Mining).Level = 10; // Below threshold

            var ruleset = new Ruleset();

            ruleset.Rules.Add(new Rule
            {
                Conditions =
                {
                    Condition.AtNode("mine"),
                    Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 15),
                },
                Action = AutomationAction.WorkAt("deep_mine"),
            });

            _ctx = new EvaluationContext(_runner, _gameState, _config);
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(-1, result);
        }

        // ─── Default rulesets ────────────────────────────────

        [Test]
        public void DefaultMacro_IsEmpty()
        {
            var ruleset = DefaultRulesets.CreateDefaultMacro();
            Assert.AreEqual(0, ruleset.Rules.Count,
                "Default macro ruleset should be empty — assignment handles the loop");
        }

        [Test]
        public void DefaultMicro_HasInventoryFullAndGatherHereRules()
        {
            var ruleset = DefaultRulesets.CreateDefaultMicro();
            Assert.AreEqual(2, ruleset.Rules.Count,
                "Default micro should have InventoryFull→FinishTask and Always→GatherHere");
            Assert.AreEqual(ActionType.FinishTask, ruleset.Rules[0].Action.Type,
                "First rule should be InventoryFull → FinishTask");
            Assert.AreEqual(ActionType.GatherHere, ruleset.Rules[1].Action.Type,
                "Second rule should be Always → GatherHere");
            Assert.AreEqual(0, ruleset.Rules[1].Action.IntParam,
                "Default gather rule should target index 0");
        }

        [Test]
        public void DefaultMicro_InventoryFullMatchesFirst_WhenFull()
        {
            // Fill inventory so InventoryFull condition matches
            var itemDef = _config.ItemDefinitions[0];
            for (int i = 0; i < _config.InventorySize; i++)
                _runner.Inventory.TryAdd(itemDef);

            var ruleset = DefaultRulesets.CreateDefaultMicro();
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(0, result, "When inventory is full, InventoryFull rule (index 0) should match first");
            Assert.AreEqual(ActionType.FinishTask, ruleset.Rules[result].Action.Type);
        }

        [Test]
        public void DefaultMicro_GatherHereMatches_WhenNotFull()
        {
            var ruleset = DefaultRulesets.CreateDefaultMicro();
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(1, result, "When inventory is not full, GatherHere rule (index 1) should match");
            Assert.AreEqual(ActionType.GatherHere, ruleset.Rules[result].Action.Type);
        }

        // ─── DeepCopy ───────────────────────────────────────

        [Test]
        public void DeepCopy_ProducesIndependentCopy()
        {
            var original = DefaultRulesets.CreateDefaultMicro();
            var copy = original.DeepCopy();

            // Modify original
            original.Rules[0].Label = "Modified";
            original.Rules[0].Action.StringParam = "changed";
            original.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.Idle(),
            });

            // Copy should be unchanged
            Assert.AreEqual("Deposit when full", copy.Rules[0].Label);
            Assert.IsNull(copy.Rules[0].Action.StringParam);
            Assert.AreEqual(2, copy.Rules.Count);
        }

        [Test]
        public void DeepCopy_ConditionsAreIndependent()
        {
            var original = new Ruleset();
            original.Rules.Add(new Rule
            {
                Conditions =
                {
                    Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50),
                },
                Action = AutomationAction.WorkAt("forest"),
            });

            var copy = original.DeepCopy();

            // Modify original condition
            original.Rules[0].Conditions[0].NumericValue = 100;

            // Copy condition should be unchanged
            Assert.AreEqual(50, copy.Rules[0].Conditions[0].NumericValue);
        }
    }
}
