using System.Collections.Generic;
using UnityEngine;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Data
{
    /// <summary>
    /// ScriptableObject for authoring an ability in the Unity inspector.
    /// Each ability (Basic Attack, Fireball, Culling Frost, etc.) gets its own asset.
    /// The simulation layer receives a plain C# AbilityConfig at runtime via ToAbilityConfig().
    /// </summary>
    [CreateAssetMenu(fileName = "New Ability", menuName = "Project Guild/Ability Config")]
    public class AbilityConfigAsset : ScriptableObject
    {
        [Tooltip("Unique identifier (e.g. 'basic_attack', 'fireball').")]
        public string Id;

        [Tooltip("Player-facing display name.")]
        public string Name;

        [Tooltip("Which skill governs this ability's unlock and scaling.")]
        public SkillType SkillType;

        [Tooltip("Ticks to cast/execute. At 10 ticks/sec: 10 = 1 second.")]
        public int ActionTimeTicks = 10;

        [Tooltip("Ticks before the ability can be used again. 0 = spammable.")]
        public int CooldownTicks;

        [Tooltip("Mana cost. Only Restoration abilities cost mana by design.")]
        public float ManaCost;

        [Tooltip("Max range in meters. 0 = melee range.")]
        public float Range;

        [Tooltip("Skill level required to unlock. 0 = always available. Uses inherent level (not equipment-boosted).")]
        public int UnlockLevel;

        [Tooltip("Optional weapon requirement (e.g. 'sword'). Leave empty for no requirement. Deferred to Phase 7.")]
        public string WeaponRequirement;

        [Tooltip("Player-facing description for the ability browser tooltip.")]
        [TextArea(2, 4)]
        public string Description;

        [Tooltip("Effects applied when the ability resolves.")]
        public AbilityEffectData[] Effects = new AbilityEffectData[0];

        public AbilityConfig ToAbilityConfig()
        {
            var config = new AbilityConfig
            {
                Id = Id,
                Name = Name,
                SkillType = SkillType,
                ActionTimeTicks = ActionTimeTicks,
                CooldownTicks = CooldownTicks,
                ManaCost = ManaCost,
                Range = Range,
                UnlockLevel = UnlockLevel,
                WeaponRequirement = string.IsNullOrEmpty(WeaponRequirement) ? null : WeaponRequirement,
                Description = Description,
            };

            foreach (var effectData in Effects)
            {
                if (effectData == null) continue;
                var effect = new AbilityEffect(
                    effectData.Type, effectData.BaseValue,
                    effectData.ScalingStat, effectData.ScalingFactor);

                if (effectData.HasCondition)
                {
                    effect.Condition = new AbilityEffectCondition(
                        effectData.ConditionType, effectData.ConditionThreshold);
                }

                config.Effects.Add(effect);
            }

            return config;
        }
    }

    /// <summary>
    /// Inspector-friendly ability effect data. Flat fields for clean inspector display.
    /// </summary>
    [System.Serializable]
    public class AbilityEffectData
    {
        [Tooltip("What this effect does (damage, heal, taunt, etc.).")]
        public EffectType Type;

        [Tooltip("Base value before scaling.")]
        public float BaseValue = 10f;

        [Tooltip("Which skill level amplifies this effect.")]
        public SkillType ScalingStat;

        [Tooltip("Multiplier on the base value (e.g. 0.7 for Bloodthirst's reduced damage).")]
        public float ScalingFactor = 1.0f;

        [Tooltip("Enable to add a conditional modifier (e.g. 'if target below 35% HP').")]
        public bool HasCondition;

        [Tooltip("Condition type for conditional effects.")]
        public AbilityEffectConditionType ConditionType;

        [Tooltip("Threshold for the condition (e.g. 35 for 'below 35% HP').")]
        public float ConditionThreshold;
    }
}
