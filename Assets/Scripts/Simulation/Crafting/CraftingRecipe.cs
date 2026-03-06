using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Simulation.Crafting
{
    public enum CraftingStation
    {
        Engineering,
        Alchemy,
    }

    /// <summary>
    /// A material requirement for a recipe.
    /// </summary>
    [Serializable]
    public class RecipeIngredient
    {
        public string ItemId;
        public int Quantity;

        public RecipeIngredient() { }
        public RecipeIngredient(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }

    /// <summary>
    /// Defines a crafting recipe. Bare minimum for demo.
    /// </summary>
    [Serializable]
    public class CraftingRecipe
    {
        public string Id;
        public string Name;
        public CraftingStation Station;
        public SkillType RequiredSkill;
        public int MinLevel;
        public List<RecipeIngredient> Ingredients = new();

        /// <summary>
        /// Time in ticks to craft one item.
        /// </summary>
        public int CraftTicks = 30; // 3 seconds at 10 ticks/sec

        /// <summary>
        /// XP awarded on completion.
        /// </summary>
        public float XpReward = 50f;

        /// <summary>
        /// The item produced. For equipment, this creates an EquipmentItem.
        /// For consumables, this adds to inventory as a regular item.
        /// </summary>
        public string ProducedItemId;

        /// <summary>
        /// If non-null, the crafted item is an equipment piece with these stats.
        /// </summary>
        public EquipmentSlot? EquipmentSlot;
        public Dictionary<SkillType, int> EquipmentStats;
    }
}
