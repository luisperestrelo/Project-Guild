using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class AutomationConditionTests
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
                    new ItemDefinition("iron_ore", "Iron Ore", ItemCategory.Ore),
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

        // ─── Always ─────────────────────────────────────────

        [Test]
        public void Always_ReturnsTrue()
        {
            var cond = Condition.Always();
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── InventoryFull ──────────────────────────────────

        [Test]
        public void InventoryFull_WhenFull_ReturnsTrue()
        {
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 28; i++)
                _runner.Inventory.TryAdd(itemDef);

            var cond = Condition.InventoryFull();
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void InventoryFull_WhenNotFull_ReturnsFalse()
        {
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            _runner.Inventory.TryAdd(itemDef);

            var cond = Condition.InventoryFull();
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void InventoryFull_WhenEmpty_ReturnsFalse()
        {
            var cond = Condition.InventoryFull();
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── InventorySlots ─────────────────────────────────

        [Test]
        public void InventorySlots_LessThan5_WhenHas3Free_ReturnsTrue()
        {
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 25; i++)
                _runner.Inventory.TryAdd(itemDef);

            var cond = Condition.InventorySlots(ComparisonOperator.LessThan, 5);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void InventorySlots_LessThan5_WhenHas10Free_ReturnsFalse()
        {
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 18; i++)
                _runner.Inventory.TryAdd(itemDef);

            var cond = Condition.InventorySlots(ComparisonOperator.LessThan, 5);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── InventoryContains ──────────────────────────────

        [Test]
        public void InventoryContains_HasEnough_ReturnsTrue()
        {
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 10; i++)
                _runner.Inventory.TryAdd(itemDef);

            var cond = Condition.InventoryContains("copper_ore", ComparisonOperator.GreaterOrEqual, 10);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void InventoryContains_NotEnough_ReturnsFalse()
        {
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 5; i++)
                _runner.Inventory.TryAdd(itemDef);

            var cond = Condition.InventoryContains("copper_ore", ComparisonOperator.GreaterOrEqual, 10);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void InventoryContains_ItemNotPresent_ZeroCount()
        {
            var cond = Condition.InventoryContains("iron_ore", ComparisonOperator.Equal, 0);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── BankContains ───────────────────────────────────

        [Test]
        public void BankContains_HasEnough_ReturnsTrue()
        {
            _gameState.Bank.Deposit("copper_ore", 50);

            var cond = Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void BankContains_NotEnough_ReturnsFalse()
        {
            _gameState.Bank.Deposit("copper_ore", 30);

            var cond = Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 50);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void BankContains_LessThan_WhenBankLow_ReturnsTrue()
        {
            _gameState.Bank.Deposit("copper_ore", 10);

            var cond = Condition.BankContains("copper_ore", ComparisonOperator.LessThan, 50);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── SkillLevel ─────────────────────────────────────

        [Test]
        public void SkillLevel_MeetsThreshold_ReturnsTrue()
        {
            _runner.GetSkill(SkillType.Mining).Level = 15;

            var cond = Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 15);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void SkillLevel_BelowThreshold_ReturnsFalse()
        {
            _runner.GetSkill(SkillType.Mining).Level = 10;

            var cond = Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 15);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── RunnerStateIs ──────────────────────────────────

        [Test]
        public void RunnerStateIs_Matches_ReturnsTrue()
        {
            _runner.State = RunnerState.Gathering;
            var cond = Condition.RunnerStateIs(RunnerState.Gathering);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void RunnerStateIs_DoesNotMatch_ReturnsFalse()
        {
            _runner.State = RunnerState.Idle;
            var cond = Condition.RunnerStateIs(RunnerState.Gathering);
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── AtNode ─────────────────────────────────────────

        [Test]
        public void AtNode_AtCorrectNode_ReturnsTrue()
        {
            _runner.CurrentNodeId = "mine";
            var cond = Condition.AtNode("mine");
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsTrue(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        [Test]
        public void AtNode_AtWrongNode_ReturnsFalse()
        {
            _runner.CurrentNodeId = "hub";
            var cond = Condition.AtNode("mine");
            _ctx = new EvaluationContext(_runner, _gameState, _config);
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── SelfHP ─────────────────────────────────────────

        [Test]
        public void SelfHP_AlwaysFalse_UntilHPImplemented()
        {
            var cond = Condition.SelfHP(ComparisonOperator.LessThan, 30);
            Assert.IsFalse(RuleEvaluator.EvaluateCondition(cond, _ctx));
        }

        // ─── Compare helper ─────────────────────────────────

        [Test]
        public void Compare_GreaterThan_CorrectResults()
        {
            Assert.IsTrue(RuleEvaluator.Compare(10, ComparisonOperator.GreaterThan, 5));
            Assert.IsFalse(RuleEvaluator.Compare(5, ComparisonOperator.GreaterThan, 5));
            Assert.IsFalse(RuleEvaluator.Compare(3, ComparisonOperator.GreaterThan, 5));
        }

        [Test]
        public void Compare_GreaterOrEqual_CorrectResults()
        {
            Assert.IsTrue(RuleEvaluator.Compare(10, ComparisonOperator.GreaterOrEqual, 5));
            Assert.IsTrue(RuleEvaluator.Compare(5, ComparisonOperator.GreaterOrEqual, 5));
            Assert.IsFalse(RuleEvaluator.Compare(3, ComparisonOperator.GreaterOrEqual, 5));
        }

        [Test]
        public void Compare_LessThan_CorrectResults()
        {
            Assert.IsTrue(RuleEvaluator.Compare(3, ComparisonOperator.LessThan, 5));
            Assert.IsFalse(RuleEvaluator.Compare(5, ComparisonOperator.LessThan, 5));
            Assert.IsFalse(RuleEvaluator.Compare(10, ComparisonOperator.LessThan, 5));
        }

        [Test]
        public void Compare_LessOrEqual_CorrectResults()
        {
            Assert.IsTrue(RuleEvaluator.Compare(3, ComparisonOperator.LessOrEqual, 5));
            Assert.IsTrue(RuleEvaluator.Compare(5, ComparisonOperator.LessOrEqual, 5));
            Assert.IsFalse(RuleEvaluator.Compare(10, ComparisonOperator.LessOrEqual, 5));
        }

        [Test]
        public void Compare_Equal_CorrectResults()
        {
            Assert.IsTrue(RuleEvaluator.Compare(5, ComparisonOperator.Equal, 5));
            Assert.IsFalse(RuleEvaluator.Compare(5.1f, ComparisonOperator.Equal, 5));
        }

        [Test]
        public void Compare_Equal_WithFloatTolerance()
        {
            // Values within 0.001 tolerance should be considered equal
            Assert.IsTrue(RuleEvaluator.Compare(5.0005f, ComparisonOperator.Equal, 5));
            Assert.IsFalse(RuleEvaluator.Compare(5.01f, ComparisonOperator.Equal, 5));
        }

        [Test]
        public void Compare_NotEqual_CorrectResults()
        {
            Assert.IsTrue(RuleEvaluator.Compare(10, ComparisonOperator.NotEqual, 5));
            Assert.IsFalse(RuleEvaluator.Compare(5, ComparisonOperator.NotEqual, 5));
        }
    }
}
