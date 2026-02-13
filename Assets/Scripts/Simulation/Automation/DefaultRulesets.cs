namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Factory methods for default rulesets. New runners get a default ruleset
    /// that replicates the current hardcoded behavior.
    /// </summary>
    public static class DefaultRulesets
    {
        /// <summary>
        /// Default gathering ruleset:
        ///   1. IF InventoryFull THEN DepositAndResume
        ///
        /// Replaces the hardcoded BeginAutoReturn call in TickGathering.
        /// No "Always" fallback needed — evaluator returning -1 means
        /// "keep doing what you're doing."
        /// </summary>
        public static Ruleset CreateGathererDefault()
        {
            var ruleset = new Ruleset();

            ruleset.Rules.Add(new Rule
            {
                Label = "Deposit when full",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.DepositAndResume(),
                FinishCurrentTrip = false, // Already full, nothing to finish
                Enabled = true,
            });

            return ruleset;
        }
    }
}
