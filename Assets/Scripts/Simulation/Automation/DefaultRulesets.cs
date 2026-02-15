namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Factory methods for default rulesets assigned to new runners.
    /// </summary>
    public static class DefaultRulesets
    {
        /// <summary>
        /// Default macro ruleset: empty.
        /// The task sequence handles the gather→deposit→repeat loop.
        /// Macro rules are for task sequence *changes* — the player adds them when they want
        /// condition-based switching (e.g. "IF BankContains(copper) >= 200 THEN switch to oak").
        /// </summary>
        public static Ruleset CreateDefaultMacro()
        {
            return new Ruleset();
        }

        /// <summary>
        /// Default micro ruleset: Always → GatherHere(0).
        /// This is the "mine copper" / "spam fireball" equivalent.
        /// Visible, editable, communicates what the pawn does within a task.
        /// </summary>
        public static Ruleset CreateDefaultMicro()
        {
            var ruleset = new Ruleset();

            ruleset.Rules.Add(new Rule
            {
                Label = "Deposit when full",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });

            ruleset.Rules.Add(new Rule
            {
                Label = "Gather resource",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });

            return ruleset;
        }
    }
}
