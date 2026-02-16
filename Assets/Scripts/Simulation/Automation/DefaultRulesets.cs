using System.Collections.Generic;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Factory methods for default rulesets and well-known library sequences.
    /// Well-known IDs for the default templates.
    /// </summary>
    public static class DefaultRulesets
    {
        public const string DefaultMicroId = "default-micro";
        public const string ReturnToHubSequenceId = "return-to-hub";

        /// <summary>
        /// Default micro ruleset: Always → GatherAny (random resource).
        /// This is the "mine whatever's here" equivalent.
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
                Label = "Gather any resource",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherAny(),
                Enabled = true,
            });

            return ruleset;
        }

        /// <summary>
        /// Pre-created library sequence: travel to hub and deposit.
        /// Non-looping — runner goes idle after depositing (macro re-evaluates).
        /// Visible and editable by the player.
        /// </summary>
        public static TaskSequence CreateReturnToHubSequence(string hubNodeId)
        {
            return new TaskSequence
            {
                Id = ReturnToHubSequenceId,
                Name = "Return to Hub",
                TargetNodeId = hubNodeId,
                Loop = false,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, hubNodeId),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
        }

        /// <summary>
        /// Ensure the default micro ruleset and ReturnToHub sequence exist in the library.
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

            // Ensure ReturnToHub sequence exists in library
            string hubId = state.Map?.HubNodeId ?? "hub";
            bool hasReturnToHub = false;
            foreach (var s in state.TaskSequenceLibrary)
                if (s.Id == ReturnToHubSequenceId) { hasReturnToHub = true; break; }

            if (!hasReturnToHub) state.TaskSequenceLibrary.Add(CreateReturnToHubSequence(hubId));
        }
    }
}
