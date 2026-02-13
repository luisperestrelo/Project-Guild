using System;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// A named ruleset that can be saved and applied to runners.
    /// Stored in GameState for persistence across save/load.
    /// </summary>
    [Serializable]
    public class RulesetTemplate
    {
        public string Name;
        public Ruleset Ruleset;

        public RulesetTemplate() { }

        public RulesetTemplate(string name, Ruleset ruleset)
        {
            Name = name;
            Ruleset = ruleset;
        }
    }
}
