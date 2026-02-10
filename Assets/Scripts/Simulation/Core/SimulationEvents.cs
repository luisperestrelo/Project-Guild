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

    // ─── Simulation Lifecycle ────────────────────────────────────

    public struct SimulationTickCompleted
    {
        public long TickNumber;
    }
}
