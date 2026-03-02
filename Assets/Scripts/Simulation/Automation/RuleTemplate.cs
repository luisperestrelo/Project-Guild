using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// A reusable template of rules that can be batch-inserted into a Ruleset.
    /// Used for both macro and micro rule templates. Built-in templates cannot
    /// be deleted. Players can create custom templates from existing rulesets.
    /// </summary>
    [Serializable]
    public class RuleTemplate
    {
        public string Id;
        public string Name;
        public bool IsBuiltIn;

        /// <summary>
        /// When true, this template appears as a quick-access button in the editor.
        /// Built-in templates default to true. Players can toggle this.
        /// </summary>
        public bool IsFavorite;

        public List<Rule> Rules = new();

        /// <summary>
        /// Deep-copy the rules list for safe insertion into a ruleset.
        /// Rules contain reference types (Conditions list, AutomationAction)
        /// so a shallow copy would share references.
        /// </summary>
        public List<Rule> DeepCopyRules()
        {
            var copy = new List<Rule>(Rules.Count);
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
                copy.Add(newRule);
            }
            return copy;
        }
    }
}
