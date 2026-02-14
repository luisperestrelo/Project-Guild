using System;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// An action to execute when a rule's conditions match.
    /// Data-driven: ActionType enum determines which parameters are used.
    /// </summary>
    [Serializable]
    public class AutomationAction
    {
        public ActionType Type;
        public string StringParam;   // nodeId for WorkAt/TravelTo
        public int IntParam;         // gatherableIndex for GatherHere

        public AutomationAction() { }

        // ─── Factory methods ───

        public static AutomationAction Idle()
            => new AutomationAction { Type = ActionType.Idle };

        public static AutomationAction TravelTo(string nodeId)
            => new AutomationAction { Type = ActionType.TravelTo, StringParam = nodeId };

        /// <summary>
        /// Macro action: create a work loop at the target node.
        /// What the runner does there is determined by micro rules.
        /// </summary>
        public static AutomationAction WorkAt(string nodeId)
            => new AutomationAction { Type = ActionType.WorkAt, StringParam = nodeId };

        /// <summary>
        /// Micro action: gather the resource at the given index at the current node.
        /// </summary>
        public static AutomationAction GatherHere(int gatherableIndex = 0)
            => new AutomationAction { Type = ActionType.GatherHere, IntParam = gatherableIndex };

        public static AutomationAction ReturnToHub()
            => new AutomationAction { Type = ActionType.ReturnToHub };

        public static AutomationAction DepositAndResume()
            => new AutomationAction { Type = ActionType.DepositAndResume };

        public static AutomationAction FleeToHub()
            => new AutomationAction { Type = ActionType.FleeToHub };

        public static AutomationAction FinishTask()
            => new AutomationAction { Type = ActionType.FinishTask };
    }
}
