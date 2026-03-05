namespace ProjectGuild.Simulation.Tutorial
{
    /// <summary>
    /// String constants for tutorial milestone IDs. Each milestone is a one-time flag
    /// that advances the tutorial narrative when completed.
    /// Grouped by phase — future phases add their own constants here.
    /// </summary>
    public static class TutorialMilestones
    {
        // ─── Gathering Phase ────────────────────────────────────────
        public const string Gathering_Intro = "Gathering_Intro";
        public const string Gathering_SentRunnerToNode = "Gathering_SentRunnerToNode";
        public const string Gathering_IdleNudgeShown = "Gathering_IdleNudgeShown";
        public const string Gathering_CopperDeposited = "Gathering_CopperDeposited";
        public const string Gathering_Complete = "Gathering_Complete";
    }
}
