namespace ProjectGuild.Simulation.Automation
{
    public enum ActionType
    {
        // ─── Macro actions (select task sequence) ───
        Idle = 0,

        // ─── Micro actions (within-task behavior) ───
        GatherHere = 3,         // IntParam = gatherableIndex at current node (-1 = any)
        FinishTask = 4,         // Signal macro to advance past current Work step
        GatherBestAvailable = 6, // IntParam = (int)SkillType — gather highest-tier resource for that skill
        FightHere = 7,          // Start fighting at the current node (combat micro action)
        Wait = 8,               // Wait at node until conditions change (micro action)
        CraftHere = 9,          // Craft at current node. StringParam = recipeId

        // ─── Library-reference macro action ───
        AssignSequence = 5,     // StringParam = taskSequenceId in library
    }
}
