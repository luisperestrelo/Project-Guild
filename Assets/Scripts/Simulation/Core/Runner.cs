using System;
using ProjectGuild.Simulation.Automation;
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
        Depositing,
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
    /// - Assignment (macro layer): what task sequence to execute
    /// - MacroRuleset: rules that change assignments based on conditions
    /// - MicroRuleset: rules for within-task behavior (e.g. which resource to gather)
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

        // Depositing state (populated when State == Depositing)
        public DepositingState Depositing;

        // ─── Automation ────────────────────────────────────────────

        /// <summary>
        /// Current macro assignment — the task sequence this runner is executing.
        /// Null means no assignment (runner is idle with no standing orders).
        /// </summary>
        public Assignment Assignment;

        /// <summary>
        /// Deferred assignment from a FinishCurrentTrip rule.
        /// Applied when the current loop cycle completes.
        /// </summary>
        public Assignment PendingAssignment;

        /// <summary>
        /// Rules that change the assignment based on conditions
        /// (e.g. BankContains threshold → switch gathering target).
        /// </summary>
        public Ruleset MacroRuleset;

        /// <summary>
        /// Rules for within-task behavior — which resource to gather,
        /// combat rotations, etc. Evaluated during the relevant task step.
        /// </summary>
        public Ruleset MicroRuleset;

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
    /// State tracked while a runner is gathering resources.
    /// Simplified from Phase 2 — no longer tracks sub-state for the auto-return loop.
    /// The macro layer (Assignment) handles the gather→deposit→return cycle.
    /// </summary>
    [Serializable]
    public class GatheringState
    {
        public string NodeId;
        /// <summary>
        /// Which gatherable in the node's Gatherables[] array the runner is working on.
        /// Set by ExecuteGatherStep via micro rules or assignment default.
        /// </summary>
        public int GatherableIndex;
        public float TickAccumulator;
        public float TicksRequired;
    }

    /// <summary>
    /// State tracked while a runner is depositing at the hub.
    /// Simple countdown — when TicksRemaining hits 0, items are deposited.
    /// </summary>
    [Serializable]
    public class DepositingState
    {
        public int TicksRemaining;
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

        /// <summary>
        /// When set, the view layer uses these coordinates as the travel start point
        /// instead of the FromNode's world position. Used for redirect so the
        /// runner's visual position stays continuous (no teleport snap).
        /// Null means "use FromNode position as usual."
        /// </summary>
        public float? StartWorldX;
        public float? StartWorldZ;
    }
}
