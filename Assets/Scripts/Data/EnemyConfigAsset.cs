using System.Collections.Generic;
using UnityEngine;
using ProjectGuild.Simulation.Combat;

namespace ProjectGuild.Data
{
    /// <summary>
    /// ScriptableObject for authoring an enemy type in the Unity inspector.
    /// Each enemy type (goblin grunt, goblin shaman, etc.) gets its own asset.
    /// The simulation layer receives a plain C# EnemyConfig at runtime via ToEnemyConfig().
    /// </summary>
    [CreateAssetMenu(fileName = "New Enemy", menuName = "Project Guild/Enemy Config")]
    public class EnemyConfigAsset : ScriptableObject
    {
        [Tooltip("Unique identifier (e.g. 'goblin_grunt'). Must be unique across all enemies.")]
        public string Id;

        [Tooltip("Player-facing display name.")]
        public string Name;

        [Tooltip("Enemy level. Affects damage dealt and damage reduction.")]
        public int Level = 1;

        [Tooltip("Maximum hitpoints for this enemy type.")]
        public float MaxHitpoints = 100f;

        [Tooltip("Base damage per attack before scaling.")]
        public float BaseDamage = 5f;

        [Tooltip("Flat damage reduction applied to incoming attacks.")]
        public float BaseDefence;

        [Tooltip("Ticks between enemy auto-attacks. At 10 ticks/sec: 15 = 1.5 seconds.")]
        public int AttackSpeedTicks = 15;

        [Tooltip("Abilities this enemy can use (by ID). Evaluated in order; first available is used.")]
        public List<string> AbilityIds = new();

        [Tooltip("Loot table entries. Each entry rolls independently on enemy death.")]
        public LootTableEntryData[] LootTable = new LootTableEntryData[0];

        [Tooltip("How this enemy picks targets.")]
        public EnemyAiBehavior AiBehavior = EnemyAiBehavior.Aggressive;

        [Tooltip("XP awarded to the killing runner on death.")]
        public float XpOnDeath = 50f;

        public EnemyConfig ToEnemyConfig()
        {
            var config = new EnemyConfig
            {
                Id = Id,
                Name = Name,
                Level = Level,
                MaxHitpoints = MaxHitpoints,
                BaseDamage = BaseDamage,
                BaseDefence = BaseDefence,
                AttackSpeedTicks = AttackSpeedTicks,
                AbilityIds = new List<string>(AbilityIds),
                AiBehavior = AiBehavior,
                XpOnDeath = XpOnDeath,
            };

            foreach (var entry in LootTable)
            {
                if (entry != null)
                {
                    config.LootTable.Add(new LootTableEntry(
                        entry.ItemId, entry.DropChance, entry.MinQuantity, entry.MaxQuantity));
                }
            }

            return config;
        }
    }

    /// <summary>
    /// Serializable loot table entry for the inspector.
    /// Unity can't serialize generic Lists of classes inside arrays cleanly,
    /// so this is a flat struct-like class for the inspector.
    /// </summary>
    [System.Serializable]
    public class LootTableEntryData
    {
        [Tooltip("Item ID to drop (e.g. 'goblin_tooth').")]
        public string ItemId;

        [Tooltip("Chance to drop (0.0 to 1.0). 1.0 = guaranteed.")]
        [Range(0f, 1f)]
        public float DropChance = 1.0f;

        [Tooltip("Minimum quantity when this entry drops.")]
        public int MinQuantity = 1;

        [Tooltip("Maximum quantity when this entry drops (inclusive).")]
        public int MaxQuantity = 1;
    }
}
