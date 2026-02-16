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
        public string StringParam;   // taskSequenceId for AssignSequence, itemId for GatherHere
        public int IntParam;         // gatherableIndex for GatherHere (-1 = any available, random)

        public AutomationAction() { }

        // ─── Factory methods ───

        public static AutomationAction Idle()
            => new AutomationAction { Type = ActionType.Idle };

        // ─── Macro actions (select task sequence) ───

        /// <summary>
        /// Macro action: assign a library task sequence by ID.
        /// The runner switches to the referenced sequence (or null if not found = let it break).
        /// </summary>
        public static AutomationAction AssignSequence(string taskSequenceId)
            => new AutomationAction { Type = ActionType.AssignSequence, StringParam = taskSequenceId };

        // ─── Micro actions (within-task behavior) ───

        /// <summary>
        /// Micro action: gather the resource at the given index at the current node.
        /// Use IntParam = -1 for "any available" (random selection, see GatherAny()).
        /// </summary>
        public static AutomationAction GatherHere(int gatherableIndex = 0)
            => new AutomationAction { Type = ActionType.GatherHere, IntParam = gatherableIndex };

        /// <summary>
        /// Micro action: gather any available resource at the current node (random per item).
        /// Equivalent to GatherHere(-1). The runner picks a random gatherable each time
        /// a new item is started, producing a mix of resources over a full inventory.
        /// </summary>
        public static AutomationAction GatherAny()
            => new AutomationAction { Type = ActionType.GatherHere, IntParam = -1 };

        public static AutomationAction FinishTask()
            => new AutomationAction { Type = ActionType.FinishTask };
    }
}
