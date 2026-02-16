using UnityEngine;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Data
{
    /// <summary>
    /// ScriptableObject for authoring an item definition in the Unity inspector.
    /// Each item type (copper ore, pine log, etc.) gets its own asset file.
    /// The simulation layer uses plain C# ItemDefinition at runtime — this is
    /// just the authoring/editor layer.
    /// </summary>
    [CreateAssetMenu(fileName = "New Item", menuName = "Project Guild/Item Definition")]
    public class ItemDefinitionAsset : ScriptableObject
    {
        [Tooltip("Unique identifier used throughout the simulation (e.g. 'copper_ore'). Must match exactly everywhere.")]
        public string Id;

        [Tooltip("Display name shown to the player (e.g. 'Copper Ore').")]
        public string Name;

        [Tooltip("Category for sorting, filtering, and UI grouping.")]
        public ItemCategory Category;

        [Tooltip("Icon shown in inventory slots, tooltips, and other UI.")]
        public Sprite Icon;

        [Tooltip("Whether this item stacks in inventory. Most raw materials will stack once we implement stacking.")]
        public bool Stackable = false;

        [Tooltip("Maximum stack size per inventory slot. Only relevant if Stackable is true.")]
        public int MaxStack = 1;

        /// <summary>
        /// Convert to the plain C# ItemDefinition used by the simulation.
        /// </summary>
        public ItemDefinition ToItemDefinition()
        {
            return new ItemDefinition(Id, Name, Category, Stackable, MaxStack);
        }
    }
}
