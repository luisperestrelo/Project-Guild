namespace ProjectGuild.Simulation.Tutorial
{
    /// <summary>
    /// Ordered phases of the tutorial. Each phase teaches a core mechanic.
    /// The player advances linearly through these; future phases plug into the same skeleton.
    /// </summary>
    public enum TutorialPhase
    {
        Gathering = 0,
        Crafting = 1,
        Combat = 2,
        Automation = 3,
        FirstRaid = 4,
        Coaching = 5,
        Complete = 6,
    }
}
