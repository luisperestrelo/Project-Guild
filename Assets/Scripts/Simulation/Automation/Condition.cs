using System;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// A boolean check against live game state. Data-driven: ConditionType enum
    /// determines which parameters are used and how evaluation works.
    /// No polymorphism — flat fields for JsonUtility serialization.
    /// </summary>
    [Serializable]
    public class Condition
    {
        public ConditionType Type;
        public ComparisonOperator Operator;
        public float NumericValue;
        public string StringParam;   // itemId, nodeId
        public int IntParam;         // SkillType or RunnerState as int

        public Condition() { }

        // ─── Factory methods for readable construction ───

        public static Condition Always()
            => new Condition { Type = ConditionType.Always };

        public static Condition InventoryFull()
            => new Condition { Type = ConditionType.InventoryFull };

        public static Condition InventorySlots(ComparisonOperator op, int value)
            => new Condition { Type = ConditionType.InventorySlots, Operator = op, NumericValue = value };

        public static Condition InventoryContains(string itemId, ComparisonOperator op, int count)
            => new Condition { Type = ConditionType.InventoryContains, StringParam = itemId, Operator = op, NumericValue = count };

        public static Condition BankContains(string itemId, ComparisonOperator op, int count)
            => new Condition { Type = ConditionType.BankContains, StringParam = itemId, Operator = op, NumericValue = count };

        public static Condition SkillLevel(SkillType skill, ComparisonOperator op, int level)
            => new Condition { Type = ConditionType.SkillLevel, IntParam = (int)skill, Operator = op, NumericValue = level };

        public static Condition RunnerStateIs(RunnerState state)
            => new Condition { Type = ConditionType.RunnerStateIs, IntParam = (int)state };

        public static Condition AtNode(string nodeId)
            => new Condition { Type = ConditionType.AtNode, StringParam = nodeId };

        public static Condition SelfHP(ComparisonOperator op, float percentValue)
            => new Condition { Type = ConditionType.SelfHP, Operator = op, NumericValue = percentValue };
    }
}
