using System.Collections.Generic;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// Everything a combat condition needs to evaluate against.
    /// Created once per runner per evaluation, not stored long-term.
    /// </summary>
    public struct CombatEvaluationContext
    {
        public Runner Runner;
        public EncounterState Encounter;
        public GameState GameState;
        public SimulationConfig Config;

        public CombatEvaluationContext(Runner runner, EncounterState encounter,
            GameState gameState, SimulationConfig config)
        {
            Runner = runner;
            Encounter = encounter;
            GameState = gameState;
            Config = config;
        }
    }

    /// <summary>
    /// Static evaluator for combat style rules. No side effects: reads state and returns results.
    /// Follows the same first-match-wins pattern as RuleEvaluator.
    /// </summary>
    public static class CombatStyleEvaluator
    {
        /// <summary>
        /// Evaluate targeting rules to select a target. Returns the resolved EnemyInstance
        /// (for enemy targeting) or null if no rule matched or no valid target exists.
        /// For ally-targeting rules (healer), returns null for enemy targeting;
        /// use EvaluateTargetingForAlly for ally resolution.
        /// </summary>
        public static EnemyInstance EvaluateTargeting(CombatStyle style, CombatEvaluationContext ctx)
        {
            if (style == null || style.TargetingRules == null) return null;
            if (ctx.Encounter == null) return null;

            for (int i = 0; i < style.TargetingRules.Count; i++)
            {
                var rule = style.TargetingRules[i];
                if (!rule.Enabled) continue;

                if (!EvaluateAllConditions(rule.Conditions, ctx))
                    continue;

                // Rule matched: resolve the TargetSelection to a concrete enemy
                var target = ResolveEnemyTarget(rule.Selection, ctx);
                if (target != null)
                    return target;

                // Selection is ally-targeting (NearestAlly, LowestHpAlly): not an enemy target.
                // Or no alive enemies matched. Either way, continue to next rule.
            }
            return null;
        }

        /// <summary>
        /// Evaluate targeting rules to select an ally runner (for healing).
        /// Returns the resolved Runner or null.
        /// </summary>
        public static Runner EvaluateTargetingForAlly(CombatStyle style, CombatEvaluationContext ctx)
        {
            if (style == null || style.TargetingRules == null) return null;

            for (int i = 0; i < style.TargetingRules.Count; i++)
            {
                var rule = style.TargetingRules[i];
                if (!rule.Enabled) continue;

                if (!EvaluateAllConditions(rule.Conditions, ctx))
                    continue;

                var ally = ResolveAllyTarget(rule.Selection, ctx);
                if (ally != null)
                    return ally;
            }
            return null;
        }

        /// <summary>
        /// Evaluate ability rules to select an ability. Returns the AbilityConfig
        /// for the first matching rule where the runner can actually use the ability,
        /// or null if no usable ability found.
        /// When interruptOnly is true, only rules with CanInterrupt=true are considered.
        /// </summary>
        public static AbilityConfig EvaluateAbility(CombatStyle style, CombatEvaluationContext ctx,
            AbilityConfig[] abilityDefinitions, bool interruptOnly = false)
        {
            if (style == null || style.AbilityRules == null) return null;
            if (abilityDefinitions == null) return null;

            for (int i = 0; i < style.AbilityRules.Count; i++)
            {
                var rule = style.AbilityRules[i];
                if (!rule.Enabled) continue;
                if (interruptOnly && !rule.CanInterrupt) continue;

                if (!EvaluateAllConditions(rule.Conditions, ctx))
                    continue;

                // Rule conditions matched: check if the runner can actually use this ability
                var ability = FindAbility(rule.AbilityId, abilityDefinitions);
                if (ability == null) continue;

                if (!CanUseAbility(ctx.Runner, ability, ctx.Config))
                    continue;

                return ability;
            }
            return null;
        }

        /// <summary>
        /// Check if a runner can currently use an ability (level, cooldown, mana).
        /// Uses inherent level (base * passion), NOT equipment-boosted.
        /// </summary>
        public static bool CanUseAbility(Runner runner, AbilityConfig ability, SimulationConfig config)
        {
            // UnlockLevel check: use inherent level (base * passion multiplier)
            if (ability.UnlockLevel > 0)
            {
                float inherentLevel = runner.GetEffectiveLevel(ability.SkillType, config);
                if (inherentLevel < ability.UnlockLevel)
                    return false;
            }

            // Cooldown check
            if (runner.Fighting != null &&
                runner.Fighting.CooldownTrackers.TryGetValue(ability.Id, out int cd) && cd > 0)
                return false;

            // Mana check
            if (ability.ManaCost > 0f && runner.CurrentMana < ability.ManaCost)
                return false;

            return true;
        }

        /// <summary>
        /// Evaluate a single combat condition against the context.
        /// </summary>
        public static bool EvaluateCombatCondition(CombatCondition condition, CombatEvaluationContext ctx)
        {
            switch (condition.Type)
            {
                case CombatConditionType.Always:
                    return true;

                case CombatConditionType.SelfHpPercent:
                {
                    if (ctx.Runner.CurrentHitpoints < 0f) return false;
                    float maxHp = CombatFormulas.CalculateMaxHitpoints(
                        ctx.Runner.GetEffectiveLevel(SkillType.Hitpoints, ctx.Config), ctx.Config);
                    float hpPercent = maxHp > 0f ? (ctx.Runner.CurrentHitpoints / maxHp) * 100f : 0f;
                    return RuleEvaluator.Compare(hpPercent, condition.Operator, condition.NumericValue);
                }

                case CombatConditionType.SelfManaPercent:
                {
                    if (ctx.Runner.CurrentMana < 0f) return false;
                    float maxMana = CombatFormulas.CalculateMaxMana(
                        ctx.Runner.GetEffectiveLevel(SkillType.Restoration, ctx.Config), ctx.Config);
                    float manaPercent = maxMana > 0f ? (ctx.Runner.CurrentMana / maxMana) * 100f : 0f;
                    return RuleEvaluator.Compare(manaPercent, condition.Operator, condition.NumericValue);
                }

                case CombatConditionType.TargetHpPercent:
                {
                    // Evaluate against the runner's current target
                    if (ctx.Runner.Fighting == null) return false;
                    if (ctx.Encounter == null) return false;
                    var target = ctx.Encounter.FindEnemy(ctx.Runner.Fighting.CurrentTargetEnemyId);
                    if (target == null || !target.IsAlive) return false;
                    var targetDef = FindEnemyConfig(target.ConfigId, ctx.Config);
                    if (targetDef == null) return false;
                    float targetHpPercent = targetDef.MaxHitpoints > 0f
                        ? (target.CurrentHp / targetDef.MaxHitpoints) * 100f : 0f;
                    return RuleEvaluator.Compare(targetHpPercent, condition.Operator, condition.NumericValue);
                }

                case CombatConditionType.EnemyCountAtNode:
                {
                    int count = 0;
                    if (ctx.Encounter != null)
                    {
                        foreach (var enemy in ctx.Encounter.Enemies)
                            if (enemy.IsAlive) count++;
                    }
                    return RuleEvaluator.Compare(count, condition.Operator, condition.NumericValue);
                }

                case CombatConditionType.AllyCountAtNode:
                {
                    string nodeId = ctx.Runner.CurrentNodeId;
                    int count = 0;
                    foreach (var r in ctx.GameState.Runners)
                    {
                        if (r.CurrentNodeId == nodeId && r.Id != ctx.Runner.Id)
                            count++;
                    }
                    return RuleEvaluator.Compare(count, condition.Operator, condition.NumericValue);
                }

                case CombatConditionType.AlliesInCombatAtNode:
                {
                    string nodeId = ctx.Runner.CurrentNodeId;
                    int count = 0;
                    foreach (var r in ctx.GameState.Runners)
                    {
                        if (r.Id != ctx.Runner.Id
                            && r.State == RunnerState.Fighting
                            && r.Fighting?.NodeId == nodeId)
                            count++;
                    }
                    return RuleEvaluator.Compare(count, condition.Operator, condition.NumericValue);
                }

                case CombatConditionType.AbilityOffCooldown:
                {
                    if (string.IsNullOrEmpty(condition.StringParam)) return true;
                    if (ctx.Runner.Fighting == null) return true;
                    return !ctx.Runner.Fighting.CooldownTrackers.ContainsKey(condition.StringParam);
                }

                case CombatConditionType.EnemyIsCasting:
                    return false; // stub for future

                default:
                    return false;
            }
        }

        // ─── Private Helpers ──────────────────────────────────────

        private static bool EvaluateAllConditions(List<CombatCondition> conditions, CombatEvaluationContext ctx)
        {
            if (conditions == null || conditions.Count == 0)
                return true;

            for (int i = 0; i < conditions.Count; i++)
            {
                if (!EvaluateCombatCondition(conditions[i], ctx))
                    return false;
            }
            return true;
        }

        private static EnemyInstance ResolveEnemyTarget(TargetSelection selection, CombatEvaluationContext ctx)
        {
            if (ctx.Encounter == null) return null;

            switch (selection)
            {
                case TargetSelection.NearestEnemy:
                    // No positional data yet: first alive enemy (same as Batch B)
                    foreach (var enemy in ctx.Encounter.Enemies)
                        if (enemy.IsAlive) return enemy;
                    return null;

                case TargetSelection.LowestHpEnemy:
                {
                    EnemyInstance lowest = null;
                    foreach (var enemy in ctx.Encounter.Enemies)
                    {
                        if (!enemy.IsAlive) continue;
                        if (lowest == null || enemy.CurrentHp < lowest.CurrentHp)
                            lowest = enemy;
                    }
                    return lowest;
                }

                case TargetSelection.HighestHpEnemy:
                {
                    EnemyInstance highest = null;
                    foreach (var enemy in ctx.Encounter.Enemies)
                    {
                        if (!enemy.IsAlive) continue;
                        if (highest == null || enemy.CurrentHp > highest.CurrentHp)
                            highest = enemy;
                    }
                    return highest;
                }

                // Ally selections don't resolve to an enemy
                case TargetSelection.NearestAlly:
                case TargetSelection.LowestHpAlly:
                    return null;

                default:
                    return null;
            }
        }

        private static Runner ResolveAllyTarget(TargetSelection selection, CombatEvaluationContext ctx)
        {
            string nodeId = ctx.Runner.CurrentNodeId;

            switch (selection)
            {
                case TargetSelection.NearestAlly:
                    // No positional data: first other runner at the same node
                    foreach (var r in ctx.GameState.Runners)
                    {
                        if (r.Id != ctx.Runner.Id && r.CurrentNodeId == nodeId
                            && r.State == RunnerState.Fighting)
                            return r;
                    }
                    return null;

                case TargetSelection.LowestHpAlly:
                {
                    Runner lowest = null;
                    float lowestHp = float.MaxValue;
                    foreach (var r in ctx.GameState.Runners)
                    {
                        if (r.CurrentNodeId != nodeId) continue;
                        if (r.State != RunnerState.Fighting && r.Id != ctx.Runner.Id) continue;
                        if (r.CurrentHitpoints < 0f) continue;
                        float maxHp = CombatFormulas.CalculateMaxHitpoints(
                            r.GetEffectiveLevel(SkillType.Hitpoints, ctx.Config), ctx.Config);
                        float hpPercent = maxHp > 0f ? r.CurrentHitpoints / maxHp : 1f;
                        if (hpPercent < lowestHp)
                        {
                            lowestHp = hpPercent;
                            lowest = r;
                        }
                    }
                    return lowest;
                }

                default:
                    return null;
            }
        }

        private static AbilityConfig FindAbility(string id, AbilityConfig[] definitions)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < definitions.Length; i++)
            {
                if (definitions[i].Id == id)
                    return definitions[i];
            }
            return null;
        }

        private static EnemyConfig FindEnemyConfig(string id, SimulationConfig config)
        {
            if (config.EnemyDefinitions == null) return null;
            for (int i = 0; i < config.EnemyDefinitions.Length; i++)
            {
                if (config.EnemyDefinitions[i].Id == id)
                    return config.EnemyDefinitions[i];
            }
            return null;
        }
    }
}
