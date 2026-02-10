using NUnit.Framework;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class BankTests
    {
        private ItemDefinition _ore;
        private ItemDefinition _log;

        [SetUp]
        public void SetUp()
        {
            _ore = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            _log = new ItemDefinition("pine_log", "Pine Log", ItemCategory.Log);
        }

        [Test]
        public void Deposit_AddsNewStack()
        {
            var bank = new Bank();
            bank.Deposit("copper_ore", 10);

            Assert.AreEqual(10, bank.CountItem("copper_ore"));
            Assert.AreEqual(1, bank.Stacks.Count);
        }

        [Test]
        public void Deposit_StacksOnExisting()
        {
            var bank = new Bank();
            bank.Deposit("copper_ore", 10);
            bank.Deposit("copper_ore", 15);

            Assert.AreEqual(25, bank.CountItem("copper_ore"));
            Assert.AreEqual(1, bank.Stacks.Count);
        }

        [Test]
        public void Deposit_DifferentItems_SeparateStacks()
        {
            var bank = new Bank();
            bank.Deposit("copper_ore", 10);
            bank.Deposit("pine_log", 5);

            Assert.AreEqual(10, bank.CountItem("copper_ore"));
            Assert.AreEqual(5, bank.CountItem("pine_log"));
            Assert.AreEqual(2, bank.Stacks.Count);
        }

        [Test]
        public void DepositAll_MovesAllItems()
        {
            var bank = new Bank();
            var inv = new Inventory(28);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);
            inv.TryAdd(_log);

            bank.DepositAll(inv);

            Assert.AreEqual(3, bank.CountItem("copper_ore"));
            Assert.AreEqual(1, bank.CountItem("pine_log"));
        }

        [Test]
        public void DepositAll_ClearsInventory()
        {
            var bank = new Bank();
            var inv = new Inventory(28);
            inv.TryAdd(_ore);
            inv.TryAdd(_ore);

            bank.DepositAll(inv);

            Assert.AreEqual(0, inv.Slots.Count);
            Assert.AreEqual(28, inv.FreeSlots);
        }

        [Test]
        public void Withdraw_MovesToInventory()
        {
            var bank = new Bank();
            bank.Deposit("copper_ore", 10);

            var inv = new Inventory(28);
            int withdrawn = bank.Withdraw("copper_ore", 5, inv, _ore);

            Assert.AreEqual(5, withdrawn);
            Assert.AreEqual(5, inv.CountItem("copper_ore"));
            Assert.AreEqual(5, bank.CountItem("copper_ore"));
        }

        [Test]
        public void Withdraw_LimitedByBankAmount()
        {
            var bank = new Bank();
            bank.Deposit("copper_ore", 3);

            var inv = new Inventory(28);
            int withdrawn = bank.Withdraw("copper_ore", 10, inv, _ore);

            Assert.AreEqual(3, withdrawn);
            Assert.AreEqual(3, inv.CountItem("copper_ore"));
            Assert.AreEqual(0, bank.CountItem("copper_ore"));
        }

        [Test]
        public void Withdraw_LimitedByInventorySpace()
        {
            var bank = new Bank();
            bank.Deposit("copper_ore", 100);

            var inv = new Inventory(3);
            int withdrawn = bank.Withdraw("copper_ore", 100, inv, _ore);

            Assert.AreEqual(3, withdrawn);
            Assert.AreEqual(3, inv.CountItem("copper_ore"));
            Assert.AreEqual(97, bank.CountItem("copper_ore"));
        }

        [Test]
        public void CountItem_ReturnsZeroForUnknown()
        {
            var bank = new Bank();
            Assert.AreEqual(0, bank.CountItem("nonexistent"));
        }
    }
}
