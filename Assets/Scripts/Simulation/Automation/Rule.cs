using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// A single automation rule: IF all conditions match THEN execute action.
    /// Rules are evaluated top-to-bottom in a Ruleset; first match wins.
    /// </summary>
    [Serializable]
    public class Rule
    {
        /// <summary>
        /// All conditions must be true (AND composition). Empty list = always true.
        /// </summary>
        public List<Condition> Conditions = new();

        /// <summary>
        /// Action to execute when all conditions match.
        /// </summary>
        public AutomationAction Action;

        /// <summary>
        /// Player can disable a rule without deleting it.
        /// </summary>
        public bool Enabled = true;

        /// <summary>
        /// When true (default), if this macro rule fires during an active task sequence,
        /// the runner finishes the current sequence cycle first, then executes the new action.
        /// When false, the new action is applied immediately (interrupting the current step).
        /// </summary>
        public bool FinishCurrentSequence = true;

        /// <summary>
        /// Player-editable label for this rule. Optional.
        /// </summary>
        public string Label = "";
    }
}
