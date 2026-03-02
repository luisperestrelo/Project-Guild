using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// A reusable template of task steps that can be batch-inserted into a TaskSequence.
    /// Built-in templates (Gather Loop, Travel & Work, Return & Deposit) are created by
    /// DefaultRulesets and cannot be deleted. Players can create custom templates from
    /// existing sequences.
    /// </summary>
    [Serializable]
    public class StepTemplate
    {
        public string Id;
        public string Name;
        public bool IsBuiltIn;

        /// <summary>
        /// When true, this template appears as a quick-access button in the editor.
        /// Built-in templates default to true. Players can toggle this.
        /// </summary>
        public bool IsFavorite;

        public List<TaskStep> Steps = new();

        /// <summary>
        /// Deep-copy the steps list for safe insertion into a sequence.
        /// Each step is independently copied so mutations don't affect the template.
        /// </summary>
        public List<TaskStep> DeepCopySteps()
        {
            var copy = new List<TaskStep>(Steps.Count);
            foreach (var step in Steps)
                copy.Add(new TaskStep(step.Type, step.TargetNodeId, step.MicroRulesetId));
            return copy;
        }
    }
}
