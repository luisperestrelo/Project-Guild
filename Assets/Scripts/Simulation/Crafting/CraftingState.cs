using System;

namespace ProjectGuild.Simulation.Crafting
{
    /// <summary>
    /// State tracked while a runner is crafting at a station.
    /// </summary>
    [Serializable]
    public class CraftingState
    {
        public string NodeId;
        public string RecipeId;
        public int TicksRemaining;
        public int TicksTotal;

        public float Progress => TicksTotal > 0 ? 1f - (float)TicksRemaining / TicksTotal : 1f;
    }
}
