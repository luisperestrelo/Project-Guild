using UnityEngine;
using ProjectGuild.Simulation.Combat;

namespace ProjectGuild.Data
{
    /// <summary>
    /// ScriptableObject for authoring enemy spawn entries in the Unity inspector.
    /// Assigned to WorldNodeAsset's EnemySpawns array.
    /// </summary>
    [CreateAssetMenu(fileName = "New Enemy Spawn", menuName = "Project Guild/Enemy Spawn Entry")]
    public class EnemySpawnEntryAsset : ScriptableObject
    {
        [Tooltip("Which enemy type to spawn. Drag an EnemyConfigAsset here.")]
        public EnemyConfigAsset EnemyConfig;

        [Tooltip("How many of this enemy to spawn when the encounter starts.")]
        public int InitialCount = 3;

        [Tooltip("Ticks until this enemy respawns after dying. At 10 ticks/sec: 100 = 10 seconds.")]
        public int RespawnTimeTicks = 100;

        [Tooltip("Maximum number of this enemy type alive at once (performance guardrail).")]
        public int MaxCount = 30;

        public EnemySpawnEntry ToEnemySpawnEntry()
        {
            return new EnemySpawnEntry
            {
                EnemyConfigId = EnemyConfig != null ? EnemyConfig.Id : "",
                InitialCount = InitialCount,
                RespawnTimeTicks = RespawnTimeTicks,
                MaxCount = MaxCount,
            };
        }
    }
}
