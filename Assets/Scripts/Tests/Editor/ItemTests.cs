using NUnit.Framework;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class ItemTests
    {
        [Test]
        public void ItemRegistry_Register_And_Get()
        {
            var registry = new ItemRegistry();
            var def = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            registry.Register(def);

            Assert.AreEqual(def, registry.Get("copper_ore"));
        }

        [Test]
        public void ItemRegistry_Get_Unknown_ReturnsNull()
        {
            var registry = new ItemRegistry();
            Assert.IsNull(registry.Get("nonexistent"));
        }

        [Test]
        public void ItemRegistry_Has_ReturnsTrueForRegistered()
        {
            var registry = new ItemRegistry();
            registry.Register(new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore));

            Assert.IsTrue(registry.Has("copper_ore"));
            Assert.IsFalse(registry.Has("iron_ore"));
        }

        [Test]
        public void ItemStack_Constructor_SetsFields()
        {
            var stack = new ItemStack("copper_ore", 5);
            Assert.AreEqual("copper_ore", stack.ItemId);
            Assert.AreEqual(5, stack.Quantity);
        }

        [Test]
        public void ItemDefinition_NonStackable_MaxStackIsOne()
        {
            var def = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore, stackable: false);
            Assert.AreEqual(1, def.MaxStack);
        }

        [Test]
        public void ItemDefinition_Stackable_PreservesMaxStack()
        {
            var def = new ItemDefinition("coins", "Coins", ItemCategory.Currency, stackable: true, maxStack: 99999);
            Assert.IsTrue(def.Stackable);
            Assert.AreEqual(99999, def.MaxStack);
        }
    }
}
