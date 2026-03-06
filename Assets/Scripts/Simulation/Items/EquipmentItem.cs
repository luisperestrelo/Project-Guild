using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Items
{
    /// <summary>
    /// An equipped item instance. Bare-minimum for demo: stat bonuses only.
    /// </summary>
    [Serializable]
    public class EquipmentItem
    {
        public string ItemId;
        public string Name;
        public EquipmentSlot Slot;
        public Dictionary<SkillType, int> StatBonuses = new();

        public EquipmentItem() { }

        public EquipmentItem(string itemId, string name, EquipmentSlot slot)
        {
            ItemId = itemId;
            Name = name;
            Slot = slot;
        }

        public EquipmentItem WithBonus(SkillType skill, int amount)
        {
            StatBonuses[skill] = amount;
            return this;
        }
    }

    /// <summary>
    /// Runner's equipment loadout. Array indexed by EquipmentSlot.
    /// </summary>
    [Serializable]
    public class Equipment
    {
        public EquipmentItem[] Slots = new EquipmentItem[EquipmentSlotExtensions.SlotCount];

        public EquipmentItem GetSlot(EquipmentSlot slot) => Slots[(int)slot];

        public void Equip(EquipmentItem item)
        {
            Slots[(int)item.Slot] = item;
        }

        public EquipmentItem Unequip(EquipmentSlot slot)
        {
            var item = Slots[(int)slot];
            Slots[(int)slot] = null;
            return item;
        }

        public int GetTotalBonus(SkillType skill)
        {
            int total = 0;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] != null && Slots[i].StatBonuses.TryGetValue(skill, out int bonus))
                    total += bonus;
            }
            return total;
        }
    }
}
