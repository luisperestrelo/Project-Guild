using System;

namespace ProjectGuild.Simulation.Automation
{
    public enum DecisionLayer
    {
        Macro,
        Micro,
    }

    /// <summary>
    /// A single entry in the decision log. Captures what happened when an automation
    /// rule fired: which rule, why it triggered, what the conditions evaluated to,
    /// and what action was taken.
    ///
    /// The decision log is the player's primary debugging tool for automation.
    /// It answers "why did my runner do that?" without requiring the player to
    /// understand the evaluation engine internals.
    /// </summary>
    [Serializable]
    public class DecisionLogEntry
    {
        public long TickNumber;
        public float GameTime;
        public string RunnerId;
        public string RunnerName;
        public string NodeId;
        public DecisionLayer Layer;
        public int RuleIndex;
        public string RuleLabel;
        public string TriggerReason;
        public ActionType ActionType;
        public string ActionDetail;
        public string ConditionSnapshot;
        public bool WasDeferred;
    }
}
