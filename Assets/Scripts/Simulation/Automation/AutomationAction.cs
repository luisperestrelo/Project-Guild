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
        public string StringParam;   // nodeId for TravelTo/GatherAt
        public int IntParam;         // gatherableIndex for GatherAt

        public AutomationAction() { }

        // ─── Factory methods ───

        public static AutomationAction Idle()
            => new AutomationAction { Type = ActionType.Idle };

        public static AutomationAction TravelTo(string nodeId)
            => new AutomationAction { Type = ActionType.TravelTo, StringParam = nodeId };

        public static AutomationAction GatherAt(string nodeId, int gatherableIndex = 0)
            => new AutomationAction { Type = ActionType.GatherAt, StringParam = nodeId, IntParam = gatherableIndex };

        public static AutomationAction ReturnToHub()
            => new AutomationAction { Type = ActionType.ReturnToHub };

        public static AutomationAction DepositAndResume()
            => new AutomationAction { Type = ActionType.DepositAndResume };

        public static AutomationAction FleeToHub()
            => new AutomationAction { Type = ActionType.FleeToHub };
    }
}
