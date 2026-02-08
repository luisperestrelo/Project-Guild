using System;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// The state of a runner's current activity.
    /// </summary>
    public enum RunnerState
    {
        Idle,
        Traveling,
        Gathering,
        Crafting,
        Fighting,
        Dead,
    }

    /// <summary>
    /// A runner (autonomous adventurer). This is a pure data + logic class — no Unity dependencies.
    ///
    /// Runners have:
    /// - Identity: unique ID, name
    /// - 15 skills (each with level, XP, optional passion)
    /// - Current state and location in the world
    /// - Inventory (TODO)
    /// - Equipment (TODO)
    /// - Macro Automation rules (TODO)
    /// - Micro Automation rules (TODO)
    /// </summary>
    [Serializable]
    public class Runner
    {
        public string Id;
        public string Name;
        public RunnerState State;
        public string CurrentNodeId;

        // Skills stored as an array indexed by SkillType enum value
        public Skill[] Skills;

        // Travel state (populated when State == Traveling)
        public TravelState Travel;

        public Runner()
        {
            Skills = new Skill[SkillTypeExtensions.SkillCount];
            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                Skills[i] = new Skill
                {
                    Type = (SkillType)i,
                    Level = 1,
                    Xp = 0f,
                    HasPassion = false,
                };
            }
        }

        public Skill GetSkill(SkillType type) => Skills[(int)type];

        /// <summary>
        /// Get the effective level of a skill (including passion bonus).
        /// This is the value used in all gameplay calculations.
        /// </summary>
        public float GetEffectiveLevel(SkillType type, SimulationConfig config) =>
            Skills[(int)type].GetEffectiveLevel(config);
    }

    /// <summary>
    /// State tracked while a runner is traveling between world nodes.
    /// </summary>
    [Serializable]
    public class TravelState
    {
        public string FromNodeId;
        public string ToNodeId;
        public float TotalDistance;
        public float DistanceCovered;

        /// <summary>
        /// 0.0 to 1.0 progress along the travel path.
        /// </summary>
        public float Progress => TotalDistance > 0 ? DistanceCovered / TotalDistance : 1f;
    }
}
