using System;
using ProjectGuild.Simulation.Gathering;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// All tunable simulation parameters live here. No magic numbers in game logic —
    /// everything references this config. The Bridge/Data layer populates this from
    /// a ScriptableObject so values are tweakable in the Unity inspector.
    ///
    /// For tests, just new up a SimulationConfig() to get sensible defaults.
    /// </summary>
    [Serializable]
    public class SimulationConfig
    {
        // ─── Travel ──────────────────────────────────────────────────

        /// <summary>
        /// Base travel speed at Athletics level 1 (distance units per second).
        /// </summary>
        public float BaseTravelSpeed = 1.0f;

        /// <summary>
        /// Additional travel speed per Athletics level beyond 1.
        /// At level 10: speed = BaseTravelSpeed + (10 - 1) * AthleticsSpeedPerLevel
        /// </summary>
        public float AthleticsSpeedPerLevel = 0.05f;

        // ─── Skills / XP ────────────────────────────────────────────

        /// <summary>
        /// Multiplier applied to a skill's effective level when the runner has passion.
        /// e.g. 1.05 means a level 10 skill with passion acts as 10.5.
        /// </summary>
        public float PassionEffectivenessMultiplier = 1.05f;

        /// <summary>
        /// Multiplier applied to XP gain when the runner has passion for the skill.
        /// </summary>
        public float PassionXpMultiplier = 1.5f;

        /// <summary>
        /// Base multiplier for the XP curve. Each level costs: level^XpCurveExponent * XpCurveBase.
        /// </summary>
        public float XpCurveBase = 50f;

        /// <summary>
        /// Exponent for the XP curve. Higher = steeper curve at high levels.
        /// OSRS-like feel ( "92 is half of 99") is around 2.0. Current default 1.5 is gentler.
        /// </summary>
        public float XpCurveExponent = 1.5f;

        // ─── Runner Generation ───────────────────────────────────────

        /// <summary>
        /// Minimum starting skill level for randomly generated runners. 
        /// </summary>
        public int MinStartingLevel = 1;

        /// <summary>
        /// Maximum starting skill level for randomly generated runners. 
        /// </summary>
        public int MaxStartingLevel = 10;

        /// <summary>
        /// Chance (0-1) for each individual skill to have passion on a new runner. Average of 3 passions per pawn at 0.2 (maybe lower a bit?)
        /// </summary>
        public float PassionChance = 0.2f;

        /// <summary>
        /// Chance (0-1) for a newly generated runner to get an easter egg full name
        /// instead of a random first+last name.
        /// </summary>
        public float EasterEggNameChance = 0.02f;

        // ─── Gathering ──────────────────────────────────────────────

        /// <summary>
        /// Global multiplier on all gathering speed. Affects how many ticks it takes
        /// to gather anything. 1.0 = normal, 0.5 = twice as fast, 2.0 = twice as slow.
        /// Formula: ticksRequired = (GlobalGatheringSpeedMultiplier * gatherable.BaseTicksToGather)
        ///                          / (1 + (effectiveLevel - 1) * GatheringSkillSpeedPerLevel)
        /// </summary>
        public float GlobalGatheringSpeedMultiplier = 1.0f;

        /// <summary>
        /// Per-level speed scaling for gathering. Each skill level beyond 1 reduces gather time.
        /// At level 10: divisor = 1 + 9 * 0.03 = 1.27, so ~21% faster. Hmm, At 99, 2.97, so a maxed out pawn is only twice as fast as a level 10 pawn at 0.03. TODO: tweak this value,
        ///                                                                                                                                                     then remove this comment
        /// </summary>
        public float GatheringSkillSpeedPerLevel = 0.03f;

        /// <summary>
        /// Gatherable definitions — what each gathering node type produces.
        /// </summary>
        public GatherableConfig[] GatherableConfigs = new[]
        {
            new GatherableConfig(NodeType.GatheringMine,   "copper_ore", SkillType.Mining,      10f, 25f),
            new GatherableConfig(NodeType.GatheringForest, "pine_log",   SkillType.Woodcutting,  10f, 20f),
            new GatherableConfig(NodeType.GatheringWater,  "raw_trout",  SkillType.Fishing,      12f, 22f),
            new GatherableConfig(NodeType.GatheringHerbs,  "sage_leaf",  SkillType.Foraging,      8f, 18f),
        };

        /// <summary>
        /// All item definitions in the game. Registered into ItemRegistry at game start.
        /// </summary>
        public ItemDefinition[] ItemDefinitions = new[]
        {
            new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
            new ItemDefinition("pine_log",   "Pine Log",   ItemCategory.Log),
            new ItemDefinition("raw_trout",  "Raw Trout",  ItemCategory.Fish),
            new ItemDefinition("sage_leaf",  "Sage Leaf",  ItemCategory.Herb),
        };

        /// <summary>
        /// Find the gatherable config for a given node type. Returns null if not a gathering node.
        /// </summary>
        public GatherableConfig GetGatherableConfig(NodeType nodeType)
        {
            for (int i = 0; i < GatherableConfigs.Length; i++)
            {
                if (GatherableConfigs[i].NodeType == nodeType)
                    return GatherableConfigs[i];
            }
            return null;
        }

        // ─── Inventory ───────────────────────────────────────────────

        /// <summary>
        /// Number of inventory slots per runner. OSRS-style: 28.
        /// </summary>
        public int InventorySize = 28;

        // ─── Death (Overworld Only — Raid deaths use separate logic) ──

        /// <summary>
        /// Minimum respawn time in seconds, even if the runner dies right next to hub.
        /// </summary>
        public float DeathRespawnBaseTime = 10f;

        /// <summary>
        /// Multiplier applied to the travel-time-to-hub to calculate respawn duration.
        /// Respawn time = DeathRespawnBaseTime + (travelTimeToHub * DeathRespawnTravelMultiplier).
        /// Must be > 1.0 so that dying is always slower than walking back - prevents "suicide on purpose" weirdness to cut on travel times
        /// A value of 1.2 means respawn takes 20% longer than the walk would have.
        /// </summary>
        public float DeathRespawnTravelMultiplier = 1.2f;
    }
}
