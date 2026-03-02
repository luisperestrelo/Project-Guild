namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Identifies which template library to operate on for shared commands
    /// (rename, reorder, delete).
    /// </summary>
    public enum TemplateKind
    {
        Step,
        MacroRule,
        MicroRule,
    }
}
