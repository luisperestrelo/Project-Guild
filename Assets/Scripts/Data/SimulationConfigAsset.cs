using UnityEngine;
using ProjectGuild.Simulation.Core;

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

        [Header("Skills / XP")]
        [Tooltip("Multiplier on effective level when runner has passion (e.g. 1.05 = +5%)")]
        public float PassionEffectivenessMultiplier = 1.05f;

        [Tooltip("Multiplier on XP gain when runner has passion")]
        public float PassionXpMultiplier = 1.5f;

        [Tooltip("Base multiplier for XP curve. Each level costs: level^exponent * base")]
        public float XpCurveBase = 50f;

        [Tooltip("Exponent for XP curve. Higher = steeper at high levels. OSRS-like is ~2.0")]
        public float XpCurveExponent = 1.5f;

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

        [Header("Death")]
        [Tooltip("Seconds a runner is unavailable after dying (non-raid) before respawning")]
        public float DeathRespawnDelay = 45f;

        /// <summary>
        /// Convert this ScriptableObject's values into a plain C# SimulationConfig
        /// that the simulation layer can use.
        /// </summary>
        public SimulationConfig ToConfig()
        {
            return new SimulationConfig
            {
                BaseTravelSpeed = BaseTravelSpeed,
                AthleticsSpeedPerLevel = AthleticsSpeedPerLevel,
                PassionEffectivenessMultiplier = PassionEffectivenessMultiplier,
                PassionXpMultiplier = PassionXpMultiplier,
                XpCurveBase = XpCurveBase,
                XpCurveExponent = XpCurveExponent,
                MinStartingLevel = MinStartingLevel,
                MaxStartingLevel = MaxStartingLevel,
                PassionChance = PassionChance,
                EasterEggNameChance = EasterEggNameChance,
                DeathRespawnDelay = DeathRespawnDelay,
            };
        }
    }
}
