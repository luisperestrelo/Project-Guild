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

        [Header("Visuals")]
        [Tooltip("Color used for the node's visual representation (placeholder — will be replaced by proper art).")]
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
                ColorR = NodeColor.r,
                ColorG = NodeColor.g,
                ColorB = NodeColor.b,
                Gatherables = gatherables,
            };
        }
    }
}
