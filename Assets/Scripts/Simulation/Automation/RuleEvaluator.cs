using System;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Static evaluation methods. No side effects — reads state and returns results.
    /// All condition evaluation is "always compute, never cache."
    /// </summary>
    public static class RuleEvaluator
    {
        /// <summary>
        /// Evaluate a ruleset against the current context. Returns the first matching
        /// rule's index (0-based), or -1 if no rule matched.
        /// The caller reads the action from ruleset.Rules[result].Action.
        /// </summary>
        public static int EvaluateRuleset(Ruleset ruleset, EvaluationContext ctx)
        {
            if (ruleset == null || ruleset.Rules == null) return -1;

            for (int i = 0; i < ruleset.Rules.Count; i++)
            {
                var rule = ruleset.Rules[i];
                if (!rule.Enabled) continue;

                if (EvaluateRule(rule, ctx))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Evaluate a single rule: all conditions must be true (AND).
        /// Empty conditions list = always true.
        /// </summary>
        public static bool EvaluateRule(Rule rule, EvaluationContext ctx)
        {
            if (rule.Conditions == null || rule.Conditions.Count == 0)
                return true;

            for (int i = 0; i < rule.Conditions.Count; i++)
            {
                if (!EvaluateCondition(rule.Conditions[i], ctx))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Evaluate a single condition against the context.
        /// </summary>
        public static bool EvaluateCondition(Condition condition, EvaluationContext ctx)
        {
            switch (condition.Type)
            {
                case ConditionType.Always:
                    return true;

                case ConditionType.InventoryFull:
                    return ctx.Runner.Inventory.FreeSlots <= 0;

                case ConditionType.InventorySlots:
                    return Compare(ctx.Runner.Inventory.FreeSlots, condition.Operator, condition.NumericValue);

                case ConditionType.InventoryContains:
                    int invCount = ctx.Runner.Inventory.CountItem(condition.StringParam);
                    return Compare(invCount, condition.Operator, condition.NumericValue);

                case ConditionType.BankContains:
                    int bankCount = ctx.GameState.Bank.CountItem(condition.StringParam);
                    return Compare(bankCount, condition.Operator, condition.NumericValue);

                case ConditionType.SkillLevel:
                    var skillType = (SkillType)condition.IntParam;
                    int skillLevel = ctx.Runner.GetSkill(skillType).Level;
                    return Compare(skillLevel, condition.Operator, condition.NumericValue);

                case ConditionType.RunnerStateIs:
                    return (int)ctx.Runner.State == condition.IntParam;

                case ConditionType.AtNode:
                    return ctx.Runner.CurrentNodeId == condition.StringParam;

                case ConditionType.SelfHP:
                    // Runners don't have HP yet in Phase 3. This condition
                    // always returns false until the combat system is implemented.
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Generic numeric comparison used by all conditions with operator + value.
        /// </summary>
        public static bool Compare(float actual, ComparisonOperator op, float threshold)
        {
            return op switch
            {
                ComparisonOperator.GreaterThan    => actual > threshold,
                ComparisonOperator.GreaterOrEqual => actual >= threshold,
                ComparisonOperator.LessThan       => actual < threshold,
                ComparisonOperator.LessOrEqual    => actual <= threshold,
                ComparisonOperator.Equal           => Math.Abs(actual - threshold) < 0.001f,
                ComparisonOperator.NotEqual        => Math.Abs(actual - threshold) >= 0.001f,
                _ => false,
            };
        }
    }
}
