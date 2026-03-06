using System;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// What an ability does when it resolves. An ability can have multiple effects
    /// (e.g. Bloodthirst: Damage + HealSelf on kill).
    /// </summary>
    public enum EffectType
    {
        Damage,
        Heal,
        HealSelf,
        Taunt,
        TauntAll,
        DamageAoe,
        HealAoe,
    }

    /// <summary>
    /// A single effect within an ability. BaseValue is the flat amount before scaling.
    /// ScalingStat determines which skill level amplifies the effect.
    /// ScalingFactor is the multiplier on the base (e.g. 0.7 for Bloodthirst's reduced damage).
    /// </summary>
    [Serializable]
    public class AbilityEffect
    {
        public EffectType Type;
        public float BaseValue = 10f;
        public Core.SkillType ScalingStat;
        public float ScalingFactor = 1.0f;

        /// <summary>
        /// Conditional modifier: effect only applies when this condition is met.
        /// Null or empty means always applies.
        /// Used by Culling Frost: "2.5x if target below 35% HP".
        /// </summary>
        public AbilityEffectCondition Condition;

        public AbilityEffect() { }

        public AbilityEffect(EffectType type, float baseValue, Core.SkillType scalingStat,
            float scalingFactor = 1.0f)
        {
            Type = type;
            BaseValue = baseValue;
            ScalingStat = scalingStat;
            ScalingFactor = scalingFactor;
        }
    }

    /// <summary>
    /// Optional condition on an ability effect (e.g. "if target HP below threshold").
    /// </summary>
    [Serializable]
    public class AbilityEffectCondition
    {
        public AbilityEffectConditionType Type;
        public float Threshold;

        public AbilityEffectCondition() { }

        public AbilityEffectCondition(AbilityEffectConditionType type, float threshold)
        {
            Type = type;
            Threshold = threshold;
        }
    }

    public enum AbilityEffectConditionType
    {
        TargetHpBelowPercent,
        TargetHpAbovePercent,
        IsKillingBlow,
    }
}
