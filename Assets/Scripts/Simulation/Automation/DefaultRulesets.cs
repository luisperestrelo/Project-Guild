using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Factory methods for default rulesets assigned to new runners.
    /// Well-known IDs for the default templates.
    /// </summary>
    public static class DefaultRulesets
    {
        public const string DefaultMacroId = "default-macro";
        public const string DefaultMicroId = "default-micro";

        /// <summary>
        /// Default macro ruleset: empty.
        /// The task sequence handles the gather→deposit→repeat loop.
        /// Macro rules are for task sequence *changes* — the player adds them when they want
        /// condition-based switching (e.g. "IF BankContains(copper) >= 200 THEN switch to oak").
        /// </summary>
        public static Ruleset CreateDefaultMacro()
        {
            return new Ruleset
            {
                Id = DefaultMacroId,
                Name = "Default Macro",
                Category = RulesetCategory.General,
            };
        }

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
        /// Ensure the default macro and micro rulesets exist in the library.
        /// Called during StartNewGame and LoadState. Idempotent — skips if already present.
        /// </summary>
        public static void EnsureInLibrary(GameState state)
        {
            bool hasMacro = false, hasMicro = false;
            foreach (var r in state.MacroRulesetLibrary)
                if (r.Id == DefaultMacroId) { hasMacro = true; break; }
            foreach (var r in state.MicroRulesetLibrary)
                if (r.Id == DefaultMicroId) { hasMicro = true; break; }

            if (!hasMacro) state.MacroRulesetLibrary.Add(CreateDefaultMacro());
            if (!hasMicro) state.MicroRulesetLibrary.Add(CreateDefaultMicro());
        }
    }
}
