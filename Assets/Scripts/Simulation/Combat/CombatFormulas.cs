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
        /// Defence is a % reduction capped at MaxDefenceReductionPercent.
        /// </summary>
        public static float CalculateDamage(AbilityEffect effect, float attackerEffectiveLevel,
            float defenderDefence, SimulationConfig config)
        {
            float raw = effect.BaseValue * effect.ScalingFactor
                * (1f + attackerEffectiveLevel * config.CombatDamageScalingPerLevel);

            float reductionPercent = Math.Min(defenderDefence, config.MaxDefenceReductionPercent);
            return raw * (1f - reductionPercent / 100f);
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
        /// Calculate defence as a percentage damage reduction from Defence skill level.
        /// Higher levels give more %, capped at MaxDefenceReductionPercent.
        /// Effective level can exceed 99 via Passion, Equipment, and Buffs.
        /// </summary>
        public static float CalculateRunnerDefence(float defenceLevel, SimulationConfig config)
        {
            return Math.Min(defenceLevel * config.DefenceReductionPerLevel,
                config.MaxDefenceReductionPercent);
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
        /// Calculate disengage time in ticks for a runner based on Athletics level.
        /// Higher Athletics = shorter disengage = fewer hits taken while fleeing.
        /// </summary>
        public static int CalculateDisengageTicks(float athleticsLevel, SimulationConfig config)
        {
            int baseTicks = config.BaseDisengageTimeTicks;
            float reduction = athleticsLevel * config.DisengageReductionPerAthleticsLevel;
            return Math.Max(config.MinDisengageTimeTicks, (int)(baseTicks - reduction));
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
