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

    // ─── Simulation Lifecycle ────────────────────────────────────

    public struct SimulationTickCompleted
    {
        public long TickNumber;
    }
}
