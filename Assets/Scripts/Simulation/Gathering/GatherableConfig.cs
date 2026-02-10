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
        public float BaseXpPerGather;

        /// <summary>
        /// Minimum skill level required to gather this resource. 0 = no requirement.
        /// </summary>
        public int MinLevel;

        public GatherableConfig() { }

        public GatherableConfig(NodeType nodeType, string producedItemId, SkillType requiredSkill,
            float baseTicksToGather, float baseXpPerGather, int minLevel = 0)
        {
            NodeType = nodeType;
            ProducedItemId = producedItemId;
            RequiredSkill = requiredSkill;
            BaseTicksToGather = baseTicksToGather;
            BaseXpPerGather = baseXpPerGather;
            MinLevel = minLevel;
        }
    }
}
