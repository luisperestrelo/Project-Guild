using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// AI behavior pattern for enemies. Determines targeting priority.
    /// </summary>
    public enum EnemyAiBehavior
    {
        /// <summary>Attack nearest runner. Switch to taunter if taunted.</summary>
        Aggressive,

        /// <summary>Attack the runner dealing the most damage (threat table). Switch to taunter if taunted.</summary>
        ThreatBased,

        /// <summary>Attack lowest HP runner (healer-killer). Switch to taunter if taunted.</summary>
        Opportunistic,
    }

    /// <summary>
    /// Template definition of an enemy type. Immutable at runtime (authored via SO or code).
    /// Multiple instances of the same EnemyConfig can exist in an encounter.
    /// </summary>
    [Serializable]
    public class EnemyConfig
    {
        /// <summary>
        /// Unique identifier (e.g. "goblin_grunt", "goblin_shaman").
        /// </summary>
        public string Id;

        /// <summary>
        /// Player-facing display name.
        /// </summary>
        public string Name;

        /// <summary>
        /// Enemy level. Affects damage dealt and damage reduction from Defence.
        /// </summary>
        public int Level = 1;

        /// <summary>
        /// Maximum hitpoints for this enemy type.
        /// </summary>
        public float MaxHitpoints = 100f;

        /// <summary>
        /// Base damage per attack before scaling.
        /// </summary>
        public float BaseDamage = 5f;

        /// <summary>
        /// Flat damage reduction applied to incoming attacks.
        /// </summary>
        public float BaseDefence;

        /// <summary>
        /// Ticks between enemy auto-attacks.
        /// </summary>
        public int AttackSpeedTicks = 15;

        /// <summary>
        /// Abilities this enemy can use (by ID). Evaluated in order; first available is used.
        /// Empty = basic auto-attack only.
        /// </summary>
        public List<string> AbilityIds = new();

        /// <summary>
        /// What drops when this enemy dies.
        /// </summary>
        public List<LootTableEntry> LootTable = new();

        /// <summary>
        /// How this enemy picks targets.
        /// </summary>
        public EnemyAiBehavior AiBehavior = EnemyAiBehavior.Aggressive;

        /// <summary>
        /// XP awarded to the killing runner on death. Split across relevant combat skills.
        /// </summary>
        public float XpOnDeath = 50f;

        public EnemyConfig() { }
    }
}
