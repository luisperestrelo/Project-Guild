namespace ProjectGuild.Simulation.Automation
{
    public enum ActionType
    {
        Idle,               // Do nothing, cancel current activity
        TravelTo,           // StringParam = nodeId
        ReturnToHub,        // Travel to hub node
        DepositAndResume,   // Deposit at hub, return to previous gathering node
        FleeToHub,          // Emergency return, ignores FinishCurrentTrip

        // ─── Macro actions (change assignment) ───
        WorkAt,             // StringParam = nodeId — create work loop at target node

        // ─── Micro actions (within-task behavior) ───
        GatherHere,         // IntParam = gatherableIndex at current node
        FinishTask,         // Signal macro to advance past current Work step
    }
}
