using System.Collections.Generic;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Factory methods for default rulesets, well-known library sequences,
    /// and built-in automation templates.
    /// </summary>
    public static class DefaultRulesets
    {
        public const string DefaultMicroId = "default-micro";
        public const string ReturnToHubSequenceId = "return-to-hub";
        public const string DefaultGatherSequenceId = "default-gather-copper";

        // Combat style IDs
        public const string BasicMeleeCombatStyleId = "basic-melee";
        public const string BasicMageCombatStyleId = "basic-mage";
        public const string BasicHealerCombatStyleId = "basic-healer";

        // Built-in step template IDs
        public const string GatherLoopTemplateId = "builtin-gather-loop";
        public const string TravelAndWorkTemplateId = "builtin-travel-work";
        public const string ReturnAndDepositTemplateId = "builtin-return-deposit";

        // Built-in rule template IDs
        public const string BasicGatherTemplateId = "builtin-basic-gather";

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
        /// Pre-created example sequence: standard gather loop at the first non-hub node.
        /// Gives new players a working example to duplicate and modify.
        /// </summary>
        public static TaskSequence CreateDefaultGatherSequence(string hubNodeId, string targetNodeId, string targetNodeName)
        {
            return new TaskSequence
            {
                Id = DefaultGatherSequenceId,
                Name = $"Gather at {targetNodeName}",
                TargetNodeId = targetNodeId,
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, targetNodeId),
                    new TaskStep(TaskStepType.Work, microRulesetId: DefaultMicroId),
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

            // Ensure default gather sequence exists — targets the first non-hub node
            bool hasGather = false;
            foreach (var s in state.TaskSequenceLibrary)
                if (s.Id == DefaultGatherSequenceId) { hasGather = true; break; }

            if (!hasGather && state.Map?.Nodes != null)
            {
                // Use the first non-hub node as the gather target
                foreach (var node in state.Map.Nodes)
                {
                    if (node.Id != hubId)
                    {
                        state.TaskSequenceLibrary.Add(
                            CreateDefaultGatherSequence(hubId, node.Id, node.Name ?? node.Id));
                        break;
                    }
                }
            }

            // Ensure default combat styles exist
            EnsureCombatStyle(state, CreateBasicMeleeCombatStyle());
            EnsureCombatStyle(state, CreateBasicMageCombatStyle());
            EnsureCombatStyle(state, CreateBasicHealerCombatStyle());
        }

        private static void EnsureCombatStyle(GameState state, CombatStyle style)
        {
            foreach (var s in state.CombatStyleLibrary)
                if (s.Id == style.Id) return;
            state.CombatStyleLibrary.Add(style);
        }

        // ─── Combat Styles ──────────────────────────────────────

        /// <summary>
        /// Basic Melee combat style: always target nearest enemy, always use Basic Attack.
        /// Simple and universal. Players add to this, not replace it.
        /// </summary>
        public static CombatStyle CreateBasicMeleeCombatStyle()
        {
            return new CombatStyle
            {
                Id = BasicMeleeCombatStyleId,
                Name = "Basic Melee",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Attack nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
        }

        /// <summary>
        /// Basic Mage combat style: target nearest enemy, prefer Fireball, fall back to Basic Attack.
        /// </summary>
        public static CombatStyle CreateBasicMageCombatStyle()
        {
            return new CombatStyle
            {
                Id = BasicMageCombatStyleId,
                Name = "Basic Mage",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Attack nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Fireball",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "fireball",
                        Enabled = true,
                    },
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
        }

        /// <summary>
        /// Basic Healer combat style: target lowest HP ally, use Heal.
        /// </summary>
        public static CombatStyle CreateBasicHealerCombatStyle()
        {
            return new CombatStyle
            {
                Id = BasicHealerCombatStyleId,
                Name = "Basic Healer",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Heal weakest ally",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.LowestHpAlly,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Heal",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "heal",
                        Enabled = true,
                    },
                },
            };
        }

        // ─── Built-in Templates ─────────────────────────────────

        /// <summary>
        /// Ensure built-in step and rule templates exist in the library.
        /// Called alongside EnsureInLibrary during StartNewGame and LoadState.
        /// Idempotent — skips templates that already exist.
        /// </summary>
        public static void EnsureTemplatesInLibrary(GameState state)
        {
            string hubId = state.Map?.HubNodeId ?? "hub";

            // ─── Step Templates ───
            EnsureStepTemplate(state, new StepTemplate
            {
                Id = GatherLoopTemplateId,
                Name = "Gather Loop",
                IsBuiltIn = true,
                IsFavorite = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo), // null TargetNodeId — resolved on apply
                    new TaskStep(TaskStepType.Work, microRulesetId: DefaultMicroId),
                    new TaskStep(TaskStepType.TravelTo, hubId),
                    new TaskStep(TaskStepType.Deposit),
                },
            });

            EnsureStepTemplate(state, new StepTemplate
            {
                Id = TravelAndWorkTemplateId,
                Name = "Travel & Work",
                IsBuiltIn = true,
                IsFavorite = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo), // null TargetNodeId — resolved on apply
                    new TaskStep(TaskStepType.Work, microRulesetId: DefaultMicroId),
                },
            });

            EnsureStepTemplate(state, new StepTemplate
            {
                Id = ReturnAndDepositTemplateId,
                Name = "Return & Deposit",
                IsBuiltIn = true,
                IsFavorite = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, hubId),
                    new TaskStep(TaskStepType.Deposit),
                },
            });

            // ─── Micro Rule Templates ───
            EnsureRuleTemplate(state.MicroRuleTemplateLibrary, new RuleTemplate
            {
                Id = BasicGatherTemplateId,
                Name = "Basic Gather",
                IsBuiltIn = true,
                IsFavorite = true,
                Rules = new List<Rule>
                {
                    new Rule
                    {
                        Label = "Deposit when full",
                        Conditions = { Condition.InventoryFull() },
                        Action = AutomationAction.FinishTask(),
                        Enabled = true,
                    },
                    new Rule
                    {
                        Label = "Gather any resource",
                        Conditions = { Condition.Always() },
                        Action = AutomationAction.GatherAny(),
                        Enabled = true,
                    },
                },
            });

            // No built-in macro rule templates (intentionally blank-slate).
        }

        private static void EnsureStepTemplate(GameState state, StepTemplate template)
        {
            foreach (var t in state.StepTemplateLibrary)
                if (t.Id == template.Id) return;
            state.StepTemplateLibrary.Add(template);
        }

        private static void EnsureRuleTemplate(List<RuleTemplate> library, RuleTemplate template)
        {
            foreach (var t in library)
                if (t.Id == template.Id) return;
            library.Add(template);
        }
    }
}
