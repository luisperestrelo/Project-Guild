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

        public TaskStep() { }

        public TaskStep(TaskStepType type, string targetNodeId = null)
        {
            Type = type;
            TargetNodeId = targetNodeId;
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
        public List<TaskStep> Steps;
        public int CurrentStepIndex;
        public bool Loop;

        /// <summary>
        /// Display name for this sequence (e.g. "Gather at Copper Mine").
        /// </summary>
        public string Name;

        // Metadata for display / same-sequence suppression
        public string TargetNodeId;  // primary node

        public TaskStep CurrentStep =>
            Steps != null && CurrentStepIndex >= 0 && CurrentStepIndex < Steps.Count
                ? Steps[CurrentStepIndex]
                : null;

        /// <summary>
        /// Advance to next step. Returns true if there is a next step to execute.
        /// Returns false if the sequence is done (non-looping, past the end).
        /// </summary>
        public bool AdvanceStep()
        {
            if (Steps == null || Steps.Count == 0) return false;

            CurrentStepIndex++;
            if (CurrentStepIndex >= Steps.Count)
            {
                if (Loop)
                {
                    CurrentStepIndex = 0;
                    return true;
                }

                CurrentStepIndex = Steps.Count; // park past end
                return false;
            }
            return true;
        }

        // ─── Factory Methods ──────────────────────────────────────

        /// <summary>
        /// Standard work loop: Travel to node → Work → Travel to hub → Deposit → repeat.
        /// What happens during the Work step is determined by the runner's micro rules.
        /// </summary>
        public static TaskSequence CreateLoop(string nodeId, string hubNodeId)
        {
            return new TaskSequence
            {
                Name = $"Gather at {nodeId}",
                TargetNodeId = nodeId,
                Loop = true,
                CurrentStepIndex = 0,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, nodeId),
                    new TaskStep(TaskStepType.Work),
                    new TaskStep(TaskStepType.TravelTo, hubNodeId),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
        }
    }
}
