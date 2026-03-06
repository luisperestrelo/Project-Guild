using System;
using System.Text;
using System.Text.RegularExpressions;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;
using UnityEngine.UIElements;

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
        /// Returns "After current task" or "Interrupt".
        /// </summary>
        public static string FormatTimingTag(Rule rule)
        {
            if (rule == null) return "";
            return rule.FinishCurrentSequence ? "After current task" : "Immediately";
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
                    return $"Self HP {FormatOperator(condition.Operator)} {(int)condition.NumericValue}%";

                case ConditionType.EnemyCountAtNode:
                    return $"Enemy Count {FormatOperator(condition.Operator)} {(int)condition.NumericValue}";

                case ConditionType.AllyCountAtNode:
                    return $"Ally Count {FormatOperator(condition.Operator)} {(int)condition.NumericValue}";

                case ConditionType.AlliesInCombatAtNode:
                    return $"Allies In Combat {FormatOperator(condition.Operator)} {(int)condition.NumericValue}";

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

                case ActionType.AssignSequence:
                    if (state != null && !string.IsNullOrEmpty(action.StringParam))
                    {
                        foreach (var seq in state.TaskSequenceLibrary)
                            if (seq.Id == action.StringParam)
                                return seq.Name ?? seq.Id;
                    }
                    return string.IsNullOrEmpty(action.StringParam)
                        ? "Assign ?"
                        : "<color=#CC4444>\u26A0 [Deleted Sequence]</color> <color=#999999>\u2014 goes Idle</color>";

                case ActionType.GatherHere:
                    if (!string.IsNullOrEmpty(action.StringParam))
                        return $"Gather {ResolveItemName(action.StringParam, itemResolver)}";
                    return "Gather Any";

                case ActionType.FinishTask:
                    return "Finish Task";

                case ActionType.FightHere:
                    return "Fight Here";

                case ActionType.Wait:
                    return "Wait";

                case ActionType.GatherBestAvailable:
                    return $"Gather Best Available [{FormatSkillName((SkillType)action.IntParam)}]";

                default:
                    return action.Type.ToString();
            }
        }

        // ─── Combat Formatting ────────────────────────────────────────

        /// <summary>Format a CombatCondition as readable text for combat style rule displays.</summary>
        public static string FormatCombatCondition(CombatCondition condition, SimulationConfig config)
        {
            if (condition == null) return "";

            return condition.Type switch
            {
                CombatConditionType.Always => "Always",
                CombatConditionType.SelfHpPercent =>
                    $"Self HP {FormatOperator(condition.Operator)} {(int)condition.NumericValue}%",
                CombatConditionType.SelfManaPercent =>
                    $"Self Mana {FormatOperator(condition.Operator)} {(int)condition.NumericValue}%",
                CombatConditionType.TargetHpPercent =>
                    $"Target HP {FormatOperator(condition.Operator)} {(int)condition.NumericValue}%",
                CombatConditionType.EnemyCountAtNode =>
                    $"Enemy Count {FormatOperator(condition.Operator)} {(int)condition.NumericValue}",
                CombatConditionType.AllyCountAtNode =>
                    $"Ally Count {FormatOperator(condition.Operator)} {(int)condition.NumericValue}",
                CombatConditionType.AlliesInCombatAtNode =>
                    $"Allies In Combat {FormatOperator(condition.Operator)} {(int)condition.NumericValue}",
                CombatConditionType.AbilityOffCooldown =>
                    $"{FormatAbilityName(condition.StringParam, config)} Off Cooldown",
                CombatConditionType.EnemyIsCasting => "Enemy Is Casting",
                _ => condition.Type.ToString(),
            };
        }

        /// <summary>Format a TargetSelection enum as readable text.</summary>
        public static string FormatTargetSelection(TargetSelection selection)
        {
            return selection switch
            {
                TargetSelection.NearestEnemy => "Nearest Enemy",
                TargetSelection.LowestHpEnemy => "Lowest HP Enemy",
                TargetSelection.HighestHpEnemy => "Highest HP Enemy",
                TargetSelection.NearestAlly => "Nearest Ally",
                TargetSelection.LowestHpAlly => "Lowest HP Ally",
                _ => selection.ToString(),
            };
        }

        /// <summary>Resolve an ability ID to its display name from SimulationConfig.</summary>
        public static string FormatAbilityName(string abilityId, SimulationConfig config)
        {
            if (string.IsNullOrEmpty(abilityId)) return "Unknown";
            if (config?.AbilityDefinitions != null)
            {
                foreach (var ability in config.AbilityDefinitions)
                {
                    if (ability.Id == abilityId)
                        return ability.Name ?? HumanizeId(abilityId);
                }
            }
            return HumanizeId(abilityId);
        }

        // ─── Step Formatting ─────────────────────────────────────────

        /// <summary>Format a task step as readable text.</summary>
        public static string FormatStep(TaskStep step, GameState state)
        {
            if (step == null) return "";

            switch (step.Type)
            {
                case TaskStepType.TravelTo:
                    return $"Travel to {ResolveNodeName(step.TargetNodeId, state)}";

                case TaskStepType.Work:
                    return "Work";

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

        /// <summary>
        /// Check if a name is an auto-generated default (e.g. "Sequence 3", "Macro Ruleset 1").
        /// </summary>
        public static bool IsDefaultName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            return Regex.IsMatch(name, @"^(Sequence|Macro Ruleset|Micro Ruleset) \d+$");
        }
    }

    /// <summary>
    /// Placeholder behavior for name TextFields in automation editors.
    /// Default names (e.g. "Macro Ruleset 1") appear as greyed placeholder text.
    /// Clicking the field clears it so the user can type from scratch.
    /// Leaving the field empty restores the default name.
    /// </summary>
    public class NameFieldPlaceholder
    {
        private readonly TextField _field;
        private readonly Func<string> _getDataName;
        private const string PlaceholderClass = "name-field-placeholder";

        public NameFieldPlaceholder(TextField field, Func<string> getDataName)
        {
            _field = field;
            _getDataName = getDataName;

            field.RegisterCallback<FocusInEvent>(_ =>
            {
                if (_field.ClassListContains(PlaceholderClass))
                    _field.SelectAll();
            });

            // Remove placeholder styling as soon as the user types
            field.RegisterValueChangedCallback(evt =>
            {
                if (!string.IsNullOrEmpty(evt.newValue))
                    _field.RemoveFromClassList(PlaceholderClass);
            });

            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                string name = _getDataName();
                if (string.IsNullOrEmpty(_field.value))
                    _field.SetValueWithoutNotify(name ?? "");
                if (AutomationUIHelpers.IsDefaultName(name))
                    _field.AddToClassList(PlaceholderClass);
            });
        }

        /// <summary>
        /// Call from RefreshEditor to sync the field display with the data model.
        /// </summary>
        public void UpdateDisplay(string name)
        {
            _field.SetValueWithoutNotify(name ?? "");
            if (AutomationUIHelpers.IsDefaultName(name))
                _field.AddToClassList(PlaceholderClass);
            else
                _field.RemoveFromClassList(PlaceholderClass);
        }
    }
}
