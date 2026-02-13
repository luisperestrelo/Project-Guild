using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Translates automation actions into GameSimulation commands.
    /// Stateless — all methods are static, all side effects go through GameSimulation.
    /// </summary>
    public static class ActionExecutor
    {
        /// <summary>
        /// Execute an automation action on a runner. May change the runner's state,
        /// start travel, begin gathering, or set a PendingAction for later.
        /// </summary>
        public static void Execute(AutomationAction action, Runner runner, GameSimulation sim)
        {
            switch (action.Type)
            {
                case ActionType.Idle:
                    CancelActivity(runner);
                    break;

                case ActionType.TravelTo:
                    CancelActivity(runner);
                    sim.StartTravelForAutomation(runner, action.StringParam);
                    break;

                case ActionType.GatherAt:
                    CancelActivity(runner);
                    if (runner.CurrentNodeId == action.StringParam)
                    {
                        // Already at target node — start gathering immediately
                        sim.CommandGather(runner.Id, action.IntParam);
                    }
                    else
                    {
                        // Travel to target first, then gather on arrival
                        runner.PendingAction = AutomationAction.GatherAt(action.StringParam, action.IntParam);
                        sim.StartTravelForAutomation(runner, action.StringParam);
                    }
                    break;

                case ActionType.DepositAndResume:
                    if (runner.Gathering != null)
                    {
                        sim.BeginAutoReturnForAutomation(runner);
                    }
                    break;

                case ActionType.ReturnToHub:
                    CancelActivity(runner);
                    string hubId = sim.CurrentGameState.Map.HubNodeId;
                    if (hubId != null && runner.CurrentNodeId != hubId)
                        sim.StartTravelForAutomation(runner, hubId);
                    break;

                case ActionType.FleeToHub:
                    CancelActivity(runner);
                    string fleeHubId = sim.CurrentGameState.Map.HubNodeId;
                    if (fleeHubId != null && runner.CurrentNodeId != fleeHubId)
                        sim.StartTravelForAutomation(runner, fleeHubId);
                    break;
            }
        }

        /// <summary>
        /// Cancel all current activity — gathering, travel, pending actions.
        /// Runner becomes Idle at their current node.
        /// </summary>
        private static void CancelActivity(Runner runner)
        {
            runner.Gathering = null;
            runner.Travel = null;
            runner.State = RunnerState.Idle;
            runner.PendingAction = null;
        }
    }
}
