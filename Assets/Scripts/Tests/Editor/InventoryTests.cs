using NUnit.Framework;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class InventoryTests
    {
        private ItemDefinition _ore;
        private ItemDefinition _coins;

        [SetUp]
        public void SetUp()
        {
            _ore = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            _coins = new ItemDefinition("coins", "Coins", ItemCategory.Currency, stackable: true, maxStack: 9999);
        }

        [Test]
        public void TryAdd_NonStackable_UsesOneSlot()
        {
            var inv = new Inventory(28);
            bool added = inv.TryAdd(_ore);

            Assert.IsTrue(added);
            Assert.AreEqual(1, inv.Slots.Count);
            Assert.AreEqual(27, inv.FreeSlots);
        }

        [Test]
        public void TryAdd_NonStackable_MultipleItems_UsesMultipleSlots()
        {
            var inv = new Inventory(28);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);

            Assert.AreEqual(3, inv.Slots.Count);
            Assert.AreEqual(3, inv.CountItem("copper_ore"));
        }

        [Test]
        public void TryAdd_NonStackable_WhenFull_ReturnsFalse()
        {
            var inv = new Inventory(3);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);

            bool added = inv.TryAdd(_ore);
            Assert.IsFalse(added);
            Assert.AreEqual(3, inv.Slots.Count);
        }

        [Test]
        public void TryAdd_NonStackable_BulkAdd_FailsIfNotEnoughSlots()
        {
            var inv = new Inventory(3);
            bool added = inv.TryAdd(_ore, 5);

            Assert.IsFalse(added);
            Assert.AreEqual(0, inv.Slots.Count);
        }

        [Test]
        public void TryAdd_Stackable_StacksOnExisting()
        {
            var inv = new Inventory(28);
            inv.TryAdd(_coins, 50);
            inv.TryAdd(_coins, 30);

            Assert.AreEqual(1, inv.Slots.Count);
            Assert.AreEqual(80, inv.CountItem("coins"));
        }

        [Test]
        public void TryAdd_Stackable_NewSlot_WhenNoExistingStack()
        {
            var inv = new Inventory(28);
            inv.TryAdd(_coins, 10);

            Assert.AreEqual(1, inv.Slots.Count);
            Assert.AreEqual(10, inv.Slots[0].Quantity);
        }

        [Test]
        public void TryAdd_Stackable_RespectsMaxStack()
        {
            var smallStack = new ItemDefinition("runes", "Runes", ItemCategory.Misc, stackable: true, maxStack: 100);
            var inv = new Inventory(28);
            inv.TryAdd(smallStack, 80);
            inv.TryAdd(smallStack, 50);

            // 80 in first slot, 50 overflows: 20 fills first to 100, 30 in new slot
            Assert.AreEqual(2, inv.Slots.Count);
            Assert.AreEqual(130, inv.CountItem("runes"));
        }

        [Test]
        public void Remove_DecreasesQuantity()
        {
            var inv = new Inventory(28);
            inv.TryAdd(_coins, 50);
            bool removed = inv.Remove("coins", 20);

            Assert.IsTrue(removed);
            Assert.AreEqual(30, inv.CountItem("coins"));
        }

        [Test]
        public void Remove_RemovesSlot_WhenQuantityZero()
        {
            var inv = new Inventory(28);
            inv.TryAdd(_ore);
            inv.Remove("copper_ore", 1);

            Assert.AreEqual(0, inv.Slots.Count);
        }

        [Test]
        public void Remove_NonExistent_ReturnsFalse()
        {
            var inv = new Inventory(28);
            bool removed = inv.Remove("nonexistent", 1);
            Assert.IsFalse(removed);
        }

        [Test]
        public void IsFull_ReturnsTrueWhen28Slots()
        {
            var inv = new Inventory(28);
            for (int i = 0; i < 28; i++)
                inv.TryAdd(_ore);

            Assert.IsTrue(inv.IsFull(_ore));
        }

        [Test]
        public void IsFull_ReturnsFalseWhenSlotsAvailable()
        {
            var inv = new Inventory(28);
            inv.TryAdd(_ore);
            Assert.IsFalse(inv.IsFull(_ore));
        }

        [Test]
        public void IsFull_Stackable_FalseWhenExistingStackHasRoom()
        {
            var inv = new Inventory(1);
            inv.TryAdd(_coins, 50);
            // Slot is used but coins can stack more
            Assert.IsFalse(inv.IsFull(_coins));
        }

        [Test]
        public void CountItem_ReturnsCorrectCount()
        {
            var inv = new Inventory(28);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);

            Assert.AreEqual(3, inv.CountItem("copper_ore"));
            Assert.AreEqual(0, inv.CountItem("nonexistent"));
        }

        [Test]
        public void Clear_RemovesAllItems()
        {
            var inv = new Inventory(28);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);
            inv.TryAdd(_coins, 100);

            inv.Clear();
            Assert.AreEqual(0, inv.Slots.Count);
            Assert.AreEqual(28, inv.FreeSlots);
        }

        [Test]
        public void FreeSlots_ReturnsCorrectCount()
        {
            var inv = new Inventory(28);
            Assert.AreEqual(28, inv.FreeSlots);

            inv.TryAdd(_ore);
            Assert.AreEqual(27, inv.FreeSlots);
        }
    }
}
