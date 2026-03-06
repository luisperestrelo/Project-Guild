using System;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// A single entry in an enemy's loot table. When the enemy dies, each entry
    /// rolls independently (not mutually exclusive).
    /// </summary>
    [Serializable]
    public class LootTableEntry
    {
        /// <summary>
        /// Item definition ID (e.g. "goblin_tooth", "copper_ore").
        /// </summary>
        public string ItemId;

        /// <summary>
        /// Chance to drop (0.0 to 1.0). 1.0 = guaranteed.
        /// </summary>
        public float DropChance = 1.0f;

        /// <summary>
        /// Minimum quantity when this entry drops.
        /// </summary>
        public int MinQuantity = 1;

        /// <summary>
        /// Maximum quantity when this entry drops (inclusive).
        /// </summary>
        public int MaxQuantity = 1;

        public LootTableEntry() { }

        public LootTableEntry(string itemId, float dropChance = 1.0f,
            int minQuantity = 1, int maxQuantity = 1)
        {
            ItemId = itemId;
            DropChance = dropChance;
            MinQuantity = minQuantity;
            MaxQuantity = maxQuantity;
        }
    }
}
