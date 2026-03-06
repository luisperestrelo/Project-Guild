using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    public enum TaskStepType
    {
        TravelTo,
        Work,
        Deposit,
    }

    [Serializable]
    public class TaskStep
    {
        public TaskStepType Type;
        public string TargetNodeId; // for TravelTo

        /// <summary>
        /// For Work steps: which micro ruleset governs behavior during this step.
        /// Always explicit — no implicit fallbacks. The UI enforces selection when creating a Work step.
        /// Null/empty on a Work step = misconfiguration (let it break).
        /// Ignored for non-Work steps.
        /// </summary>
        public string MicroRulesetId;

        /// <summary>
        /// For Work steps: optional per-step combat style override.
        /// When set, the runner uses this combat style instead of their runner-level CombatStyleId.
        /// Null = use runner's default combat style. Same pattern as MicroRulesetId.
        /// </summary>
        public string CombatStyleOverrideId;

        public TaskStep() { }

        public TaskStep(TaskStepType type, string targetNodeId = null, string microRulesetId = null,
            string combatStyleOverrideId = null)
        {
            Type = type;
            TargetNodeId = targetNodeId;
            MicroRulesetId = microRulesetId;
            CombatStyleOverrideId = combatStyleOverrideId;
        }
    }

    /// <summary>
    /// A runner's task sequence — an ordered list of steps.
    /// The sequence handles WHERE to go and WHEN to deposit.
    /// Micro rules decide WHAT to do at each node (gather, fight, craft).
    /// Null sequence = idle (no standing orders).
    /// When Loop is true, the sequence wraps back to step 0 after the last step.
    /// When Loop is false, the sequence ends after the last step and the runner goes idle.
    /// </summary>
    [Serializable]
    public class TaskSequence
    {
        public TaskSequence DeepCopy()
        {
            var copy = new TaskSequence
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name,
                AutoGenerateName = AutoGenerateName,
                Loop = Loop,
                TargetNodeId = TargetNodeId,
                Steps = new List<TaskStep>(),
            };
            if (Steps != null)
            {
                foreach (var step in Steps)
                    copy.Steps.Add(new TaskStep(step.Type, step.TargetNodeId, step.MicroRulesetId,
                        step.CombatStyleOverrideId));
            }
            return copy;
        }

        /// <summary>Unique identifier for library lookups. Null when not in a library.</summary>
        public string Id;

        public List<TaskStep> Steps;
        public bool Loop;

        /// <summary>
        /// Display name for this sequence (e.g. "Gather at Copper Mine").
        /// </summary>
        public string Name;

        /// <summary>
        /// When true, the name is auto-derived from the sequence's steps
        /// and updates automatically as steps change.
        /// Typing a custom name in the UI sets this to false.
        /// </summary>
        public bool AutoGenerateName;

        // Metadata for display / same-sequence suppression
        public string TargetNodeId;  // primary node

        // ─── Factory Methods ──────────────────────────────────────

        /// <summary>
        /// Standard work loop: Travel to node → Work → Travel to hub → Deposit → repeat.
        /// What happens during the Work step is determined by the runner's micro rules.
        /// </summary>
        public static TaskSequence CreateLoop(string nodeId, string hubNodeId, string microRulesetId = null, string nodeName = null)
        {
            return new TaskSequence
            {
                Id = $"work-loop-{nodeId}",
                Name = $"Gather at {nodeName ?? nodeId}",
                AutoGenerateName = false, // WorkAt creates with a specific name
                TargetNodeId = nodeId,
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, nodeId),
                    new TaskStep(TaskStepType.Work, microRulesetId: microRulesetId ?? DefaultRulesets.DefaultMicroId),
                    new TaskStep(TaskStepType.TravelTo, hubNodeId),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
        }

        /// <summary>
        /// Standard combat loop: TravelTo(node) → Work(combat micro) → TravelTo(hub) → Deposit.
        /// </summary>
        public static TaskSequence CreateCombatLoop(string nodeId, string hubNodeId, string microRulesetId = null, string nodeName = null)
        {
            return new TaskSequence
            {
                Id = $"fight-loop-{nodeId}",
                Name = $"Fight at {nodeName ?? nodeId}",
                AutoGenerateName = false,
                TargetNodeId = nodeId,
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, nodeId),
                    new TaskStep(TaskStepType.Work, microRulesetId: microRulesetId ?? DefaultRulesets.DefaultCombatMicroId),
                    new TaskStep(TaskStepType.TravelTo, hubNodeId),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
        }

        /// <summary>
        /// Send to hub: Travel to hub → Deposit. Non-looping (runner goes Idle after).
        /// </summary>
        public static TaskSequence CreateSendToHub(string hubNodeId)
        {
            return new TaskSequence
            {
                Id = $"send-to-hub-{hubNodeId}",
                Name = "Send to Guild Hall",
                AutoGenerateName = false,
                TargetNodeId = hubNodeId,
                Loop = false,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, hubNodeId),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
        }
    }
}
