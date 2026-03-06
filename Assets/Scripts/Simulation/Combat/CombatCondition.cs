using System;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// Combat-specific condition types for targeting and ability rules.
    /// Separate from the micro/macro ConditionType enum because combat
    /// has different context (enemies, allies, cooldowns).
    /// </summary>
    public enum CombatConditionType
    {
        /// <summary>Always true (fallback).</summary>
        Always,

        /// <summary>Self HP percentage. Operator + NumericValue (0-100).</summary>
        SelfHpPercent,

        /// <summary>Self mana percentage. Operator + NumericValue (0-100).</summary>
        SelfManaPercent,

        /// <summary>Current target's HP percentage. Operator + NumericValue (0-100).</summary>
        TargetHpPercent,

        /// <summary>Number of alive enemies at this node. Operator + NumericValue.</summary>
        EnemyCountAtNode,

        /// <summary>Number of ally runners at this node. Operator + NumericValue.</summary>
        AllyCountAtNode,

        /// <summary>Number of ally runners currently fighting at this node. Operator + NumericValue.</summary>
        AlliesInCombatAtNode,

        /// <summary>True if the specified ability is off cooldown. StringParam = abilityId.</summary>
        AbilityOffCooldown,

        /// <summary>Lowest ally HP percentage at this node (including self). Operator + NumericValue (0-100).</summary>
        LowestAllyHpPercent,

        /// <summary>True if the current target is casting (future, stub). Always returns false.</summary>
        EnemyIsCasting,

        /// <summary>True if any alive enemy at this node is NOT taunted by this runner. No params.</summary>
        AnyUntauntedEnemy,

        /// <summary>Count of allies at this node below NumericValue% HP. Operator + NumericValue for threshold, StringParam for count comparison.</summary>
        AlliesBelowHpPercent,

        /// <summary>True if the current target is attacking (targeting) this runner. No params.</summary>
        EnemyTargetingSelf,
    }

    /// <summary>
    /// A single combat condition. Data-driven with flat fields for serialization.
    /// </summary>
    [Serializable]
    public class CombatCondition
    {
        public CombatConditionType Type;
        public Automation.ComparisonOperator Operator;
        public float NumericValue;
        public string StringParam;

        public CombatCondition() { }

        public CombatCondition DeepCopy()
        {
            return new CombatCondition
            {
                Type = Type,
                Operator = Operator,
                NumericValue = NumericValue,
                StringParam = StringParam,
            };
        }

        // ─── Factory methods ───

        public static CombatCondition Always()
            => new CombatCondition { Type = CombatConditionType.Always };

        public static CombatCondition SelfHpPercent(Automation.ComparisonOperator op, float percent)
            => new CombatCondition { Type = CombatConditionType.SelfHpPercent, Operator = op, NumericValue = percent };

        public static CombatCondition SelfManaPercent(Automation.ComparisonOperator op, float percent)
            => new CombatCondition { Type = CombatConditionType.SelfManaPercent, Operator = op, NumericValue = percent };

        public static CombatCondition TargetHpPercent(Automation.ComparisonOperator op, float percent)
            => new CombatCondition { Type = CombatConditionType.TargetHpPercent, Operator = op, NumericValue = percent };

        public static CombatCondition EnemyCountAtNode(Automation.ComparisonOperator op, int count)
            => new CombatCondition { Type = CombatConditionType.EnemyCountAtNode, Operator = op, NumericValue = count };

        public static CombatCondition AllyCountAtNode(Automation.ComparisonOperator op, int count)
            => new CombatCondition { Type = CombatConditionType.AllyCountAtNode, Operator = op, NumericValue = count };

        public static CombatCondition AlliesInCombatAtNode(Automation.ComparisonOperator op, int count)
            => new CombatCondition { Type = CombatConditionType.AlliesInCombatAtNode, Operator = op, NumericValue = count };

        public static CombatCondition LowestAllyHpPercent(Automation.ComparisonOperator op, float percent)
            => new CombatCondition { Type = CombatConditionType.LowestAllyHpPercent, Operator = op, NumericValue = percent };

        public static CombatCondition AbilityOffCooldown(string abilityId)
            => new CombatCondition { Type = CombatConditionType.AbilityOffCooldown, StringParam = abilityId };

        public static CombatCondition AnyUntauntedEnemy()
            => new CombatCondition { Type = CombatConditionType.AnyUntauntedEnemy };

        /// <summary>
        /// True if at least `count` allies at this node are below `hpPercent`% HP.
        /// NumericValue = HP threshold (0-100), StringParam = minimum count as string.
        /// </summary>
        public static CombatCondition AlliesBelowHpPercent(float hpPercent, int minCount)
            => new CombatCondition { Type = CombatConditionType.AlliesBelowHpPercent, NumericValue = hpPercent, StringParam = minCount.ToString() };

        public static CombatCondition EnemyTargetingSelf()
            => new CombatCondition { Type = CombatConditionType.EnemyTargetingSelf };
    }
}
