using System;
using System.Collections.Generic;
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

        // Bank/inventory state (TODO)
        // Economy state (TODO)
    }
}
