using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Items
{
    /// <summary>
    /// OSRS-style fixed-slot inventory. Non-stackable items take 1 slot each.
    /// Stackable items share a slot up to MaxStack.
    /// </summary>
    [Serializable]
    public class Inventory
    {
        public int MaxSlots;
        public List<ItemStack> Slots = new();

        public Inventory() { }

        public Inventory(int maxSlots)
        {
            MaxSlots = maxSlots;
        }

        public int FreeSlots => MaxSlots - Slots.Count;

        /// <summary>
        /// Try to add an item. Returns true if successful.
        /// For non-stackable items, each unit takes a slot.
        /// For stackable items, tries to stack on existing before using a new slot.
        /// </summary>
        public bool TryAdd(ItemDefinition def, int quantity = 1)
        {
            if (def == null || quantity <= 0) return false;

            if (def.Stackable)
                return TryAddStackable(def, quantity);

            return TryAddNonStackable(def, quantity);
        }

        private bool TryAddStackable(ItemDefinition def, int quantity)
        {
            int remaining = quantity;

            // Try to add to existing stacks first
            for (int i = 0; i < Slots.Count && remaining > 0; i++)
            {
                if (Slots[i].ItemId != def.Id) continue;
                int canAdd = def.MaxStack - Slots[i].Quantity;
                if (canAdd <= 0) continue;

                int toAdd = Math.Min(canAdd, remaining);
                Slots[i].Quantity += toAdd;
                remaining -= toAdd;
            }

            // Use new slots for the rest
            while (remaining > 0 && Slots.Count < MaxSlots)
            {
                int toAdd = Math.Min(remaining, def.MaxStack);
                Slots.Add(new ItemStack(def.Id, toAdd));
                remaining -= toAdd;
            }

            return remaining == 0;
        }

        private bool TryAddNonStackable(ItemDefinition def, int quantity)
        {
            if (Slots.Count + quantity > MaxSlots)
                return false;

            for (int i = 0; i < quantity; i++)
            {
                Slots.Add(new ItemStack(def.Id, 1));
            }

            return true;
        }

        /// <summary>
        /// Remove a specific number of items by ID, scanning across all slots.
        /// For example, RemoveItemsOfType("gold_ore", 3) removes 3 gold ore total,
        /// pulling from multiple slots if necessary (iterates backwards to safely handle slot deletion).
        /// Returns true if the full amount was removed, false if not enough items existed.
        /// </summary>
        public bool RemoveItemsOfType(string itemId, int count = 1)
        {
            int remaining = count;

            for (int i = Slots.Count - 1; i >= 0 && remaining > 0; i--)
            {
                if (Slots[i].ItemId != itemId) continue;

                int toRemove = Math.Min(Slots[i].Quantity, remaining);
                Slots[i].Quantity -= toRemove;
                remaining -= toRemove;

                if (Slots[i].Quantity <= 0)
                    Slots.RemoveAt(i);
            }

            return remaining == 0;
        }

        /// <summary>
        /// Can we add one more of this item? For non-stackable: need a free slot.
        /// For stackable: need either an existing non-full stack or a free slot.
        /// </summary>
        public bool IsFull(ItemDefinition def)
        {
            if (def == null) return true;

            if (def.Stackable)
            {
                // Check existing stacks
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i].ItemId == def.Id && Slots[i].Quantity < def.MaxStack)
                        return false;
                }
            }

            return Slots.Count >= MaxSlots;
        }

        public int CountItem(string itemId)
        {
            int count = 0;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].ItemId == itemId)
                    count += Slots[i].Quantity;
            }
            return count;
        }

        public void Clear()
        {
            Slots.Clear();
        }
    }
}
