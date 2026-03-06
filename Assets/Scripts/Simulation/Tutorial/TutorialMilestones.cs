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

        // ─── Crafting Phase ─────────────────────────────────────────
        public const string Crafting_Intro = "Crafting_Intro";
        public const string Crafting_FirstItemCrafted = "Crafting_FirstItemCrafted";
        public const string Crafting_ItemEquipped = "Crafting_ItemEquipped";
        public const string Crafting_Complete = "Crafting_Complete";

        // ─── Combat Phase ───────────────────────────────────────────
        public const string Combat_Intro = "Combat_Intro";
        public const string Combat_SentToGoblins = "Combat_SentToGoblins";
        public const string Combat_FirstKill = "Combat_FirstKill";
        public const string Combat_Complete = "Combat_Complete";

        // ─── Automation Phase ───────────────────────────────────────
        public const string Automation_Intro = "Automation_Intro";
        public const string Automation_Complete = "Automation_Complete";
    }
}
