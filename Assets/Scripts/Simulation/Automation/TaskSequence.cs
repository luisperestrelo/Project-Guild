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

        public TaskStep() { }

        public TaskStep(TaskStepType type, string targetNodeId = null, string microRulesetId = null)
        {
            Type = type;
            TargetNodeId = targetNodeId;
            MicroRulesetId = microRulesetId;
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
        /// <summary>Unique identifier for library lookups. Null when not in a library.</summary>
        public string Id;

        public List<TaskStep> Steps;
        public bool Loop;

        /// <summary>
        /// Display name for this sequence (e.g. "Gather at Copper Mine").
        /// </summary>
        public string Name;

        // Metadata for display / same-sequence suppression
        public string TargetNodeId;  // primary node

        // ─── Factory Methods ──────────────────────────────────────

        /// <summary>
        /// Standard work loop: Travel to node → Work → Travel to hub → Deposit → repeat.
        /// What happens during the Work step is determined by the runner's micro rules.
        /// </summary>
        public static TaskSequence CreateLoop(string nodeId, string hubNodeId, string microRulesetId = null)
        {
            return new TaskSequence
            {
                Id = $"work-loop-{nodeId}",
                Name = $"Gather at {nodeId}",
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
    }
}
