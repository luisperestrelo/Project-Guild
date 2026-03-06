using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// Template definition of an ability. Immutable at runtime (authored via SO or code).
    /// Abilities are unlocked by skill level and optionally require a weapon type.
    /// </summary>
    [Serializable]
    public class AbilityConfig
    {
        /// <summary>
        /// Unique identifier (e.g. "basic_attack", "fireball", "culling_frost").
        /// </summary>
        public string Id;

        /// <summary>
        /// Player-facing display name.
        /// </summary>
        public string Name;

        /// <summary>
        /// Which skill governs this ability's unlock level and scaling.
        /// </summary>
        public SkillType SkillType;

        /// <summary>
        /// How many ticks the ability takes to resolve (action commitment window).
        /// At 10 ticks/sec: 10 = 1 second, 20 = 2 seconds.
        /// </summary>
        public int ActionTimeTicks = 10;

        /// <summary>
        /// Ticks before the ability can be used again after resolving. 0 = spammable.
        /// </summary>
        public int CooldownTicks;

        /// <summary>
        /// Mana cost. Only Restoration abilities cost mana (by design).
        /// </summary>
        public float ManaCost;

        /// <summary>
        /// Max range in meters. 0 = melee range (must be adjacent).
        /// </summary>
        public float Range;

        /// <summary>
        /// Skill level required to unlock this ability. 0 = always available.
        /// Uses inherent level (base * passion), NOT equipment-boosted.
        /// </summary>
        public int UnlockLevel;

        /// <summary>
        /// Optional weapon requirement (e.g. "sword", "staff"). Null = no requirement.
        /// Deferred until Phase 7 (Equipment). For now, all abilities are usable without weapons.
        /// </summary>
        public string WeaponRequirement;

        /// <summary>
        /// What happens when the ability resolves. Multiple effects per ability are supported.
        /// </summary>
        public List<AbilityEffect> Effects = new();

        /// <summary>
        /// Player-facing description for the ability browser tooltip.
        /// </summary>
        public string Description;

        public AbilityConfig() { }
    }
}
