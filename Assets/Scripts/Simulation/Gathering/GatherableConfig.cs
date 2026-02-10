using System;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Simulation.Gathering
{
    /// <summary>
    /// Defines what a gathering node type produces and how.
    /// Each world node type (GatheringMine, GatheringForest, etc.) maps to one of these.
    /// BaseTicksToGather is the per-gatherable difficulty — endgame ores take more ticks.
    /// </summary>
    [Serializable]
    public class GatherableConfig
    {
        public NodeType NodeType;
        public string ProducedItemId;
        public SkillType RequiredSkill;
        public float BaseTicksToGather;

        /// <summary>
        /// XP awarded every tick while gathering this resource.
        /// Decoupled from gathering speed — leveling is purely about which resource you're grinding,
        /// while gathering speed only affects economic output (items per trip).
        /// Passion XP multiplier still applies on top of this.
        /// </summary>
        public float XpPerTick;

        /// <summary>
        /// Minimum skill level required to gather this resource. 0 = no requirement.
        /// </summary>
        public int MinLevel;

        public GatherableConfig() { }

        public GatherableConfig(NodeType nodeType, string producedItemId, SkillType requiredSkill,
            float baseTicksToGather, float xpPerTick, int minLevel = 0)
        {
            NodeType = nodeType;
            ProducedItemId = producedItemId;
            RequiredSkill = requiredSkill;
            BaseTicksToGather = baseTicksToGather;
            XpPerTick = xpPerTick;
            MinLevel = minLevel;
        }
    }
}
