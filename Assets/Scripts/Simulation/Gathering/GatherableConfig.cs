using System;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Gathering
{
    /// <summary>
    /// Defines a gatherable resource — what it produces, what skill it needs, and how fast.
    /// Gatherables live on WorldNodes, not in a global config. A node can have multiple
    /// gatherables with different level requirements (e.g. a mine with copper and iron).
    /// </summary>
    [Serializable]
    public class GatherableConfig
    {
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

        public GatherableConfig(string producedItemId, SkillType requiredSkill,
            float baseTicksToGather, float xpPerTick, int minLevel = 0)
        {
            ProducedItemId = producedItemId;
            RequiredSkill = requiredSkill;
            BaseTicksToGather = baseTicksToGather;
            XpPerTick = xpPerTick;
            MinLevel = minLevel;
        }
    }
}
