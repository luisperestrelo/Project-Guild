using System;
using ProjectGuild.Simulation.Items;

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
    /// - Inventory (OSRS-style 28-slot)
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

        // Inventory — initialized by RunnerFactory with MaxSlots from config
        public Inventory Inventory;

        // Travel state (populated when State == Traveling)
        public TravelState Travel;

        // Gathering state (populated when State == Gathering)
        public GatheringState Gathering;

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
    /// Sub-state for the auto-return loop during gathering.
    /// </summary>
    public enum GatheringSubState
    {
        Gathering,        // Actively gathering at the node
        TravelingToBank,  // Inventory full, heading to hub to deposit
        TravelingToNode,  // Deposited, heading back to resume gathering
    }

    /// <summary>
    /// State tracked while a runner is gathering resources.
    /// Persists across the auto-return loop (gather -> deposit -> return -> resume).
    /// </summary>
    [Serializable]
    public class GatheringState
    {
        public string NodeId;
        /// <summary>
        /// Which gatherable in the node's Gatherables[] array the runner is working on.
        /// Set by CommandGather; preserved across auto-return trips.
        /// </summary>
        public int GatherableIndex;
        public float TickAccumulator;
        public float TicksRequired;
        public GatheringSubState SubState;
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
