using System;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Tutorial
{
    /// <summary>
    /// Monitors simulation events and advances tutorial milestones.
    /// Pure C# — no Unity dependency.
    /// </summary>
    public class TutorialService
    {
        private readonly Func<GameState> _getState;
        private readonly EventBus _events;
        private readonly float _tickDeltaTime;

        private long _sentRunnerTick = -1;

        /// <summary>
        /// Total goblin kills tracked for the tutorial pawn award.
        /// 25 kills after Culling Frost unlock triggers new pawn.
        /// </summary>
        private int _killsSinceCullingFrost;
        private bool _cullingFrostUnlocked;
        private const int KillsForNewPawn = 10;

        public TutorialService(Func<GameState> getState, EventBus events, float tickDeltaTime)
        {
            _getState = getState;
            _events = events;
            _tickDeltaTime = tickDeltaTime;
        }

        // ─── Subscription ─────────────────────────────────────────

        public void SubscribeAll(EventBus events)
        {
            events.Subscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
            events.Subscribe<RunnerDeposited>(OnRunnerDeposited);
            events.Subscribe<SimulationTickCompleted>(OnSimulationTickCompleted);
            events.Subscribe<CraftingCompleted>(OnCraftingCompleted);
            events.Subscribe<ItemEquipped>(OnItemEquipped);
            events.Subscribe<EnemyDied>(OnEnemyDied);
            events.Subscribe<RunnerSkillLeveledUp>(OnSkillLeveledUp);
        }

        public void UnsubscribeAll(EventBus events)
        {
            events.Unsubscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
            events.Unsubscribe<RunnerDeposited>(OnRunnerDeposited);
            events.Unsubscribe<SimulationTickCompleted>(OnSimulationTickCompleted);
            events.Unsubscribe<CraftingCompleted>(OnCraftingCompleted);
            events.Unsubscribe<ItemEquipped>(OnItemEquipped);
            events.Unsubscribe<EnemyDied>(OnEnemyDied);
            events.Unsubscribe<RunnerSkillLeveledUp>(OnSkillLeveledUp);
        }

        // ─── Initialization ───────────────────────────────────────

        public void InitializeDiscoveredNodes()
        {
            var state = _getState();
            if (state == null) return;

            // Show all nodes from the start (empty list = show all)
            state.Tutorial.DiscoveredNodeIds.Clear();
        }

        public void CompleteIntroMilestone()
        {
            CompleteMilestone(TutorialMilestones.Gathering_Intro);
        }

        public void ForceCompleteMilestone(string milestoneId)
        {
            CompleteMilestone(milestoneId);
        }

        public void SkipTutorial()
        {
            var state = _getState();
            if (state == null) return;

            state.Tutorial.IsActive = false;
            state.Tutorial.DiscoveredNodeIds.Clear();
        }

        public void ResetTutorial()
        {
            var state = _getState();
            if (state == null) return;

            state.Tutorial.IsActive = true;
            state.Tutorial.CurrentPhase = TutorialPhase.Gathering;
            state.Tutorial.CompletedMilestones.Clear();
            _sentRunnerTick = -1;
            InitializeDiscoveredNodes();
            CompleteIntroMilestone();
        }

        public void AdvanceToNextPhase()
        {
            var state = _getState();
            if (state == null) return;

            var tutorial = state.Tutorial;
            if (tutorial.CurrentPhase >= TutorialPhase.Complete) return;

            var completed = tutorial.CurrentPhase;
            tutorial.CurrentPhase = completed + 1;

            OnPhaseTransition(completed, tutorial.CurrentPhase);

            _events.Publish(new TutorialPhaseCompleted
            {
                CompletedPhase = completed,
                NextPhase = tutorial.CurrentPhase,
            });
        }

        public void UnlockNode(string nodeId)
        {
            var state = _getState();
            if (state == null) return;

            if (!state.Tutorial.DiscoveredNodeIds.Contains(nodeId))
                state.Tutorial.DiscoveredNodeIds.Add(nodeId);
        }

        // ─── Event Handlers ───────────────────────────────────────

        private void OnRunnerStartedTravel(RunnerStartedTravel e)
        {
            var state = _getState();
            if (state == null || !state.Tutorial.IsActive) return;

            if (state.Tutorial.CurrentPhase == TutorialPhase.Gathering)
            {
                if (e.ToNodeId != state.Map.HubNodeId)
                {
                    if (CompleteMilestone(TutorialMilestones.Gathering_SentRunnerToNode))
                        _sentRunnerTick = state.TickCount;
                }
            }

            if (state.Tutorial.CurrentPhase == TutorialPhase.Combat)
            {
                if (e.ToNodeId == "goblin_camp")
                    CompleteMilestone(TutorialMilestones.Combat_SentToGoblins);
            }
        }

        private void OnRunnerDeposited(RunnerDeposited e)
        {
            var state = _getState();
            if (state == null || !state.Tutorial.IsActive) return;
            if (state.Tutorial.CurrentPhase != TutorialPhase.Gathering) return;

            int copperCount = state.Bank.CountItem("copper_ore");
            if (copperCount >= 20)
            {
                if (CompleteMilestone(TutorialMilestones.Gathering_CopperDeposited))
                    TransitionPhase(TutorialPhase.Gathering, TutorialPhase.Crafting);
            }
        }

        private void OnSimulationTickCompleted(SimulationTickCompleted e)
        {
            var state = _getState();
            if (state == null || !state.Tutorial.IsActive) return;

            if (state.Tutorial.CurrentPhase == TutorialPhase.Gathering)
                CheckIdleNudge(state, e.TickNumber);
        }

        private void OnCraftingCompleted(CraftingCompleted e)
        {
            var state = _getState();
            if (state == null || !state.Tutorial.IsActive) return;
            if (state.Tutorial.CurrentPhase != TutorialPhase.Crafting) return;

            CompleteMilestone(TutorialMilestones.Crafting_FirstItemCrafted);
        }

        private void OnItemEquipped(ItemEquipped e)
        {
            var state = _getState();
            if (state == null || !state.Tutorial.IsActive) return;
            if (state.Tutorial.CurrentPhase != TutorialPhase.Crafting) return;

            if (CompleteMilestone(TutorialMilestones.Crafting_ItemEquipped))
                TransitionPhase(TutorialPhase.Crafting, TutorialPhase.Combat);
        }

        private void OnEnemyDied(EnemyDied e)
        {
            var state = _getState();
            if (state == null || !state.Tutorial.IsActive) return;

            if (state.Tutorial.CurrentPhase == TutorialPhase.Combat)
                CompleteMilestone(TutorialMilestones.Combat_FirstKill);

            // Track kills after Culling Frost unlock for new pawn
            if (_cullingFrostUnlocked
                && !state.Tutorial.IsMilestoneCompleted(TutorialMilestones.NewPawnAwarded))
            {
                _killsSinceCullingFrost++;
                if (_killsSinceCullingFrost >= KillsForNewPawn)
                {
                    CompleteMilestone(TutorialMilestones.NewPawnAwarded);
                }
            }
        }

        private void OnSkillLeveledUp(RunnerSkillLeveledUp e)
        {
            var state = _getState();
            if (state == null || !state.Tutorial.IsActive) return;

            // Culling Frost unlock: Magic level 8 triggers Automation phase
            if (e.Skill == SkillType.Magic && e.NewLevel >= 8)
            {
                _cullingFrostUnlocked = true;
                _killsSinceCullingFrost = 0;

                if (state.Tutorial.CurrentPhase == TutorialPhase.Combat)
                    TransitionPhase(TutorialPhase.Combat, TutorialPhase.Automation);
            }
        }

        // ─── Internal ─────────────────────────────────────────────

        private void CheckIdleNudge(GameState state, long currentTick)
        {
            if (_sentRunnerTick < 0) return;
            if (state.Tutorial.IsMilestoneCompleted(TutorialMilestones.Gathering_IdleNudgeShown)) return;

            float secondsSinceSent = (currentTick - _sentRunnerTick) * _tickDeltaTime;
            if (secondsSinceSent < 15f) return;

            int idleAtHub = 0;
            string hubId = state.Map.HubNodeId;
            for (int i = 0; i < state.Runners.Count; i++)
            {
                var runner = state.Runners[i];
                if (runner.State == RunnerState.Idle && runner.CurrentNodeId == hubId)
                    idleAtHub++;
            }

            if (idleAtHub >= 2)
                CompleteMilestone(TutorialMilestones.Gathering_IdleNudgeShown);
        }

        private bool CompleteMilestone(string milestoneId)
        {
            var state = _getState();
            if (state == null || !state.Tutorial.IsActive) return false;

            if (!state.Tutorial.CompleteMilestone(milestoneId))
                return false;

            _events.Publish(new TutorialMilestoneCompleted
            {
                MilestoneId = milestoneId,
                Phase = state.Tutorial.CurrentPhase,
            });

            return true;
        }

        private void TransitionPhase(TutorialPhase from, TutorialPhase to)
        {
            var state = _getState();
            if (state == null) return;
            if (state.Tutorial.CurrentPhase != from) return;

            // Complete the "complete" milestone for the current phase
            string completeMilestone = from switch
            {
                TutorialPhase.Gathering => TutorialMilestones.Gathering_Complete,
                TutorialPhase.Crafting => TutorialMilestones.Crafting_Complete,
                TutorialPhase.Combat => TutorialMilestones.Combat_Complete,
                _ => null,
            };
            if (completeMilestone != null)
                CompleteMilestone(completeMilestone);

            state.Tutorial.CurrentPhase = to;
            OnPhaseTransition(from, to);

            _events.Publish(new TutorialPhaseCompleted
            {
                CompletedPhase = from,
                NextPhase = to,
            });

            // Publish intro milestone for the new phase
            string introMilestone = to switch
            {
                TutorialPhase.Crafting => TutorialMilestones.Crafting_Intro,
                TutorialPhase.Combat => TutorialMilestones.Combat_Intro,
                TutorialPhase.Automation => TutorialMilestones.Automation_Intro,
                _ => null,
            };
            if (introMilestone != null)
                CompleteMilestone(introMilestone);
        }

        private void OnPhaseTransition(TutorialPhase from, TutorialPhase to)
        {
            // All nodes visible from start. Nothing to unlock.
        }
    }
}
