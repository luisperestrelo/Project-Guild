using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Items
{
    /// <summary>
    /// Guild bank — infinite slots, everything stacks regardless of item's Stackable flag.
    /// Shared across all runners. Located at the Hub.
    /// </summary>
    [Serializable]
    public class Bank
    {
        public List<ItemStack> Stacks = new();

        /// <summary>
        /// Deposit items into the bank. Always stacks with existing entries.
        /// </summary>
        public void Deposit(string itemId, int quantity)
        {
            if (quantity <= 0) return;

            for (int i = 0; i < Stacks.Count; i++)
            {
                if (Stacks[i].ItemId == itemId)
                {
                    Stacks[i].Quantity += quantity;
                    return;
                }
            }

            Stacks.Add(new ItemStack(itemId, quantity));
        }

        /// <summary>
        /// Move everything from an inventory into the bank, clearing the inventory.
        /// </summary>
        public void DepositAll(Inventory inventory)
        {
            for (int i = 0; i < inventory.Slots.Count; i++)
            {
                var slot = inventory.Slots[i];
                Deposit(slot.ItemId, slot.Quantity);
            }

            inventory.Clear();
        }

        /// <summary>
        /// Withdraw items from the bank into an inventory. Returns the actual amount withdrawn
        /// (may be less than requested if bank doesn't have enough or inventory is full).
        /// </summary>
        public int Withdraw(string itemId, int quantity, Inventory inventory, ItemDefinition def)
        {
            int available = CountItem(itemId);
            int toWithdraw = Math.Min(quantity, available);
            if (toWithdraw <= 0) return 0;

            // Try to add to inventory first — if it can't fit, reduce the amount
            int withdrawn = 0;
            for (int i = 0; i < toWithdraw; i++)
            {
                if (inventory.TryAdd(def, 1))
                    withdrawn++;
                else
                    break;
            }

            // Remove from bank
            if (withdrawn > 0)
                RemoveFromBank(itemId, withdrawn);

            return withdrawn;
        }

        public int CountItem(string itemId)
        {
            for (int i = 0; i < Stacks.Count; i++)
            {
                if (Stacks[i].ItemId == itemId)
                    return Stacks[i].Quantity;
            }
            return 0;
        }

        private void RemoveFromBank(string itemId, int quantity)
        {
            for (int i = 0; i < Stacks.Count; i++)
            {
                if (Stacks[i].ItemId != itemId) continue;

                Stacks[i].Quantity -= quantity;
                if (Stacks[i].Quantity <= 0)
                    Stacks.RemoveAt(i);
                return;
            }
        }
    }
}
