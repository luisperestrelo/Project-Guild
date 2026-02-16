using System.Text;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Static helpers for formatting automation rules, conditions, and actions
    /// as human-readable natural language. Pure logic — no Unity dependencies, fully testable.
    ///
    /// Resolves IDs to display names where possible (nodes from GameState.Map,
    /// items via an optional resolver function). Falls back to humanized IDs.
    /// </summary>
    public static class AutomationUIHelpers
    {
        /// <summary>
        /// Optional delegate for resolving item IDs to display names.
        /// When null, falls back to HumanizeId.
        /// </summary>
        public delegate string ItemNameResolver(string itemId);

        // ─── Top-Level Formatters ────────────────────────────────────

        /// <summary>
        /// Format a full rule as a readable sentence.
        /// "IF Bank contains Copper Ore >= 200 THEN Work at Copper Mine [Finish Current Seq]"
        /// </summary>
        public static string FormatRule(Rule rule, GameState state, ItemNameResolver itemResolver = null)
        {
            if (rule == null) return "";

            var sb = new StringBuilder();

            // Conditions
            string conditions = FormatConditions(rule.Conditions, state, itemResolver);
            sb.Append("IF ");
            sb.Append(conditions);
            sb.Append(" THEN ");

            // Action
            sb.Append(FormatAction(rule.Action, state, itemResolver));

            return sb.ToString();
        }

        /// <summary>
        /// Format the timing as a readable tag.
        /// Returns "Immediately" or "Finish Current Sequence".
        /// </summary>
        public static string FormatTimingTag(Rule rule)
        {
            if (rule == null) return "";
            return rule.FinishCurrentSequence ? "Finish Current Sequence" : "Immediately";
        }

        // ─── Condition Formatting ─────────────────────────────────────

        /// <summary>Format a single condition as readable text.</summary>
        public static string FormatCondition(Condition condition, GameState state, ItemNameResolver itemResolver = null)
        {
            if (condition == null) return "";

            switch (condition.Type)
            {
                case ConditionType.Always:
                    return "Always";

                case ConditionType.InventoryFull:
                    return "Inventory Full";

                case ConditionType.InventorySlots:
                    return $"Free Slots {FormatOperator(condition.Operator)} {(int)condition.NumericValue}";

                case ConditionType.InventoryContains:
                    return $"Inventory contains {ResolveItemName(condition.StringParam, itemResolver)} " +
                           $"{FormatOperator(condition.Operator)} {(int)condition.NumericValue}";

                case ConditionType.BankContains:
                    return $"Bank contains {ResolveItemName(condition.StringParam, itemResolver)} " +
                           $"{FormatOperator(condition.Operator)} {(int)condition.NumericValue}";

                case ConditionType.SkillLevel:
                    return $"{FormatSkillName((SkillType)condition.IntParam)} Level " +
                           $"{FormatOperator(condition.Operator)} {(int)condition.NumericValue}";

                case ConditionType.RunnerStateIs:
                    return $"Runner is {(RunnerState)condition.IntParam}";

                case ConditionType.AtNode:
                    return $"At {ResolveNodeName(condition.StringParam, state)}";

                case ConditionType.SelfHP:
                    return $"HP {FormatOperator(condition.Operator)} {(int)condition.NumericValue}%";

                default:
                    return condition.Type.ToString();
            }
        }

        /// <summary>Format a list of conditions as AND-joined text.</summary>
        public static string FormatConditions(
            System.Collections.Generic.List<Condition> conditions,
            GameState state, ItemNameResolver itemResolver = null)
        {
            if (conditions == null || conditions.Count == 0)
                return "Always";

            if (conditions.Count == 1)
                return FormatCondition(conditions[0], state, itemResolver);

            var sb = new StringBuilder();
            for (int i = 0; i < conditions.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                sb.Append(FormatCondition(conditions[i], state, itemResolver));
            }
            return sb.ToString();
        }

        // ─── Action Formatting ─────────────────────────────────────────

        /// <summary>Format an action as readable text.</summary>
        public static string FormatAction(AutomationAction action, GameState state, ItemNameResolver itemResolver = null)
        {
            if (action == null) return "";

            switch (action.Type)
            {
                case ActionType.Idle:
                    return "Go Idle";

                case ActionType.WorkAt:
                    return $"Work at {ResolveNodeName(action.StringParam, state)}";

                case ActionType.ReturnToHub:
                    return "Return to Hub";

                case ActionType.GatherHere:
                    if (!string.IsNullOrEmpty(action.StringParam))
                        return $"Gather {ResolveItemName(action.StringParam, itemResolver)}";
                    return $"Gather Here ({action.IntParam})";

                case ActionType.FinishTask:
                    return "Finish Task";

                default:
                    return action.Type.ToString();
            }
        }

        // ─── Step Formatting ─────────────────────────────────────────

        /// <summary>Format a task step as readable text.</summary>
        public static string FormatStep(TaskStep step, GameState state, System.Func<string, string> microNameResolver = null)
        {
            if (step == null) return "";

            switch (step.Type)
            {
                case TaskStepType.TravelTo:
                    return $"Travel to {ResolveNodeName(step.TargetNodeId, state)}";

                case TaskStepType.Work:
                    string microName = null;
                    if (!string.IsNullOrEmpty(step.MicroRulesetId))
                        microName = microNameResolver?.Invoke(step.MicroRulesetId);
                    return microName != null
                        ? $"Work ({microName})"
                        : "Work";

                case TaskStepType.Deposit:
                    return "Deposit";

                default:
                    return step.Type.ToString();
            }
        }

        // ─── Name Resolution ──────────────────────────────────────────

        /// <summary>Resolve a node ID to its display name via GameState.Map.</summary>
        public static string ResolveNodeName(string nodeId, GameState state)
        {
            if (string.IsNullOrEmpty(nodeId)) return "Unknown";
            var node = state?.Map?.GetNode(nodeId);
            return node?.Name ?? HumanizeId(nodeId);
        }

        /// <summary>Resolve an item ID to its display name via the resolver, or humanize.</summary>
        public static string ResolveItemName(string itemId, ItemNameResolver resolver = null)
        {
            if (string.IsNullOrEmpty(itemId)) return "Unknown";
            if (resolver != null)
            {
                var name = resolver(itemId);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            return HumanizeId(itemId);
        }

        /// <summary>Format a SkillType enum value as a readable name.</summary>
        public static string FormatSkillName(SkillType skill)
        {
            return skill switch
            {
                SkillType.PotionMaking => "Potion Making",
                _ => skill.ToString(),
            };
        }

        // ─── Operator Formatting ──────────────────────────────────────

        /// <summary>Format a ComparisonOperator as a symbol.</summary>
        public static string FormatOperator(ComparisonOperator op)
        {
            return op switch
            {
                ComparisonOperator.GreaterThan => ">",
                ComparisonOperator.GreaterOrEqual => ">=",
                ComparisonOperator.LessThan => "<",
                ComparisonOperator.LessOrEqual => "<=",
                ComparisonOperator.Equal => "=",
                ComparisonOperator.NotEqual => "!=",
                _ => op.ToString(),
            };
        }

        // ─── Utility ─────────────────────────────────────────────────

        /// <summary>
        /// Convert a snake_case or kebab-case ID to a Title Case display name.
        /// "copper_ore" → "Copper Ore", "pine-forest" → "Pine Forest"
        /// </summary>
        public static string HumanizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";

            var sb = new StringBuilder();
            bool capitalizeNext = true;

            foreach (char c in id)
            {
                if (c == '_' || c == '-')
                {
                    sb.Append(' ');
                    capitalizeNext = true;
                }
                else if (capitalizeNext)
                {
                    sb.Append(char.ToUpper(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
