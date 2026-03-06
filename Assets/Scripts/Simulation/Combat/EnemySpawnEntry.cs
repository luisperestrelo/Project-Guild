using System;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// Defines which enemies spawn at a node and how they respawn.
    /// Parallel to GatherableConfig: nodes have an array of these.
    /// </summary>
    [Serializable]
    public class EnemySpawnEntry
    {
        /// <summary>
        /// Which enemy type to spawn (references EnemyConfig.Id).
        /// </summary>
        public string EnemyConfigId;

        /// <summary>
        /// Ticks until this enemy respawns after dying. At 10 ticks/sec: 100 = 10 seconds.
        /// </summary>
        public int RespawnTimeTicks = 100;

        /// <summary>
        /// Maximum number of this enemy type alive at once at this node.
        /// Performance guardrail. Default 30.
        /// </summary>
        public int MaxCount = 30;

        /// <summary>
        /// How many of this enemy to spawn when the encounter starts.
        /// </summary>
        public int InitialCount = 3;

        public EnemySpawnEntry() { }

        public EnemySpawnEntry(string enemyConfigId, int initialCount = 3,
            int respawnTimeTicks = 100, int maxCount = 30)
        {
            EnemyConfigId = enemyConfigId;
            InitialCount = initialCount;
            RespawnTimeTicks = respawnTimeTicks;
            MaxCount = maxCount;
        }
    }
}
