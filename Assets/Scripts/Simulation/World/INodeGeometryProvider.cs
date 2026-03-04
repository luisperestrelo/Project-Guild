namespace ProjectGuild.Simulation.World
{
    /// <summary>
    /// Provides geometry information about node interiors to the simulation.
    /// The sim uses this to add an "exiting node" phase to travel — the runner
    /// walks to the edge of the current node before overworld travel begins.
    ///
    /// Same pattern as <see cref="IPathDistanceProvider"/>: pure C# interface
    /// in the sim layer, implemented by a view-layer MonoBehaviour using scene data.
    /// Returns null when data is unavailable — caller treats as 0 (instant exit).
    /// </summary>
    public interface INodeGeometryProvider
    {
        /// <summary>
        /// Distance from a runner's current position to the exit point for a destination.
        /// Returns null if unavailable (scene not loaded, runner not found) — caller uses 0 (instant exit).
        /// </summary>
        float? GetExitDistance(string runnerId, string nodeId, string destinationNodeId);

        /// <summary>
        /// Distance from a runner's current position to the gathering spot for a given gatherable index.
        /// Returns null if unavailable (scene not loaded, runner not found) — caller uses 0 (instant).
        /// Returns 0 if distance is trivially short (&lt; 0.5m).
        /// </summary>
        float? GetGatheringSpotDistance(string runnerId, string nodeId, int gatherableIndex);

        /// <summary>
        /// Distance from a runner's current position to the deposit point at the node.
        /// Returns null if unavailable (scene not loaded, no deposit point, runner not found) — caller uses 0 (instant).
        /// Returns 0 if distance is trivially short (&lt; 0.5m).
        /// </summary>
        float? GetDepositPointDistance(string runnerId, string nodeId);

        // Future: GetEntryDistance, GetPointToPointDistance, GetNodeRadius
    }
}
