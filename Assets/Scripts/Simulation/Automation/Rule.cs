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
        /// When true (default), if this rule triggers a task switch during a gathering loop,
        /// the runner finishes the current deposit cycle first, then executes the new action.
        /// FleeToHub ignores this flag (always immediate).
        /// </summary>
        public bool FinishCurrentTrip = true;

        /// <summary>
        /// Player-editable label for this rule. Optional.
        /// </summary>
        public string Label = "";
    }
}
