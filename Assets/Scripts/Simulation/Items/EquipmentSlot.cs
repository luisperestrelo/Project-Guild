namespace ProjectGuild.Simulation.Items
{
    public enum EquipmentSlot
    {
        MainHand = 0,
        OffHand = 1,
        Helmet = 2,
        BodyArmour = 3,
        Gloves = 4,
        Boots = 5,
        Tool = 6,
    }

    public static class EquipmentSlotExtensions
    {
        public const int SlotCount = 7;

        public static string DisplayName(this EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.MainHand: return "Main Hand";
                case EquipmentSlot.OffHand: return "Off Hand";
                case EquipmentSlot.Helmet: return "Helmet";
                case EquipmentSlot.BodyArmour: return "Body Armour";
                case EquipmentSlot.Gloves: return "Gloves";
                case EquipmentSlot.Boots: return "Boots";
                case EquipmentSlot.Tool: return "Tool";
                default: return slot.ToString();
            }
        }
    }
}
