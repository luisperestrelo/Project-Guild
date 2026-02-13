namespace ProjectGuild.Simulation.Automation
{
    public enum ActionType
    {
        Idle,               // Do nothing, cancel current activity
        TravelTo,           // StringParam = nodeId
        GatherAt,           // StringParam = nodeId, IntParam = gatherableIndex
        ReturnToHub,        // Travel to hub node
        DepositAndResume,   // Deposit at hub, return to previous gathering node
        FleeToHub,          // Emergency return, ignores FinishCurrentTrip
    }
}
