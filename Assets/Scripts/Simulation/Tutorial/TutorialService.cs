using System;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Tutorial
{
    /// <summary>
    /// Monitors simulation events and advances tutorial milestones.
    /// Follows the same pattern as ChronicleService: Func&lt;GameState&gt; for lazy access,
    /// SubscribeAll/UnsubscribeAll for event wiring.
    ///
    /// Pure C# — no Unity dependency. The view layer's TutorialController subscribes
    /// to the milestone/phase events this service publishes and shows appropriate UI.
    /// </summary>
    public class TutorialService
    {
        private readonly Func<GameState> _getState;
        private readonly EventBus _events;
        private readonly float _tickDeltaTime;

        /// <summary>
        /// Tick when Gathering_SentRunnerToNode was completed. Used to time the idle nudge.
        /// -1 means not yet sent.
        /// </summary>
        private long _sentRunnerTick = -1;

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
        }

        public void UnsubscribeAll(EventBus events)
        {
            events.Unsubscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
            events.Unsubscribe<RunnerDeposited>(OnRunnerDeposited);
            events.Unsubscribe<SimulationTickCompleted>(OnSimulationTickCompleted);
        }

        // ─── Initialization ───────────────────────────────────────

        /// <summary>
        /// Set up the initial discovered node list for a new game.
        /// Hub + the four nearby gathering nodes are visible from the start.
        /// </summary>
        public void InitializeDiscoveredNodes()
        {
            var state = _getState();
            if (state == null) return;

            var tutorial = state.Tutorial;
            if (!tutorial.IsActive) return;

            tutorial.DiscoveredNodeIds.Clear();
            tutorial.DiscoveredNodeIds.Add(state.Map.HubNodeId);
            tutorial.DiscoveredNodeIds.Add("copper_mine");
            tutorial.DiscoveredNodeIds.Add("pine_forest");
            tutorial.DiscoveredNodeIds.Add("sunlit_pond");
            tutorial.DiscoveredNodeIds.Add("herb_garden");
        }

        /// <summary>
        /// Complete the intro milestone at tick 0 of a new game.
        /// Called by GameSimulation.StartNewGame after state is set up.
        /// </summary>
        public void CompleteIntroMilestone()
        {
            CompleteMilestone(TutorialMilestones.Gathering_Intro);
        }

        /// <summary>
        /// Manually complete a milestone (for dev commands / testing).
        /// </summary>
        public void ForceCompleteMilestone(string milestoneId)
        {
            CompleteMilestone(milestoneId);
        }

        /// <summary>
        /// Skip the tutorial entirely. Clears discovered nodes (shows all)
        /// and marks tutorial as inactive.
        /// </summary>
        public void SkipTutorial()
        {
            var state = _getState();
            if (state == null) return;

            state.Tutorial.IsActive = false;
            state.Tutorial.DiscoveredNodeIds.Clear();
        }

        /// <summary>
        /// Reset tutorial to the beginning of the Gathering phase.
        /// </summary>
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

        /// <summary>
        /// Advance to the next phase. For dev commands.
        /// </summary>
        public void AdvanceToNextPhase()
        {
            var state = _getState();
            if (state == null) return;

            var tutorial = state.Tutorial;
            if (tutorial.CurrentPhase >= TutorialPhase.Complete) return;

            var completed = tutorial.CurrentPhase;
            tutorial.CurrentPhase = completed + 1;

            _events.Publish(new TutorialPhaseCompleted
            {
                CompletedPhase = completed,
                NextPhase = tutorial.CurrentPhase,
            });
        }

        /// <summary>
        /// Add a node to the discovered list (for dev commands).
        /// </summary>
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
            if (state == null) return;
            if (!state.Tutorial.IsActive) return;
            if (state.Tutorial.CurrentPhase != TutorialPhase.Gathering) return;

            // Complete "sent runner to node" if target is not hub
            if (e.ToNodeId != state.Map.HubNodeId)
            {
                if (CompleteMilestone(TutorialMilestones.Gathering_SentRunnerToNode))
                    _sentRunnerTick = state.TickCount;
            }
        }

        private void OnRunnerDeposited(RunnerDeposited e)
        {
            var state = _getState();
            if (state == null) return;
            if (!state.Tutorial.IsActive) return;
            if (state.Tutorial.CurrentPhase != TutorialPhase.Gathering) return;

            // Check if bank has >= 20 copper ore
            int copperCount = state.Bank.CountItem("copper_ore");
            if (copperCount >= 20)
            {
                if (CompleteMilestone(TutorialMilestones.Gathering_CopperDeposited))
                    CheckPhaseComplete();
            }
        }

        private void OnSimulationTickCompleted(SimulationTickCompleted e)
        {
            var state = _getState();
            if (state == null) return;
            if (!state.Tutorial.IsActive) return;
            if (state.Tutorial.CurrentPhase != TutorialPhase.Gathering) return;

            CheckIdleNudge(state, e.TickNumber);
        }

        // ─── Internal ─────────────────────────────────────────────

        private void CheckIdleNudge(GameState state, long currentTick)
        {
            // Only after a runner has been sent and 15+ seconds have passed
            if (_sentRunnerTick < 0) return;
            if (state.Tutorial.IsMilestoneCompleted(TutorialMilestones.Gathering_IdleNudgeShown)) return;

            float secondsSinceSent = (currentTick - _sentRunnerTick) * _tickDeltaTime;
            if (secondsSinceSent < 15f) return;

            // Count runners idle at hub
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

        /// <summary>
        /// Complete a milestone if the tutorial is active. Publishes TutorialMilestoneCompleted.
        /// Returns true if newly completed.
        /// </summary>
        private bool CompleteMilestone(string milestoneId)
        {
            var state = _getState();
            if (state == null) return false;
            if (!state.Tutorial.IsActive) return false;

            if (!state.Tutorial.CompleteMilestone(milestoneId))
                return false;

            _events.Publish(new TutorialMilestoneCompleted
            {
                MilestoneId = milestoneId,
                Phase = state.Tutorial.CurrentPhase,
            });

            return true;
        }

        private void CheckPhaseComplete()
        {
            var state = _getState();
            if (state == null) return;

            if (state.Tutorial.CurrentPhase == TutorialPhase.Gathering
                && state.Tutorial.IsMilestoneCompleted(TutorialMilestones.Gathering_CopperDeposited))
            {
                CompleteMilestone(TutorialMilestones.Gathering_Complete);

                var completed = state.Tutorial.CurrentPhase;
                state.Tutorial.CurrentPhase = TutorialPhase.Crafting;

                _events.Publish(new TutorialPhaseCompleted
                {
                    CompletedPhase = completed,
                    NextPhase = TutorialPhase.Crafting,
                });
            }
        }
    }
}
