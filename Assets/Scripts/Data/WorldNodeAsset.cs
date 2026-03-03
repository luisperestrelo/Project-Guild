using UnityEngine;
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

        [Header("Approach")]
        [Tooltip("Radius of the node's overworld area (meters). Runners arrive/depart at the circumference.\n" +
            "Travel distance is edge-to-edge (subtracts both radii). 0 = point node (arrive at center).")]
        public float ApproachRadius = 5f;

        [Tooltip("If true, this node has a specific entrance point (cave mouth, gate, etc.).\n" +
            "Runners must approach and depart through the entrance position rather than the nearest circumference edge.")]
        public bool IsEntranceNode;

        [Tooltip("Local offset from WorldX/WorldZ to the entrance position.\n" +
            "Only used when IsEntranceNode is true.")]
        public Vector3 EntranceOffset;

        [Header("Visuals")]
        [Tooltip("Prefab to instantiate as this node's entrance marker in the overworld.\n" +
            "If null, a placeholder cylinder is created instead.\n" +
            "Assign an Asset Store model (cave entrance, wooden arch, tent, etc.) for each node.")]
        public GameObject EntranceMarkerPrefab;

        [Tooltip("PLACEHOLDER ONLY: color for the fallback cylinder when no EntranceMarkerPrefab is assigned.\n" +
            "Ignored entirely when a prefab is set. Has no effect on the final game visuals.")]
        public Color NodeColor = Color.gray;

        [Header("Gatherables")]
        [Tooltip("Gatherables available at this node, in order. Index 0 is the default auto-gather target.\n" +
            "Leave empty for non-gathering nodes (hub, raids, etc.).\n" +
            "A single GatherableConfigAsset can be reused across multiple nodes.")]
        public GatherableConfigAsset[] Gatherables = new GatherableConfigAsset[0];

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

            return new WorldNode
            {
                Id = Id,
                Name = Name,
                WorldX = WorldX,
                WorldZ = WorldZ,
                SceneName = SceneName,
                ApproachRadius = ApproachRadius,
                ColorR = NodeColor.r,
                ColorG = NodeColor.g,
                ColorB = NodeColor.b,
                Gatherables = gatherables,
            };
        }
    }
}
