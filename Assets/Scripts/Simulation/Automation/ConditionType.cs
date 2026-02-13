namespace ProjectGuild.Simulation.Automation
{
    public enum ConditionType
    {
        Always,             // Always true (fallback)
        InventoryFull,      // No parameters needed
        InventorySlots,     // Operator + NumericValue (checks FreeSlots)
        InventoryContains,  // StringParam (itemId) + Operator + NumericValue (count)
        BankContains,       // StringParam (itemId) + Operator + NumericValue (count)
        SkillLevel,         // IntParam (SkillType cast) + Operator + NumericValue (level)
        RunnerStateIs,      // IntParam (RunnerState cast)
        AtNode,             // StringParam (nodeId)
        SelfHP,             // Operator + NumericValue (percentage 0-100)
    }
}
