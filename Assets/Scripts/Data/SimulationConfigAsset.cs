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
        [Tooltip("Base travel speed at Athletics level 1 (meters per second)")]
        public float BaseTravelSpeed = 1.0f;

        [Tooltip("Additional travel speed per Athletics level beyond 1")]
        public float AthleticsSpeedPerLevel = 0.05f;

        [Tooltip("XP awarded every tick while traveling. Athletics leveling is decoupled from travel speed — " +
            "speed is about getting there faster, XP is about progression.")]
        public float AthleticsXpPerTick = 1.0f;

        [Tooltip("Multiplier on overworld travel speed for in-node movement. " +
            "In-node speed = travel speed * this value. Used during the 'exiting node' phase " +
            "of travel and for all in-node movement visuals (gathering spot walks, deposit walks).")]
        public float InNodeSpeedMultiplier = 4.0f;

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

        [Header("Inventory")]
        [Tooltip("Number of inventory slots per runner (OSRS-style: 28)")]
        public int InventorySize = 28;

        [Header("Deposit")]
        [Tooltip("How many ticks the deposit step takes. At 10 ticks/sec, 10 = 1 second.")]
        public int DepositDurationTicks = 10;

        [Header("Automation")]
        [Tooltip("Safety-net interval (ticks) for re-evaluating automation rules. Catches external state changes like BankContains. 10 = once per second.")]
        public int AutomationPeriodicCheckInterval = 10;

        [Tooltip("Maximum entries in the macro decision log ring buffer. Macro decisions are rare/high-value.")]
        public int MacroDecisionLogMaxEntries = 2000;

        [Tooltip("Maximum entries in the micro decision log ring buffer. Micro decisions fire every tick per gathering runner.")]
        public int MicroDecisionLogMaxEntries = 1000;

        [Tooltip("Maximum entries in the Chronicle (player-facing event log) ring buffer.")]
        public int ChronicleMaxEntries = 1000;

        [Header("Dev Tools")]
        [Tooltip("Maximum entries in the event log ring buffer. The event log is for debugging, not player-facing.")]
        public int EventLogMaxEntries = 500;

        [Header("Death (Overworld Only)")]
        [Tooltip("Minimum respawn time in seconds, even if the runner dies right next to hub")]
        public float DeathRespawnBaseTime = 10f;

        [Tooltip("Multiplier on travel-time-to-hub for respawn duration. Must be > 1.0 so dying is always slower than walking back. 1.2 = 20% longer than the walk.")]
        public float DeathRespawnTravelMultiplier = 1.2f;

        [Header("Combat")]
        [Tooltip("Base hitpoints at Hitpoints level 1. MaxHP = BaseHitpoints + (level - 1) * HitpointsPerLevel.")]
        public float BaseHitpoints = 50f;

        [Tooltip("Additional hitpoints per Hitpoints skill level beyond 1.")]
        public float HitpointsPerLevel = 5f;

        [Tooltip("Base mana pool at Restoration level 1.")]
        public float BaseMana = 50f;

        [Tooltip("Additional mana per Restoration skill level beyond 1.")]
        public float ManaPerRestorationLevel = 3f;

        [Tooltip("Flat mana regenerated every tick. At 10 ticks/sec, 0.5 = 5 mana/sec.")]
        public float BaseManaRegenPerTick = 0.5f;

        [Tooltip("Base disengage time in ticks before Athletics reduction. Enemies still attack during disengage.")]
        public int BaseDisengageTimeTicks = 20;

        [Tooltip("Floor disengage time in ticks. Even max Athletics takes at least this long (guarantees 1+ hit).")]
        public int MinDisengageTimeTicks = 3;

        [Tooltip("Ticks of disengage reduction per Athletics level. Higher Athletics = faster flee.")]
        public float DisengageReductionPerAthleticsLevel = 0.3f;

        [Tooltip("XP per tick of action time on ability completion. 10-tick ability = 10 * this value.")]
        public float CombatXpPerActionTimeTick = 1.0f;

        [Tooltip("Percentage damage reduction per Defence level. Lv50 = 25%, Lv99 = 49.5%.")]
        public float DefenceReductionPerLevel = 0.5f;

        [Tooltip("Maximum percentage of incoming damage that defence can reduce (cap). Prevents invulnerability.")]
        public float MaxDefenceReductionPercent = 75f;

        [Tooltip("Per-level scaling factor for combat damage/heal. Damage = base * scaling * (1 + level * this).")]
        public float CombatDamageScalingPerLevel = 0.1f;

        [Tooltip("Hitpoints XP per point of pre-mitigation damage taken. Harder enemies award more XP.")]
        public float HitpointsXpPerDamage = 0.5f;

        [Tooltip("Defence XP per point of pre-mitigation damage taken. Harder enemies award more XP.")]
        public float DefenceXpPerDamage = 0.5f;

        [Header("Combat Definitions")]
        [Tooltip("All ability definitions in the game. Each ability is its own ScriptableObject asset.")]
        public AbilityConfigAsset[] AbilityDefinitions = new AbilityConfigAsset[0];

        [Tooltip("All enemy definitions in the game. Each enemy is its own ScriptableObject asset.")]
        public EnemyConfigAsset[] EnemyDefinitions = new EnemyConfigAsset[0];

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

            // Convert ability SO array to plain C# array
            var abilityDefs = new Simulation.Combat.AbilityConfig[AbilityDefinitions.Length];
            for (int i = 0; i < AbilityDefinitions.Length; i++)
                abilityDefs[i] = AbilityDefinitions[i] != null
                    ? AbilityDefinitions[i].ToAbilityConfig()
                    : new Simulation.Combat.AbilityConfig();

            // Convert enemy SO array to plain C# array
            var enemyDefs = new Simulation.Combat.EnemyConfig[EnemyDefinitions.Length];
            for (int i = 0; i < EnemyDefinitions.Length; i++)
                enemyDefs[i] = EnemyDefinitions[i] != null
                    ? EnemyDefinitions[i].ToEnemyConfig()
                    : new Simulation.Combat.EnemyConfig();

            return new SimulationConfig
            {
                BaseTravelSpeed = BaseTravelSpeed,
                AthleticsSpeedPerLevel = AthleticsSpeedPerLevel,
                AthleticsXpPerTick = AthleticsXpPerTick,
                InNodeSpeedMultiplier = InNodeSpeedMultiplier,
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
                InventorySize = InventorySize,
                DepositDurationTicks = DepositDurationTicks,
                AutomationPeriodicCheckInterval = AutomationPeriodicCheckInterval,
                MacroDecisionLogMaxEntries = MacroDecisionLogMaxEntries,
                MicroDecisionLogMaxEntries = MicroDecisionLogMaxEntries,
                ChronicleMaxEntries = ChronicleMaxEntries,
                EventLogMaxEntries = EventLogMaxEntries,
                DeathRespawnBaseTime = DeathRespawnBaseTime,
                DeathRespawnTravelMultiplier = DeathRespawnTravelMultiplier,
                BaseHitpoints = BaseHitpoints,
                HitpointsPerLevel = HitpointsPerLevel,
                BaseMana = BaseMana,
                ManaPerRestorationLevel = ManaPerRestorationLevel,
                BaseManaRegenPerTick = BaseManaRegenPerTick,
                BaseDisengageTimeTicks = BaseDisengageTimeTicks,
                MinDisengageTimeTicks = MinDisengageTimeTicks,
                DisengageReductionPerAthleticsLevel = DisengageReductionPerAthleticsLevel,
                CombatXpPerActionTimeTick = CombatXpPerActionTimeTick,
                DefenceReductionPerLevel = DefenceReductionPerLevel,
                MaxDefenceReductionPercent = MaxDefenceReductionPercent,
                CombatDamageScalingPerLevel = CombatDamageScalingPerLevel,
                HitpointsXpPerDamage = HitpointsXpPerDamage,
                DefenceXpPerDamage = DefenceXpPerDamage,
                AbilityDefinitions = abilityDefs,
                EnemyDefinitions = enemyDefs,
            };
        }
    }
}
