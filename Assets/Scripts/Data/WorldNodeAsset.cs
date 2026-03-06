using UnityEngine;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Gathering;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Data
{
    /// <summary>
    /// ScriptableObject for authoring a world node in the Unity inspector.
    /// Each location in the game (a mine, a forest, the hub, etc.) gets its own asset.
    /// Gatherables are assigned directly via SO references — no string-ID matching needed.
    ///
    /// The simulation layer receives a plain C# WorldNode at runtime via ToWorldNode().
    /// </summary>
    [CreateAssetMenu(fileName = "New World Node", menuName = "Project Guild/World Node")]
    public class WorldNodeAsset : ScriptableObject
    {
        [Tooltip("Unique identifier for this node (e.g. 'copper_mine'). Must be unique across all nodes in a map.")]
        public string Id;

        [Tooltip("Display name shown in-game (e.g. 'Copper Mine').")]
        public string Name;

        [Header("Position")]
        [Tooltip("World-space X position for visual placement on the map.")]
        public float WorldX;

        [Tooltip("World-space Z position for visual placement on the map.")]
        public float WorldZ;

        [Header("Scene")]
        [Tooltip("Unity scene name for this node's additive scene (e.g. 'Node_CopperMine'). " +
            "Leave empty for nodes without a dedicated scene — they'll use a default placeholder.")]
        public string SceneName;

        [Header("Approach Mode (choose ONE)")]
        [Tooltip("If true, runners approach via EntranceOffset instead of the nearest circumference edge.\n" +
            "ApproachRadius is ignored for NavMesh pathing but kept as Euclidean fallback.")]
        public bool IsEntranceNode;

        [Tooltip("Local offset from (WorldX, 0, WorldZ) to the entrance position.\n" +
            "Only used when IsEntranceNode is true. Edit visually via the Scene View handle.")]
        public Vector3 EntranceOffset;

        [Tooltip("Radius of the node's overworld area (meters). Runners arrive/depart at the circumference.\n" +
            "Travel distance is edge-to-edge (subtracts both radii). 0 = point node (arrive at center).\n" +
            "Ignored for NavMesh pathing when IsEntranceNode is true.")]
        public float ApproachRadius = 5f;

        [Header("Gatherables")]
        [Tooltip("Gatherables available at this node, in order. Index 0 is the default auto-gather target.\n" +
            "Leave empty for non-gathering nodes (hub, raids, etc.).\n" +
            "A single GatherableConfigAsset can be reused across multiple nodes.")]
        public GatherableConfigAsset[] Gatherables = new GatherableConfigAsset[0];

        [Header("Enemies")]
        [Tooltip("Enemy spawn configuration for combat nodes.\n" +
            "Leave empty for non-combat nodes (gathering, hub).")]
        public EnemySpawnEntryAsset[] EnemySpawns = new EnemySpawnEntryAsset[0];

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                Debug.LogWarning($"[WorldNodeAsset] '{name}' has an empty Id. Every node needs a unique Id.", this);

            if (string.IsNullOrWhiteSpace(Name))
                Debug.LogWarning($"[WorldNodeAsset] '{name}' has an empty Name.", this);
        }

        /// <summary>
        /// Convert to the plain C# WorldNode used by the simulation.
        /// Gatherables are converted inline — the resulting node is fully populated.
        /// </summary>
        public WorldNode ToWorldNode()
        {
            var gatherables = new GatherableConfig[Gatherables != null ? Gatherables.Length : 0];
            for (int i = 0; i < gatherables.Length; i++)
            {
                gatherables[i] = Gatherables[i] != null
                    ? Gatherables[i].ToGatherableConfig()
                    : new GatherableConfig();
            }

            var enemySpawns = new EnemySpawnEntry[EnemySpawns != null ? EnemySpawns.Length : 0];
            for (int i = 0; i < enemySpawns.Length; i++)
            {
                enemySpawns[i] = EnemySpawns[i] != null
                    ? EnemySpawns[i].ToEnemySpawnEntry()
                    : new EnemySpawnEntry();
            }

            return new WorldNode
            {
                Id = Id,
                Name = Name,
                WorldX = WorldX,
                WorldZ = WorldZ,
                SceneName = SceneName,
                ApproachRadius = ApproachRadius,
                Gatherables = gatherables,
                EnemySpawns = enemySpawns,
            };
        }
    }
}
