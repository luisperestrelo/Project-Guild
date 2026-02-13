using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    public enum AssignmentType
    {
        Idle,
        Gather,
        // Future: Raid, Craft
    }

    public enum TaskStepType
    {
        TravelTo,
        Gather,
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
    /// A runner's standing order — what to do and in what sequence.
    /// Contains an explicit task step list (macro layer).
    /// Micro rules decide behavior within individual steps (e.g. which resource to gather).
    /// </summary>
    [Serializable]
    public class Assignment
    {
        public AssignmentType Type;
        public List<TaskStep> Steps;
        public int CurrentStepIndex;
        public bool Loop;

        // Metadata for display
        public string TargetNodeId;  // primary node (for UI label)
        public int GatherableIndex;  // default resource index (for UI label + micro fallback)

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
        /// Standard gather loop: Travel to node → Gather → Travel to hub → Deposit → repeat.
        /// </summary>
        public static Assignment CreateGatherLoop(string nodeId, string hubNodeId, int gatherableIndex = 0)
        {
            return new Assignment
            {
                Type = AssignmentType.Gather,
                TargetNodeId = nodeId,
                GatherableIndex = gatherableIndex,
                Loop = true,
                CurrentStepIndex = 0,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, nodeId),
                    new TaskStep(TaskStepType.Gather),
                    new TaskStep(TaskStepType.TravelTo, hubNodeId),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
        }

        /// <summary>
        /// Idle assignment — no steps. Runner does nothing.
        /// </summary>
        public static Assignment CreateIdle()
        {
            return new Assignment
            {
                Type = AssignmentType.Idle,
                Steps = new List<TaskStep>(),
                CurrentStepIndex = 0,
                Loop = false,
            };
        }
    }
}
