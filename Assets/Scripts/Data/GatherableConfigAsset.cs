using UnityEngine;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Gathering;

namespace ProjectGuild.Data
{
    /// <summary>
    /// ScriptableObject for authoring a gatherable resource in the Unity inspector.
    /// Each gatherable (copper ore at the mine, pine logs in the forest, etc.) gets its own asset.
    /// References an ItemDefinitionAsset for type safety — the simulation layer
    /// receives a plain C# GatherableConfig with string IDs at runtime.
    ///
    /// Gatherables are assigned to WorldNodes (not globally) — a node can have multiple
    /// gatherables with different level requirements.
    /// </summary>
    [CreateAssetMenu(fileName = "New Gatherable", menuName = "Project Guild/Gatherable Config")]
    public class GatherableConfigAsset : ScriptableObject
    {
        [Tooltip("The item produced when this resource is gathered. Drag an ItemDefinitionAsset here.")]
        public ItemDefinitionAsset ProducedItem;

        [Tooltip("Which skill is required and trained by gathering this resource.")]
        public SkillType RequiredSkill;

        [Tooltip("Base number of ticks to gather one item at the minimum level with no bonuses.\n" +
            "At 10 ticks/sec: 40 ticks = 4 seconds per item.\n\n" +
            "Tuning guide (PowerCurve formula):\n" +
            "Higher-level runners gather faster, so higher-tier resources need more base ticks\n" +
            "to feel the same speed at their minimum level.\n\n" +
            "To match a previous tier's feel:\n" +
            "  BaseTicks = DesiredSecondsPerItem × TickRate × MinLevel ^ Exponent\n\n" +
            "Example: you want 4s/item, tick rate is 10, MinLevel is 15, exponent is 0.55:\n" +
            "  4 × 10 × 15^0.55 = 177")]
        public float BaseTicksToGather = 40f;

        [Tooltip("XP awarded every tick while gathering this resource (decoupled from item production speed).\n" +
            "Higher tier resources should give more XP/tick to incentivize progression.")]
        public float XpPerTick = 0.5f;

        [Tooltip("Minimum skill level required to gather this resource. 0 = no requirement.")]
        public int MinLevel = 0;

        /// <summary>
        /// Convert to the plain C# GatherableConfig used by the simulation.
        /// Reads the item ID from the referenced ItemDefinitionAsset.
        /// </summary>
        public GatherableConfig ToGatherableConfig()
        {
            string itemId = ProducedItem != null ? ProducedItem.Id : "";
            return new GatherableConfig(itemId, RequiredSkill, BaseTicksToGather, XpPerTick, MinLevel);
        }
    }
}
