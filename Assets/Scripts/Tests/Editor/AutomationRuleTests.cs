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
                Action = AutomationAction.TravelTo("deep_mine"),
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
                Action = AutomationAction.TravelTo("deep_mine"),
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
                Action = AutomationAction.TravelTo("mine"),
            });

            // Rule 1: Always -> TravelTo forest (should never match)
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.TravelTo("forest"),
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
                Action = AutomationAction.TravelTo("mine"),
                Enabled = false,
            });

            // Rule 1: Enabled, should match
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.TravelTo("forest"),
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
                Action = AutomationAction.TravelTo("mine"),
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
                Action = AutomationAction.TravelTo("forest"),
            });

            // Rule 1: Always -> GatherAt mine (lower priority, suppressed)
            ruleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherAt("mine"),
            });

            _ctx = new EvaluationContext(_runner, _gameState, _config);
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(0, result);
            Assert.AreEqual(ActionType.TravelTo, ruleset.Rules[result].Action.Type);
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
                Action = AutomationAction.TravelTo("deep_mine"),
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
                Action = AutomationAction.TravelTo("deep_mine"),
            });

            _ctx = new EvaluationContext(_runner, _gameState, _config);
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);
            Assert.AreEqual(-1, result);
        }

        // ─── Default ruleset ────────────────────────────────

        [Test]
        public void DefaultGatherer_InventoryFull_FiresDepositRule()
        {
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 28; i++)
                _runner.Inventory.TryAdd(itemDef);

            var ruleset = DefaultRulesets.CreateGathererDefault();
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);

            Assert.AreEqual(0, result);
            Assert.AreEqual(ActionType.DepositAndResume, ruleset.Rules[result].Action.Type);
        }

        [Test]
        public void DefaultGatherer_InventoryNotFull_NoRuleFires()
        {
            var ruleset = DefaultRulesets.CreateGathererDefault();
            int result = RuleEvaluator.EvaluateRuleset(ruleset, _ctx);

            Assert.AreEqual(-1, result);
        }

        // ─── DeepCopy ───────────────────────────────────────

        [Test]
        public void DeepCopy_ProducesIndependentCopy()
        {
            var original = DefaultRulesets.CreateGathererDefault();
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
            Assert.AreEqual(1, copy.Rules.Count);
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
                Action = AutomationAction.TravelTo("forest"),
            });

            var copy = original.DeepCopy();

            // Modify original condition
            original.Rules[0].Conditions[0].NumericValue = 100;

            // Copy condition should be unchanged
            Assert.AreEqual(50, copy.Rules[0].Conditions[0].NumericValue);
        }
    }
}
