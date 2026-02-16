using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Factory methods for default rulesets assigned to new runners.
    /// Well-known IDs for the default templates.
    /// </summary>
    public static class DefaultRulesets
    {
        public const string DefaultMicroId = "default-micro";

        /// <summary>
        /// Default micro ruleset: Always → GatherHere(0).
        /// This is the "mine copper" / "spam fireball" equivalent.
        /// Visible, editable, communicates what the pawn does within a task.
        /// </summary>
        public static Ruleset CreateDefaultMicro()
        {
            var ruleset = new Ruleset
            {
                Id = DefaultMicroId,
                Name = "Default Gather",
                Category = RulesetCategory.Gathering,
            };

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

        /// <summary>
        /// Ensure the default micro ruleset exists in the library.
        /// Called during StartNewGame and LoadState. Idempotent — skips if already present.
        /// Macro rulesets have no default — runners start with null (no auto-switching)
        /// until the player actively sets up macro rules.
        /// </summary>
        public static void EnsureInLibrary(GameState state)
        {
            bool hasMicro = false;
            foreach (var r in state.MicroRulesetLibrary)
                if (r.Id == DefaultMicroId) { hasMicro = true; break; }

            if (!hasMicro) state.MicroRulesetLibrary.Add(CreateDefaultMicro());
        }
    }
}
