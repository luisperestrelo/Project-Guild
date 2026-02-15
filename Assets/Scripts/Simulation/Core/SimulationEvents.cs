namespace ProjectGuild.Simulation.Core
{
    // ─── Runner Events ───────────────────────────────────────────

    public struct RunnerCreated
    {
        public string RunnerId;
        public string RunnerName;
    }

    public struct RunnerSkillLeveledUp
    {
        public string RunnerId;
        public SkillType Skill;
        public int NewLevel;
    }

    // ─── Travel Events ───────────────────────────────────────────

    public struct RunnerStartedTravel
    {
        public string RunnerId;
        public string FromNodeId;
        public string ToNodeId;
        public float EstimatedDurationSeconds;
    }

    public struct RunnerArrivedAtNode
    {
        public string RunnerId;
        public string NodeId;
    }

    // ─── Gathering Events ────────────────────────────────────────

    public struct GatheringStarted
    {
        public string RunnerId;
        public string NodeId;
        public string ItemId;
        public SkillType Skill;
    }

    public struct GatheringFailed
    {
        public string RunnerId;
        public string NodeId;
        public string ItemId;
        public SkillType Skill;
        public int RequiredLevel;
        public int CurrentLevel;
    }

    public struct ItemGathered
    {
        public string RunnerId;
        public string ItemId;
        public int InventoryFreeSlots;
    }

    public struct InventoryFull
    {
        public string RunnerId;
    }

    public struct RunnerDeposited
    {
        public string RunnerId;
        public int ItemsDeposited;
    }

    // ─── Assignment Events ────────────────────────────────────────

    public struct AssignmentChanged
    {
        public string RunnerId;
        public string TargetNodeId;
        public string Reason;
    }

    public struct AssignmentStepAdvanced
    {
        public string RunnerId;
        public Automation.TaskStepType StepType;
        public int StepIndex;
    }

    // ─── Automation Events ───────────────────────────────────────

    public struct AutomationRuleFired
    {
        public string RunnerId;
        public int RuleIndex;
        public string RuleLabel;
        public string TriggerReason;
        public Automation.ActionType ActionType;
        public bool WasDeferred;
    }

    public struct AutomationPendingActionExecuted
    {
        public string RunnerId;
        public Automation.ActionType ActionType;
        public string ActionDetail;
    }

    public struct NoMicroRuleMatched
    {
        public string RunnerId;
        public string RunnerName;
        public string NodeId;
        /// <summary>
        /// True if the micro ruleset has zero rules (player deleted all rules).
        /// False if rules exist but none matched the current conditions.
        /// </summary>
        public bool RulesetIsEmpty;
        public int RuleCount;
    }

    // ─── Simulation Lifecycle ────────────────────────────────────

    public struct SimulationTickCompleted
    {
        public long TickNumber;
    }
}
