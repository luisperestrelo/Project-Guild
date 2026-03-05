using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Tutorial
{
    /// <summary>
    /// Serializable tutorial progress data that lives on GameState.
    /// Tracks which phase the player is in, which milestones have been completed,
    /// and which world map nodes the player has "discovered" (for progressive reveal).
    /// </summary>
    [Serializable]
    public class TutorialState
    {
        /// <summary>
        /// Whether the tutorial system is active. When false, all milestones are ignored
        /// and all nodes are visible on the map.
        /// </summary>
        public bool IsActive = true;

        /// <summary>
        /// The current tutorial phase the player is working through.
        /// </summary>
        public TutorialPhase CurrentPhase = TutorialPhase.Gathering;

        /// <summary>
        /// Milestone IDs that have been completed. Checked for idempotency —
        /// completing an already-completed milestone is a no-op.
        /// </summary>
        public List<string> CompletedMilestones = new();

        /// <summary>
        /// Node IDs visible on the strategic map. Empty list means "show all nodes"
        /// (used when tutorial is off or skipped). Populated by TutorialService
        /// at game start with hub + nearby gathering nodes.
        /// </summary>
        public List<string> DiscoveredNodeIds = new();

        /// <summary>
        /// Complete a milestone. Idempotent — does nothing if already completed.
        /// Returns true if the milestone was newly completed, false if already done.
        /// </summary>
        public bool CompleteMilestone(string milestoneId)
        {
            if (CompletedMilestones.Contains(milestoneId))
                return false;

            CompletedMilestones.Add(milestoneId);
            return true;
        }

        /// <summary>
        /// Check if a milestone has been completed.
        /// </summary>
        public bool IsMilestoneCompleted(string milestoneId)
        {
            return CompletedMilestones.Contains(milestoneId);
        }
    }
}
