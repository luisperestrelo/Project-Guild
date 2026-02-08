using System;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// All tunable simulation parameters live here. No magic numbers in game logic —
    /// everything references this config. The Bridge/Data layer populates this from
    /// a ScriptableObject so values are tweakable in the Unity inspector.
    ///
    /// For tests, you can just new up a SimulationConfig() and get sensible defaults.
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
        /// OSRS-like feel is around 2.0. Current default 1.5 is gentler.
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
        /// Chance (0-1) for each individual skill to have passion on a new runner.
        /// </summary>
        public float PassionChance = 0.2f;

        /// <summary>
        /// Chance (0-1) for a newly generated runner to get an easter egg name
        /// instead of a random first+last name.
        /// </summary>
        public float EasterEggNameChance = 0.02f;

        // ─── Death (Non-Raid) ────────────────────────────────────────

        /// <summary>
        /// Seconds a runner is unavailable after dying before respawning at hub.
        /// This is a secondary deterrent — the primary anti-suicide-teleport mechanism
        /// is that runners DROP THEIR INVENTORY on death in the overworld.
        /// Losing a full inventory of gathered resources makes dying strictly worse
        /// than walking back in every scenario.
        /// </summary>
        public float DeathRespawnDelay = 45f;
    }
}
