using System.Collections.Generic;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Simulation.Crafting
{
    /// <summary>
    /// All crafting recipes in the game. Hardcoded for demo, would be SO-driven later.
    /// </summary>
    public static class CraftingRecipeRegistry
    {
        private static readonly Dictionary<string, CraftingRecipe> _recipes = new();
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _recipes.Clear();

            // ── Engineering recipes (weapons, armor) ──────────────────

            Register(new CraftingRecipe
            {
                Id = "recipe_copper_sword",
                Name = "Copper Sword",
                Station = CraftingStation.Engineering,
                RequiredSkill = SkillType.Engineering,
                MinLevel = 1,
                Ingredients = new() { new("copper_ore", 5) },
                CraftTicks = 30,
                XpReward = 40f,
                ProducedItemId = "copper_sword",
                EquipmentSlot = Items.EquipmentSlot.MainHand,
                EquipmentStats = new() { { SkillType.Melee, 3 } },
            });

            Register(new CraftingRecipe
            {
                Id = "recipe_copper_shield",
                Name = "Copper Shield",
                Station = CraftingStation.Engineering,
                RequiredSkill = SkillType.Engineering,
                MinLevel = 1,
                Ingredients = new() { new("copper_ore", 5) },
                CraftTicks = 30,
                XpReward = 40f,
                ProducedItemId = "copper_shield",
                EquipmentSlot = Items.EquipmentSlot.OffHand,
                EquipmentStats = new() { { SkillType.Defence, 3 } },
            });

            Register(new CraftingRecipe
            {
                Id = "recipe_copper_helmet",
                Name = "Copper Helmet",
                Station = CraftingStation.Engineering,
                RequiredSkill = SkillType.Engineering,
                MinLevel = 1,
                Ingredients = new() { new("copper_ore", 4) },
                CraftTicks = 25,
                XpReward = 35f,
                ProducedItemId = "copper_helmet",
                EquipmentSlot = Items.EquipmentSlot.Helmet,
                EquipmentStats = new() { { SkillType.Defence, 2 }, { SkillType.Hitpoints, 1 } },
            });

            Register(new CraftingRecipe
            {
                Id = "recipe_copper_body",
                Name = "Copper Body Armour",
                Station = CraftingStation.Engineering,
                RequiredSkill = SkillType.Engineering,
                MinLevel = 1,
                Ingredients = new() { new("copper_ore", 8) },
                CraftTicks = 40,
                XpReward = 55f,
                ProducedItemId = "copper_body",
                EquipmentSlot = Items.EquipmentSlot.BodyArmour,
                EquipmentStats = new() { { SkillType.Defence, 4 }, { SkillType.Hitpoints, 2 } },
            });

            Register(new CraftingRecipe
            {
                Id = "recipe_wooden_staff",
                Name = "Wooden Staff",
                Station = CraftingStation.Engineering,
                RequiredSkill = SkillType.Engineering,
                MinLevel = 1,
                Ingredients = new() { new("pine_log", 5) },
                CraftTicks = 25,
                XpReward = 35f,
                ProducedItemId = "wooden_staff",
                EquipmentSlot = Items.EquipmentSlot.MainHand,
                EquipmentStats = new() { { SkillType.Magic, 4 } },
            });

            Register(new CraftingRecipe
            {
                Id = "recipe_wooden_wand",
                Name = "Wooden Wand",
                Station = CraftingStation.Engineering,
                RequiredSkill = SkillType.Engineering,
                MinLevel = 1,
                Ingredients = new() { new("pine_log", 3) },
                CraftTicks = 20,
                XpReward = 25f,
                ProducedItemId = "wooden_wand",
                EquipmentSlot = Items.EquipmentSlot.MainHand,
                EquipmentStats = new() { { SkillType.Restoration, 3 }, { SkillType.Magic, 1 } },
            });

            // ── Alchemy recipes (potions/consumables) ────────────────

            Register(new CraftingRecipe
            {
                Id = "recipe_health_potion",
                Name = "Health Potion",
                Station = CraftingStation.Alchemy,
                RequiredSkill = SkillType.PotionMaking,
                MinLevel = 1,
                Ingredients = new() { new("herb", 3) },
                CraftTicks = 20,
                XpReward = 30f,
                ProducedItemId = "health_potion",
            });

            Register(new CraftingRecipe
            {
                Id = "recipe_mana_potion",
                Name = "Mana Potion",
                Station = CraftingStation.Alchemy,
                RequiredSkill = SkillType.PotionMaking,
                MinLevel = 1,
                Ingredients = new() { new("herb", 3) },
                CraftTicks = 20,
                XpReward = 30f,
                ProducedItemId = "mana_potion",
            });
        }

        private static void Register(CraftingRecipe recipe)
        {
            _recipes[recipe.Id] = recipe;
        }

        public static CraftingRecipe Get(string id)
        {
            return _recipes.TryGetValue(id, out var r) ? r : null;
        }

        public static IEnumerable<CraftingRecipe> GetAllForStation(CraftingStation station)
        {
            foreach (var kvp in _recipes)
            {
                if (kvp.Value.Station == station)
                    yield return kvp.Value;
            }
        }

        public static IEnumerable<CraftingRecipe> All => _recipes.Values;
    }
}
