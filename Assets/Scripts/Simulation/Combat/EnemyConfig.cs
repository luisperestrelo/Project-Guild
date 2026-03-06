using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// AI behavior pattern for enemies. Determines targeting priority.
    /// Taunt permanently overrides targeting for all behaviors.
    /// </summary>
    public enum EnemyAiBehavior
    {
        /// <summary>Attack nearest runner. Most common behavior for overworld mobs.</summary>
        Aggressive,

        /// <summary>Attack lowest HP runner (healer-killer).</summary>
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
        /// Enemy level. Visual aid for the player only: no mechanical effect.
        /// Displayed on tooltips and nameplates to help players gauge difficulty.
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
        /// What drops when this enemy dies.
        /// </summary>
        public List<LootTableEntry> LootTable = new();

        /// <summary>
        /// How this enemy picks targets.
        /// </summary>
        public EnemyAiBehavior AiBehavior = EnemyAiBehavior.Aggressive;

        public EnemyConfig() { }
    }
}
