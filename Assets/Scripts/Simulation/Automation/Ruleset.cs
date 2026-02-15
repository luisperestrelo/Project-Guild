using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    public enum RulesetCategory
    {
        General,
        Gathering,
        Combat,
        Crafting,
    }

    /// <summary>
    /// A collection of rules assigned to a runner. Evaluated top-to-bottom,
    /// first match wins. Phase 3 has a single list; Phase 5 adds separate
    /// Targeting and Ability lists for combat.
    /// </summary>
    [Serializable]
    public class Ruleset
    {
        /// <summary>Unique identifier for library lookups. Null when not in a library.</summary>
        public string Id;

        /// <summary>Player-facing display name (e.g. "Default Gather", "Tank Rotation").</summary>
        public string Name;

        /// <summary>Category for library organization and filtering.</summary>
        public RulesetCategory Category;

        public List<Rule> Rules = new();

        /// <summary>
        /// Deep-copy for templates/copy-paste. Rules contain Lists of Conditions
        /// so a shallow copy would share references. Generates a NEW Id (clone, not alias).
        /// </summary>
        public Ruleset DeepCopy()
        {
            var copy = new Ruleset
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name,
                Category = Category,
            };
            foreach (var rule in Rules)
            {
                var newRule = new Rule
                {
                    Action = new AutomationAction
                    {
                        Type = rule.Action.Type,
                        StringParam = rule.Action.StringParam,
                        IntParam = rule.Action.IntParam,
                    },
                    Enabled = rule.Enabled,
                    FinishCurrentSequence = rule.FinishCurrentSequence,
                    Label = rule.Label,
                };
                foreach (var cond in rule.Conditions)
                {
                    newRule.Conditions.Add(new Condition
                    {
                        Type = cond.Type,
                        Operator = cond.Operator,
                        NumericValue = cond.NumericValue,
                        StringParam = cond.StringParam,
                        IntParam = cond.IntParam,
                    });
                }
                copy.Rules.Add(newRule);
            }
            return copy;
        }
    }
}
