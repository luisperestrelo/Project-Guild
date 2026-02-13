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
        /// Auto-gathering is NOT a rule — it's implicit default behavior in the sim.
        /// Idle runners at nodes with gatherables automatically start gathering.
        /// This keeps the player's rule list clean and focused on real decisions.
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
