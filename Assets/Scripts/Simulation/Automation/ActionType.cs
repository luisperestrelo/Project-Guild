namespace ProjectGuild.Simulation.Automation
{
    public enum ActionType
    {
        // ─── Macro actions (select task sequence) ───
        Idle,               // Clear task sequence, runner goes idle
        WorkAt,             // StringParam = nodeId — standard gather loop at target node
        ReturnToHub,        // Travel to hub (1-step non-looping sequence)

        // ─── Micro actions (within-task behavior) ───
        GatherHere,         // IntParam = gatherableIndex at current node
        FinishTask,         // Signal macro to advance past current Work step
    }
}
