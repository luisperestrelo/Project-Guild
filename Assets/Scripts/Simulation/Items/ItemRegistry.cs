using System.Collections.Generic;

namespace ProjectGuild.Simulation.Items
{
    /// <summary>
    /// Lookup table for item definitions. Populated at game start from config.
    /// The simulation layer's equivalent of a database — all systems reference items through this.
    /// </summary>
    public class ItemRegistry
    {
        private readonly Dictionary<string, ItemDefinition> _items = new();

        public void Register(ItemDefinition def)
        {
            _items[def.Id] = def;
        }

        public ItemDefinition Get(string id)
        {
            return _items.TryGetValue(id, out var def) ? def : null;
        }

        public bool Has(string id) => _items.ContainsKey(id);

        public IEnumerable<ItemDefinition> AllItemDefinitions => _items.Values;
    }
}
