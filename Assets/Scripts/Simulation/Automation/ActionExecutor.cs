using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Translates automation actions into GameSimulation commands.
    /// Stateless — all methods are static, all side effects go through GameSimulation.
    ///
    /// NOT ACTIVE in Phase 3 — the rule engine exists as data/infrastructure only.
    /// Phase 4 (macro layer) will rework this to execute task-level actions
    /// (travel, gather, deposit) as part of the assignment/task system.
    /// </summary>
    public static class ActionExecutor
    {
        // Placeholder — Phase 4 will implement task-driven action execution.
        // The previous implementation coupled directly to GameSimulation internals
        // that have been reverted to hardcoded Phase 2 behavior.
    }
}
