using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// A collection of rules assigned to a runner. Evaluated top-to-bottom,
    /// first match wins. Phase 3 has a single list; Phase 5 adds separate
    /// Targeting and Ability lists for combat.
    /// </summary>
    [Serializable]
    public class Ruleset
    {
        public List<Rule> Rules = new();

        /// <summary>
        /// Deep-copy for templates/copy-paste. Rules contain Lists of Conditions
        /// so a shallow copy would share references.
        /// </summary>
        public Ruleset DeepCopy()
        {
            var copy = new Ruleset();
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
