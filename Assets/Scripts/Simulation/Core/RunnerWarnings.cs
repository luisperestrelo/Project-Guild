namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Centralized runner warning messages set on Runner.ActiveWarning.
    /// All warning strings live here so they're easy to find, update, and reference.
    /// Use the parameterized methods for warnings that include dynamic data.
    /// </summary>
    public static class RunnerWarnings
    {
        // ─── Generic ────────────────────────────────────
        public const string Stuck = "Runner is stuck";

        // ─── Task Sequence ──────────────────────────────
        public const string NoSteps = "Task sequence has no steps";
        public const string LoopingWithoutProgress = "Task sequence is looping without making progress";

        // ─── Gathering ──────────────────────────────────
        public const string NoGatherablesAtNode = "No gatherables at this node";
        public const string NoEligibleGatherables = "No eligible gatherables at this node";

        // ─── Micro Rules ────────────────────────────────
        public const string NoMicroRulesConfigured = "No micro rules configured";

        public static string NoMicroRuleMatched(string nodeId, int rulesEvaluated)
        {
            return $"No micro rule matched at {nodeId} ({rulesEvaluated} rules evaluated)";
        }

        // ─── Skill ──────────────────────────────────────
        public static string SkillTooLow(SkillType skill, int currentLevel, int requiredLevel)
        {
            return $"{skill} level too low ({currentLevel}/{requiredLevel})";
        }

    }
}
