namespace ProjectGuild.Simulation.World
{
    /// <summary>
    /// Provides real path distances for travel calculations. The simulation layer
    /// calls this to get NavMesh-backed distances when available, falling back to
    /// Euclidean/FindPath math when the provider returns null.
    ///
    /// Lives in the simulation layer as a pure C# interface — the view-layer
    /// NavMeshTravelPathCache implements it using Unity's NavMesh API.
    /// </summary>
    public interface IPathDistanceProvider
    {
        /// <summary>
        /// Get the travel distance from one node to another (normal travel).
        /// Returns null if unavailable — caller falls back to FindPath.
        /// </summary>
        float? GetTravelDistance(string runnerId, string fromNodeId, string toNodeId);

        /// <summary>
        /// Get the travel distance from a world position to a node (mid-travel redirect).
        /// Returns null if unavailable — caller falls back to Euclidean distance.
        /// </summary>
        float? GetTravelDistance(string runnerId, float fromX, float fromZ, string toNodeId);
    }
}
