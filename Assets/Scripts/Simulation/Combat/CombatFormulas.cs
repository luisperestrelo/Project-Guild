using System;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// Static damage/heal/defence calculations. All tuning values come from SimulationConfig.
    /// No side effects: pure input -> output.
    /// </summary>
    public static class CombatFormulas
    {
        /// <summary>
        /// Calculate damage dealt by an ability effect.
        /// Formula: baseValue * scalingFactor * (1 + attackerLevel * scalingPerLevel)
        /// Defence reduces by flat amount capped at MaxDefenceReductionPercent.
        /// </summary>
        public static float CalculateDamage(AbilityEffect effect, float attackerEffectiveLevel,
            float defenderDefence, SimulationConfig config)
        {
            float raw = effect.BaseValue * effect.ScalingFactor
                * (1f + attackerEffectiveLevel * config.CombatDamageScalingPerLevel);

            float reduction = Math.Min(defenderDefence,
                raw * config.MaxDefenceReductionPercent / 100f);

            return Math.Max(raw - reduction, 1f); // always deal at least 1 damage
        }

        /// <summary>
        /// Calculate healing from a heal effect.
        /// Formula: baseValue * scalingFactor * (1 + healerLevel * scalingPerLevel)
        /// </summary>
        public static float CalculateHeal(AbilityEffect effect, float healerEffectiveLevel,
            SimulationConfig config)
        {
            return effect.BaseValue * effect.ScalingFactor
                * (1f + healerEffectiveLevel * config.CombatDamageScalingPerLevel);
        }

        /// <summary>
        /// Calculate max hitpoints for a runner based on Hitpoints skill level.
        /// </summary>
        public static float CalculateMaxHitpoints(float hitpointsLevel, SimulationConfig config)
        {
            return config.BaseHitpoints + (hitpointsLevel - 1f) * config.HitpointsPerLevel;
        }

        /// <summary>
        /// Calculate max mana for a runner based on Restoration skill level.
        /// </summary>
        public static float CalculateMaxMana(float restorationLevel, SimulationConfig config)
        {
            return config.BaseMana + (restorationLevel - 1f) * config.ManaPerRestorationLevel;
        }

        /// <summary>
        /// Calculate mana regenerated per tick.
        /// </summary>
        public static float CalculateManaRegenPerTick(SimulationConfig config)
        {
            return config.BaseManaRegenPerTick;
        }

        /// <summary>
        /// Calculate defence reduction from a runner's Defence skill level.
        /// Returns a flat reduction amount applied to incoming damage.
        /// </summary>
        public static float CalculateRunnerDefence(float defenceLevel, SimulationConfig config)
        {
            return defenceLevel * config.DefenceReductionPerLevel;
        }

        /// <summary>
        /// Calculate XP awarded for completing a combat ability.
        /// Based on the ability's action time (longer abilities = more XP).
        /// </summary>
        public static float CalculateCombatXp(int actionTimeTicks, SimulationConfig config)
        {
            return actionTimeTicks * config.CombatXpPerActionTimeTick;
        }

        /// <summary>
        /// Calculate respawn time for a dead runner based on distance to hub.
        /// </summary>
        public static float CalculateRespawnTime(float travelTimeToHub, SimulationConfig config)
        {
            return config.DeathRespawnBaseTime
                + travelTimeToHub * config.DeathRespawnTravelMultiplier;
        }

        /// <summary>
        /// Check if an ability effect condition is met.
        /// </summary>
        public static bool IsEffectConditionMet(AbilityEffectCondition condition,
            float targetCurrentHp, float targetMaxHp, bool isKillingBlow)
        {
            if (condition == null) return true;

            float hpPercent = targetMaxHp > 0f ? (targetCurrentHp / targetMaxHp) * 100f : 0f;

            return condition.Type switch
            {
                AbilityEffectConditionType.TargetHpBelowPercent => hpPercent < condition.Threshold,
                AbilityEffectConditionType.TargetHpAbovePercent => hpPercent > condition.Threshold,
                AbilityEffectConditionType.IsKillingBlow => isKillingBlow,
                _ => true,
            };
        }
    }
}
