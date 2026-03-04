using UnityEngine;

namespace ProjectGuild.View
{
    /// <summary>
    /// Specialized NodeSceneRoot for the Guild Hall (hub) scene.
    /// Adds hub-specific fields like the deposit point (bank/warehouse).
    ///
    /// The Guild Hall is the player's base of operations — a settlement with
    /// multiple buildings. Future hub-specific features (crafting stations,
    /// NPC positions, etc.) belong here rather than on the base NodeSceneRoot.
    ///
    /// In the Unity scene, replace the NodeSceneRoot component on the Guild Hall
    /// root with this component. All inherited fields (spawn points, directional
    /// spawns, gathering spots) still work — this just adds hub extras.
    /// </summary>
    public class GuildHallSceneRoot : NodeSceneRoot
    {
        [Header("Guild Hall — Deposit")]
        [Tooltip("Where runners walk to during the Deposit step. " +
            "Place near the bank/warehouse building.")]
        [SerializeField] private Transform _depositPoint;

        public override Vector3? DepositPointPosition =>
            _depositPoint != null ? _depositPoint.position : null;
    }
}
