using System;

namespace ProjectGuild.Simulation.Items
{
    /// <summary>
    /// A stack of items in an inventory slot or bank slot.
    /// References an ItemDefinition by ID. Quantity is always >= 1.
    /// </summary>
    [Serializable]
    public class ItemStack
    {
        public string ItemId;
        public int Quantity;

        public ItemStack() { }

        public ItemStack(string itemId, int quantity = 1)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }
}
