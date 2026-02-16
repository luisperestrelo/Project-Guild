namespace ProjectGuild.Simulation.Automation
{
    public enum ActionType
    {
        // ─── Macro actions (select task sequence) ───
        Idle = 0,

        // ─── Micro actions (within-task behavior) ───
        GatherHere = 3,         // IntParam = gatherableIndex at current node (-1 = any)
        FinishTask = 4,         // Signal macro to advance past current Work step

        // ─── Library-reference macro action ───
        AssignSequence = 5,     // StringParam = taskSequenceId in library
    }
}
