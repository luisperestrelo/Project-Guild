using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// The root of all simulation state. Everything that needs to be saved/loaded
    /// lives under this object. This is the single source of truth for the game world.
    /// </summary>
    [Serializable]
    public class GameState
    {
        public List<Runner> Runners = new();
        public long TickCount;
        public float TotalTimeElapsed;

        public WorldMap Map;
        public Bank Bank = new();
        public DecisionLog DecisionLog = new();

        // ─── Global Automation Libraries ────────────────────────────
        // Named templates with IDs. Runners hold string refs into these.
        // Editing a library entry immediately affects all runners/sequences referencing it.
        public List<TaskSequence> TaskSequenceLibrary = new();
        public List<Ruleset> MacroRulesetLibrary = new();
        public List<Ruleset> MicroRulesetLibrary = new();

        // Economy state (TODO)
    }
}
