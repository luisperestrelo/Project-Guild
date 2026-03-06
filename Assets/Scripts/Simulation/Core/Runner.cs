using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Cosmetic gender for a runner. Drives name generation and visual selection.
    /// No gameplay impact.
    /// </summary>
    public enum RunnerGender
    {
        Male,
        Female,
    }

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
        Waiting,
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
        public RunnerGender Gender;
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

        // Combat state (populated when State == Fighting)
        public FightingState Fighting;

        // Death state (populated when State == Dead)
        public DeathState Death;

        // Waiting state (populated when State == Waiting)
        public WaitingState Waiting;

        /// <summary>
        /// Current hitpoints. Starts at max and decreases from combat damage.
        /// Restored to max on respawn. -1 means uninitialized (set on first combat entry).
        /// </summary>
        public float CurrentHitpoints = -1f;

        /// <summary>
        /// Current mana. Used only by Restoration abilities.
        /// Regenerates at a flat rate per tick. -1 means uninitialized.
        /// </summary>
        public float CurrentMana = -1f;

        /// <summary>
        /// Tick count when mana was last spent (ability with ManaCost > 0).
        /// -1 means mana has never been spent. Used by UI to show/hide mana bars
        /// ("recently used" = within 100 ticks or mana not at max).
        /// </summary>
        public long LastManaSpentTick = -1;

        /// <summary>
        /// ID reference into CombatStyleLibrary. Null means no combat style assigned.
        /// A runner with no combat style will idle in combat ("let it break").
        /// </summary>
        public string CombatStyleId;

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
        /// Context from the deferred rule that set PendingTaskSequenceId.
        /// Used by the Decision Log to show a complete entry when the pending executes.
        /// </summary>
        public float PendingSetAtGameTime;
        public string PendingConditionSnapshot;
        public string PendingRuleLabel;

        /// <summary>
        /// Set true when AdvanceRunnerStepIndex wraps from the last step back to step 0.
        /// Cleared on AssignRunner (fresh assignment starts at step 0 but hasn't looped yet).
        /// Used to distinguish "starting a new sequence at step 0" from "looped back to step 0
        /// after completing all steps" — deferred pending actions only apply on loop-back.
        /// </summary>
        public bool CompletedAtLeastOneCycle;

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
        /// Human-readable warning message when the runner is stuck.
        /// Null = no warning. Set when NoMicroRuleMatched or GatheringFailed occurs.
        /// Cleared when the runner transitions to a productive activity.
        /// </summary>
        public string ActiveWarning;

        /// <summary>
        /// Human-readable config warning when the runner's macro ruleset has broken references
        /// (e.g., rules pointing to deleted task sequences). Null = no warning.
        /// Set/cleared by RefreshMacroConfigWarnings() at relevant command trigger points.
        /// </summary>
        public string MacroConfigWarning;

        /// <summary>
        /// ID reference into MacroRulesetLibrary.
        /// </summary>
        public string MacroRulesetId;

        /// <summary>
        /// Per-Work-step micro ruleset overrides. When set, the runner uses the override
        /// MicroRulesetId instead of the one specified in the TaskSequence step.
        /// Keyed by step index. Cleared on task sequence reassignment.
        /// </summary>
        public List<MicroOverride> MicroOverrides;

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
    /// A per-step micro ruleset override on a runner. Overrides the MicroRulesetId
    /// that the TaskSequence step specifies, without touching the shared template.
    /// </summary>
    [Serializable]
    public class MicroOverride
    {
        public int StepIndex;
        public string MicroRulesetId;
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

        /// <summary>
        /// Which physical spot within the gatherable's spot group this runner occupies.
        /// Assigned once when gathering starts or when switching gatherables (micro rules).
        /// Stable — does not shift when other runners arrive or depart.
        /// </summary>
        public int SpotIndex;

        public float TickAccumulator;
        public float TicksRequired;

        // ─── Transit Phase ──────────────────────────────────────
        // Distance from runner's position to the gathering spot.
        // 0 = no transit (scene not loaded, already at spot, or old save).

        public float TransitDistance;
        public float TransitDistanceCovered;

        /// <summary>
        /// True while the runner is still walking to the gathering spot.
        /// </summary>
        public bool IsInTransit => TransitDistance > 0f && TransitDistanceCovered < TransitDistance;

        /// <summary>
        /// 0.0 to 1.0 progress through the transit phase. 1.0 when no transit or complete.
        /// </summary>
        public float TransitProgress => TransitDistance > 0f
            ? Math.Min(TransitDistanceCovered / TransitDistance, 1f) : 1f;
    }

    /// <summary>
    /// State tracked while a runner is depositing at the hub.
    /// Simple countdown — when TicksRemaining hits 0, items are deposited.
    /// </summary>
    [Serializable]
    public class DepositingState
    {
        public int TicksRemaining;

        // ─── Transit Phase ──────────────────────────────────────
        // Distance from runner's position to the deposit point.
        // 0 = no transit (scene not loaded, no deposit point, or old save).

        public float TransitDistance;
        public float TransitDistanceCovered;

        /// <summary>
        /// True while the runner is still walking to the deposit point.
        /// </summary>
        public bool IsInTransit => TransitDistance > 0f && TransitDistanceCovered < TransitDistance;

        /// <summary>
        /// 0.0 to 1.0 progress through the transit phase. 1.0 when no transit or complete.
        /// </summary>
        public float TransitProgress => TransitDistance > 0f
            ? Math.Min(TransitDistanceCovered / TransitDistance, 1f) : 1f;
    }

    /// <summary>
    /// State tracked while a runner is traveling between world nodes.
    /// Travel has two phases:
    /// 1. Exit phase (optional): runner walks from current position to node edge.
    ///    Burns ExitDistance at in-node speed. Sim-driven so the view stays in sync.
    /// 2. Overworld phase: runner travels between nodes at athletics-based travel speed.
    ///    Burns TotalDistance at overworld speed.
    /// </summary>
    [Serializable]
    public class TravelState
    {
        public string FromNodeId;
        public string ToNodeId;
        public float TotalDistance;
        public float DistanceCovered;

        // ─── Exit Phase ──────────────────────────────────────────
        // Distance from runner's position to the node exit point.
        // 0 = no exit phase (scene not loaded, instant exit, or redirect from overworld).

        public float ExitDistance;
        public float ExitDistanceCovered;

        /// <summary>
        /// True while the runner is still walking to the node exit point.
        /// </summary>
        public bool IsExitingNode => ExitDistance > 0f && ExitDistanceCovered < ExitDistance;

        /// <summary>
        /// 0.0 to 1.0 progress through the exit phase. 1.0 when no exit phase or complete.
        /// </summary>
        public float ExitProgress => ExitDistance > 0f ? ExitDistanceCovered / ExitDistance : 1f;

        /// <summary>
        /// 0.0 to 1.0 progress along the overworld travel path (excludes exit phase).
        /// Used for path interpolation in the view layer.
        /// </summary>
        public float Progress => TotalDistance > 0 ? DistanceCovered / TotalDistance : 1f;

        /// <summary>
        /// 0.0 to 1.0 combined progress across both exit + overworld phases.
        /// Used for UI display (progress bar, ETA).
        /// </summary>
        public float OverallProgress
        {
            get
            {
                float totalCombined = ExitDistance + TotalDistance;
                return totalCombined > 0f
                    ? (ExitDistanceCovered + DistanceCovered) / totalCombined
                    : 1f;
            }
        }

        /// <summary>
        /// When set, the view layer uses these coordinates as the travel start point
        /// instead of the FromNode's world position. Used for redirect so the
        /// runner's visual position stays continuous (no teleport snap).
        /// Null means "use FromNode position as usual."
        /// </summary>
        public float? StartWorldX;
        public float? StartWorldZ;
    }

    /// <summary>
    /// State tracked while a runner is fighting enemies at a combat node.
    /// </summary>
    [Serializable]
    public class FightingState
    {
        /// <summary>
        /// Node where combat is taking place.
        /// </summary>
        public string NodeId;

        /// <summary>
        /// Instance ID of the enemy currently being targeted. Null = no target.
        /// </summary>
        public string CurrentTargetEnemyId;

        /// <summary>
        /// Runner ID of the ally currently being targeted (for healing). Null = targeting enemy.
        /// </summary>
        public string CurrentTargetAllyId;

        /// <summary>
        /// Ability currently being executed. Null = not mid-action.
        /// </summary>
        public string CurrentAbilityId;

        /// <summary>
        /// Ticks remaining on the current ability. 0 = ready to act.
        /// </summary>
        public int ActionTicksRemaining;

        /// <summary>
        /// Total ticks for the current ability (for progress display).
        /// </summary>
        public int ActionTicksTotal;

        /// <summary>
        /// True when the runner is leaving combat (walking to exit).
        /// Enemies can still hit them during this phase.
        /// </summary>
        public bool IsDisengaging;

        /// <summary>
        /// Ticks remaining on the disengage exit walk.
        /// </summary>
        public int DisengageTicksRemaining;

        /// <summary>
        /// Per-ability cooldown tracking. Key = abilityId, Value = ticks remaining.
        /// Decremented every tick.
        /// </summary>
        public Dictionary<string, int> CooldownTrackers = new();

        /// <summary>
        /// True while the runner is executing an ability (committed to action).
        /// </summary>
        public bool IsActing => ActionTicksRemaining > 0;

        /// <summary>
        /// 0.0 to 1.0 progress on the current ability cast.
        /// </summary>
        public float ActionProgress => ActionTicksTotal > 0
            ? 1f - (float)ActionTicksRemaining / ActionTicksTotal
            : 1f;
    }

    /// <summary>
    /// State tracked while a runner is waiting at a node for conditions to change.
    /// Used for group coordination (e.g. "wait until 3 allies arrive").
    /// </summary>
    [Serializable]
    public class WaitingState
    {
        /// <summary>
        /// Node where the runner is waiting.
        /// </summary>
        public string NodeId;
    }

    /// <summary>
    /// State tracked while a runner is dead and waiting to respawn.
    /// </summary>
    [Serializable]
    public class DeathState
    {
        /// <summary>
        /// Ticks remaining until the runner respawns.
        /// </summary>
        public int RespawnTicksRemaining;

        /// <summary>
        /// Node where the runner died (for death log / chronicle).
        /// </summary>
        public string DeathNodeId;

        /// <summary>
        /// 0.0 to 1.0 respawn progress.
        /// </summary>
        public float RespawnProgress(int totalTicks) =>
            totalTicks > 0 ? 1f - (float)RespawnTicksRemaining / totalTicks : 1f;
    }
}
