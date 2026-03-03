using UnityEngine;

namespace ProjectGuild.View
{
    /// <summary>
    /// Static helper for sampling terrain height at any world XZ position.
    /// Used by VisualSyncSystem to place runners and markers on the terrain surface.
    /// Returns 0 if no active terrain exists (tests, scenes without terrain).
    /// </summary>
    public static class TerrainHeightSampler
    {
        /// <summary>
        /// Get the terrain height at the given world XZ position.
        /// Returns 0 if no active terrain exists.
        /// </summary>
        public static float GetHeight(float worldX, float worldZ)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return 0f;
            return terrain.SampleHeight(new Vector3(worldX, 0f, worldZ));
        }

        /// <summary>
        /// Get a full Vector3 position sitting on the terrain surface at the given XZ,
        /// with an optional Y offset (e.g. half-height of a character model).
        /// Returns (worldX, yOffset, worldZ) if no active terrain exists.
        /// </summary>
        public static Vector3 GetPositionOnTerrain(float worldX, float worldZ, float yOffset = 0f)
        {
            float terrainY = GetHeight(worldX, worldZ);
            return new Vector3(worldX, terrainY + yOffset, worldZ);
        }
    }
}
