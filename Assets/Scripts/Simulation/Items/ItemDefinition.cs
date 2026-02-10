using System;

namespace ProjectGuild.Simulation.Items
{
    public enum ItemCategory
    {
        Ore,
        Log,
        Fish,
        Herb,
        Consumable,
        Currency,
        CraftedGear,
        Misc,
    }

    /// <summary>
    /// Defines an item type. This is the "template" — ItemStack references these by ID.
    /// Populated into ItemRegistry at game start from config.
    /// </summary>
    [Serializable]
    public class ItemDefinition
    {
        public string Id;
        public string Name;
        public ItemCategory Category;
        public bool Stackable;
        public int MaxStack = 1;

        public ItemDefinition() { }

        public ItemDefinition(string id, string name, ItemCategory category, bool stackable = false, int maxStack = 1)
        {
            Id = id;
            Name = name;
            Category = category;
            Stackable = stackable;
            MaxStack = stackable ? maxStack : 1;
        }
    }
}
