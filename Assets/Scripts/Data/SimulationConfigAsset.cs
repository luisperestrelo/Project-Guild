using System;
using UnityEngine;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Gathering;

namespace ProjectGuild.Data
{
    /// <summary>
    /// ScriptableObject wrapper around SimulationConfig. Create one of these in the Unity editor
    /// (right-click > Create > Project Guild > Simulation Config) to get an inspector-editable
    /// config asset. The Bridge layer reads from this asset and passes the plain C# config
    /// into the simulation.
    /// </summary>
    [CreateAssetMenu(fileName = "SimulationConfig", menuName = "Project Guild/Simulation Config")]
    public class SimulationConfigAsset : ScriptableObject
    {
        [Header("Travel")]
        [Tooltip("Base travel speed at Athletics level 1 (distance units per second)")]
        public float BaseTravelSpeed = 1.0f;

        [Tooltip("Additional travel speed per Athletics level beyond 1")]
        public float AthleticsSpeedPerLevel = 0.05f;

        [Tooltip("XP awarded every tick while traveling. Athletics leveling is decoupled from travel speed — " +
            "speed is about getting there faster, XP is about progression.")]
        public float AthleticsXpPerTick = 1.0f;

        [Header("Skills / XP")]
        [Tooltip("Multiplier on effective level when runner has passion (e.g. 1.05 = +5%)")]
        public float PassionEffectivenessMultiplier = 1.05f;

        [Tooltip("Multiplier on XP gain when runner has passion")]
        public float PassionXpMultiplier = 1.5f;

        [Tooltip("Base XP for the exponential XP curve. Each level costs: base * growth^level.\n" +
            "Higher values = more XP needed at all levels.\n\n" +
            "OSRS uses 75. Our default is 100 (1.33x slower than OSRS across the board).")]
        public float XpCurveBase = 100f;

        [Tooltip("Growth factor for the exponential XP curve. Each level costs growth^level more than the base.\n\n" +
            "OSRS uses 1.104 (= 2^(1/7), XP doubles every ~7 levels, '92 is half of 99').\n\n" +
            "1.05: XP doubles every ~14 levels, gentler curve.\n" +
            "1.104: OSRS default.\n" +
            "1.15: XP doubles every ~5 levels, steeper wall.")]
        public float XpCurveGrowth = 1.104f;

        [Header("Runner Generation")]
        [Tooltip("Minimum starting skill level for random runners")]
        public int MinStartingLevel = 1;

        [Tooltip("Maximum starting skill level for random runners")]
        public int MaxStartingLevel = 10;

        [Tooltip("Chance (0-1) for each skill to have passion on a new runner")]
        [Range(0f, 1f)]
        public float PassionChance = 0.2f;

        [Tooltip("Chance (0-1) for a new runner to get an easter egg name")]
        [Range(0f, 1f)]
        public float EasterEggNameChance = 0.02f;

        [Header("Gathering")]
        [Tooltip("Global multiplier on gathering speed. 1.0 = normal, 0.5 = twice as fast")]
        public float GlobalGatheringSpeedMultiplier = 1.0f;

        [Tooltip("Which formula to use for skill-level-based gathering speed scaling.\n\n" +
            "PowerCurve: speedMultiplier = level ^ exponent. Higher levels are proportionally more impactful. " +
            "The grind from 90->99 is more rewarding per-level than 1->10.\n\n" +
            "Hyperbolic (diminishing returns): speedMultiplier = 1 + (level - 1) * perLevelFactor. " +
            "Each level adds the same flat amount, but marginal speed gain shrinks. " +
            "Early levels feel most impactful; high-level grinding yields diminishing improvements.")]
        public GatheringSpeedFormula GatheringFormula = GatheringSpeedFormula.PowerCurve;

        [Tooltip("Only used when GatheringFormula == PowerCurve. Ignored otherwise.\n\n" +
            "speedMultiplier = effectiveLevel ^ this exponent.\n" +
            "0.5 = gentle: level 1 = 1x, level 10 = 3.2x, level 50 = 7.1x, level 99 = 10x\n" +
            "0.7 = moderate: level 1 = 1x, level 10 = 5x, level 50 = 18x, level 99 = 30x\n" +
            "1.0 = linear: level 1 = 1x, level 10 = 10x, level 50 = 50x, level 99 = 99x")]
        public float GatheringSpeedExponent = 0.55f;

        [Tooltip("Only used when GatheringFormula == Hyperbolic. Ignored otherwise.\n\n" +
            "speedMultiplier = 1 + (effectiveLevel - 1) * this value.\n" +
            "At 0.08: level 1 = 1x, level 10 = 1.7x, level 50 = 4.9x, level 99 = 8.8x")]
        public float HyperbolicSpeedPerLevel = 0.08f;

        [Header("Items")]
        [Tooltip("All item definitions in the game. Each item is its own ScriptableObject asset.")]
        public ItemDefinitionAsset[] ItemDefinitions = new ItemDefinitionAsset[0];

        [Header("Node Gatherables")]
        [Tooltip("Maps world nodes to their gatherables. Each entry pairs a node ID with an ordered list of gatherable configs.\n" +
            "The array order within each node determines the gatherable index (index 0 = default auto-gather target).")]
        public NodeGatherableSetup[] NodeGatherables = new NodeGatherableSetup[0];

        /// <summary>
        /// Associates a world node ID with an ordered list of GatherableConfigAssets.
        /// The Inspector shows this as an array of expandable entries.
        /// </summary>
        [Serializable]
        public struct NodeGatherableSetup
        {
            [Tooltip("The world node ID this gatherable setup applies to (e.g. 'copper_mine', 'deep_mine').")]
            public string NodeId;

            [Tooltip("Gatherables available at this node, in order. Index 0 is the default gather target.")]
            public GatherableConfigAsset[] Gatherables;
        }

        [Header("Inventory")]
        [Tooltip("Number of inventory slots per runner (OSRS-style: 28)")]
        public int InventorySize = 28;

        [Header("Death (Overworld Only)")]
        [Tooltip("Minimum respawn time in seconds, even if the runner dies right next to hub")]
        public float DeathRespawnBaseTime = 10f;

        [Tooltip("Multiplier on travel-time-to-hub for respawn duration. Must be > 1.0 so dying is always slower than walking back. 1.2 = 20% longer than the walk.")]
        public float DeathRespawnTravelMultiplier = 1.2f;

        /// <summary>
        /// Convert this ScriptableObject's values into a plain C# SimulationConfig
        /// that the simulation layer can use.
        /// </summary>
        public SimulationConfig ToConfig()
        {
            // Convert item SO array to plain C# array for the simulation
            var itemDefs = new Simulation.Items.ItemDefinition[ItemDefinitions.Length];
            for (int i = 0; i < ItemDefinitions.Length; i++)
                itemDefs[i] = ItemDefinitions[i].ToItemDefinition();

            // Convert node-gatherable SO mappings to plain C# structs
            var nodeGatherables = new SimulationConfig.NodeGatherable[NodeGatherables.Length];
            for (int i = 0; i < NodeGatherables.Length; i++)
            {
                var setup = NodeGatherables[i];
                var gatherables = new GatherableConfig[setup.Gatherables != null ? setup.Gatherables.Length : 0];
                for (int j = 0; j < gatherables.Length; j++)
                    gatherables[j] = setup.Gatherables[j].ToGatherableConfig();

                nodeGatherables[i] = new SimulationConfig.NodeGatherable(setup.NodeId, gatherables);
            }

            return new SimulationConfig
            {
                BaseTravelSpeed = BaseTravelSpeed,
                AthleticsSpeedPerLevel = AthleticsSpeedPerLevel,
                AthleticsXpPerTick = AthleticsXpPerTick,
                PassionEffectivenessMultiplier = PassionEffectivenessMultiplier,
                PassionXpMultiplier = PassionXpMultiplier,
                XpCurveBase = XpCurveBase,
                XpCurveGrowth = XpCurveGrowth,
                MinStartingLevel = MinStartingLevel,
                MaxStartingLevel = MaxStartingLevel,
                PassionChance = PassionChance,
                EasterEggNameChance = EasterEggNameChance,
                GlobalGatheringSpeedMultiplier = GlobalGatheringSpeedMultiplier,
                GatheringFormula = GatheringFormula,
                GatheringSpeedExponent = GatheringSpeedExponent,
                HyperbolicSpeedPerLevel = HyperbolicSpeedPerLevel,
                ItemDefinitions = itemDefs,
                NodeGatherables = nodeGatherables,
                InventorySize = InventorySize,
                DeathRespawnBaseTime = DeathRespawnBaseTime,
                DeathRespawnTravelMultiplier = DeathRespawnTravelMultiplier,
            };
        }
    }
}
