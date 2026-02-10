using System;
using ProjectGuild.Simulation.Gathering;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Determines how skill level affects gathering speed.
    /// Each formula produces a speed multiplier from the runner's effective level.
    /// </summary>
    public enum GatheringSpeedFormula
    {
        /// <summary>
        /// Power curve: speedMultiplier = effectiveLevel ^ exponent.
        /// Higher levels are proportionally more impactful than lower levels.
        /// The grind from 90→99 is more rewarding per-level than 1→10.
        /// Controlled by GatheringSpeedExponent.
        ///   Exponent 0.5: level 1 = 1x, level 10 = 3.2x, level 50 = 7.1x, level 99 = 10x
        ///   Exponent 0.7: level 1 = 1x, level 10 = 5x,   level 50 = 18x,  level 99 = 30x
        ///   Exponent 1.0: level 1 = 1x, level 10 = 10x,  level 50 = 50x,  level 99 = 99x (linear)
        /// </summary>
        PowerCurve,

        /// <summary>
        /// Hyperbolic (diminishing returns): speedMultiplier = 1 + (effectiveLevel - 1) * perLevelFactor.
        /// Each level adds the same flat amount to the divisor, but the marginal speed gain shrinks.
        /// Early levels feel most impactful; high-level grinding yields diminishing improvements.
        /// Controlled by GatheringSkillSpeedPerLevel.
        ///   At 0.08: level 1 = 1x, level 10 = 1.7x, level 50 = 4.9x, level 99 = 8.8x
        /// </summary>
        Hyperbolic,
    }

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
        /// Applied regardless of which formula is selected.
        /// </summary>
        public float GlobalGatheringSpeedMultiplier = 1.0f;

        /// <summary>
        /// Which formula to use for skill-level-based gathering speed scaling.
        /// See <see cref="GatheringSpeedFormula"/> for detailed descriptions of each option.
        /// </summary>
        public GatheringSpeedFormula GatheringFormula = GatheringSpeedFormula.PowerCurve;

        /// <summary>
        /// Exponent for the PowerCurve formula. Controls how aggressively speed scales with level.
        /// speedMultiplier = effectiveLevel ^ GatheringSpeedExponent.
        /// 0.5 = gentle (level 99 is ~10x faster than level 1).
        /// 0.7 = moderate (level 99 is ~30x faster).
        /// 1.0 = linear (level 99 is 99x faster).
        /// Only used when GatheringFormula == PowerCurve.
        /// </summary>
        public float GatheringSpeedExponent = 0.55f;

        /// <summary>
        /// Per-level flat speed factor for the Hyperbolic formula (diminishing returns).
        /// speedMultiplier = 1 + (effectiveLevel - 1) * GatheringSkillSpeedPerLevel.
        /// At 0.08: level 10 = ~1.7x faster, level 99 = ~8.8x faster.
        /// Only used when GatheringFormula == Hyperbolic.
        /// </summary>
        public float GatheringSkillSpeedPerLevel = 0.08f;

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
