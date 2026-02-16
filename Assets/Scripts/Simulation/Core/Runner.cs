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
    /// - TaskSequence (macro layer): what step sequence to execute
    /// - MacroRuleset: rules that change task sequences based on conditions
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

        /// <summary>
        /// Virtual world position captured when travel is interrupted mid-journey.
        /// Used by StartTravelInternal to start new travel from the runner's
        /// actual visual position instead of snapping to CurrentNodeId.
        /// Cleared after being consumed.
        /// </summary>
        public float? RedirectWorldX;
        public float? RedirectWorldZ;

        // Gathering state (populated when State == Gathering)
        public GatheringState Gathering;

        // Depositing state (populated when State == Depositing)
        public DepositingState Depositing;

        // ─── Automation ────────────────────────────────────────────

        /// <summary>
        /// ID reference into TaskSequenceLibrary for the runner's active task sequence.
        /// Null means no active sequence (runner is idle with no standing orders).
        /// </summary>
        public string TaskSequenceId;

        /// <summary>
        /// Per-runner progress through the current task sequence.
        /// The TaskSequence itself is a pure template; this tracks where the runner is in it.
        /// </summary>
        public int TaskSequenceCurrentStepIndex;

        /// <summary>
        /// ID reference into TaskSequenceLibrary for the deferred task sequence
        /// from a FinishCurrentSequence rule. Applied when the current sequence cycle completes.
        /// </summary>
        public string PendingTaskSequenceId;

        /// <summary>
        /// When true, macro rules are skipped until the current sequence loops
        /// (step index wraps back to 0). Used by "Work At" to guarantee one cycle.
        /// Cleared automatically on loop wrap, or when the sequence is cancelled/replaced.
        /// </summary>
        public bool MacroSuspendedUntilLoop;

        /// <summary>
        /// Tracks the Id of the last completed (non-looping) sequence.
        /// Used by same-assignment suppression to prevent re-assigning the same
        /// sequence that just completed (e.g., ReturnToHub when already at hub).
        /// Cleared when a different sequence is assigned.
        /// </summary>
        public string LastCompletedTaskSequenceId;

        /// <summary>
        /// ID reference into MacroRulesetLibrary.
        /// </summary>
        public string MacroRulesetId;

        // ─── Legacy fields for save migration (populated in old saves) ───
        // These are read by MigrateRunnerRulesets() during LoadState, then cleared.
        // New saves only populate the ID fields above.
        public TaskSequence TaskSequence;
        public TaskSequence PendingTaskSequence;
        public Ruleset MacroRuleset;
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
    /// The macro layer (TaskSequence) handles the gather→deposit→return cycle.
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
