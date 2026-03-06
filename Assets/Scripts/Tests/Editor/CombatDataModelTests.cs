using System.Collections.Generic;
using NUnit.Framework;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for combat data model: config classes, runner combat fields,
    /// encounter state, combat formulas.
    /// </summary>
    [TestFixture]
    public class CombatDataModelTests
    {
        [Test]
        public void AbilityConfigCreatesWithDefaults()
        {
            var ability = new AbilityConfig
            {
                Id = "fireball",
                Name = "Fireball",
                SkillType = SkillType.Magic,
                ActionTimeTicks = 20,
                CooldownTicks = 0,
                ManaCost = 0,
                UnlockLevel = 1,
            };
            ability.Effects.Add(new AbilityEffect(EffectType.Damage, 15f, SkillType.Magic, 1.0f));

            Assert.AreEqual("fireball", ability.Id);
            Assert.AreEqual(20, ability.ActionTimeTicks);
            Assert.AreEqual(1, ability.Effects.Count);
            Assert.AreEqual(EffectType.Damage, ability.Effects[0].Type);
            Assert.AreEqual(15f, ability.Effects[0].BaseValue);
        }

        [Test]
        public void EnemyConfigCreatesWithLootTable()
        {
            var enemy = new EnemyConfig
            {
                Id = "goblin_grunt",
                Name = "Goblin Grunt",
                Level = 3,
                MaxHitpoints = 80f,
                BaseDamage = 5f,
                AttackSpeedTicks = 15,
                AiBehavior = EnemyAiBehavior.Aggressive,
            };
            enemy.LootTable.Add(new LootTableEntry("goblin_tooth", 0.5f, 1, 2));

            Assert.AreEqual("goblin_grunt", enemy.Id);
            Assert.AreEqual(3, enemy.Level);
            Assert.AreEqual(80f, enemy.MaxHitpoints);
            Assert.AreEqual(1, enemy.LootTable.Count);
            Assert.AreEqual(0.5f, enemy.LootTable[0].DropChance);
        }

        [Test]
        public void EnemyInstanceTracksAliveState()
        {
            var instance = new EnemyInstance
            {
                InstanceId = "enemy-1",
                ConfigId = "goblin_grunt",
                CurrentHp = 80f,
            };

            Assert.IsTrue(instance.IsAlive);
            Assert.IsFalse(instance.IsActing);

            instance.CurrentHp = 0f;
            Assert.IsFalse(instance.IsAlive);
        }

        [Test]
        public void EncounterStateFindAndListEnemies()
        {
            var encounter = new EncounterState("goblin_camp");
            encounter.Enemies.Add(new EnemyInstance
            {
                InstanceId = "e1", ConfigId = "goblin_grunt", CurrentHp = 80f,
            });
            encounter.Enemies.Add(new EnemyInstance
            {
                InstanceId = "e2", ConfigId = "goblin_grunt", CurrentHp = 0f,
            });
            encounter.Enemies.Add(new EnemyInstance
            {
                InstanceId = "e3", ConfigId = "goblin_shaman", CurrentHp = 60f,
            });

            var alive = encounter.GetAliveEnemies();
            Assert.AreEqual(2, alive.Count);

            var found = encounter.FindEnemy("e2");
            Assert.IsNotNull(found);
            Assert.AreEqual("goblin_grunt", found.ConfigId);
            Assert.IsFalse(found.IsAlive);

            Assert.IsNull(encounter.FindEnemy("nonexistent"));
        }

        [Test]
        public void RunnerMaxHitpointsScalesWithSkillLevel()
        {
            var config = new SimulationConfig
            {
                BaseHitpoints = 50f,
                HitpointsPerLevel = 5f,
            };

            // Level 1: 50 + (1 - 1) * 5 = 50
            float hp1 = CombatFormulas.CalculateMaxHitpoints(1f, config);
            Assert.AreEqual(50f, hp1, 0.01f);

            // Level 10: 50 + (10 - 1) * 5 = 95
            float hp10 = CombatFormulas.CalculateMaxHitpoints(10f, config);
            Assert.AreEqual(95f, hp10, 0.01f);
        }

        [Test]
        public void RunnerMaxManaScalesWithRestorationLevel()
        {
            var config = new SimulationConfig
            {
                BaseMana = 50f,
                ManaPerRestorationLevel = 3f,
            };

            float mana1 = CombatFormulas.CalculateMaxMana(1f, config);
            Assert.AreEqual(50f, mana1, 0.01f);

            float mana10 = CombatFormulas.CalculateMaxMana(10f, config);
            Assert.AreEqual(77f, mana10, 0.01f);
        }

        [Test]
        public void DamageFormulaAppliesScaling()
        {
            var config = new SimulationConfig
            {
                CombatDamageScalingPerLevel = 0.1f,
                MaxDefenceReductionPercent = 75f,
            };

            var effect = new AbilityEffect(EffectType.Damage, 10f, SkillType.Melee, 1.0f);

            // Attacker level 5, no defence
            float damage = CombatFormulas.CalculateDamage(effect, 5f, 0f, config);
            // 10 * 1.0 * (1 + 5 * 0.1) = 10 * 1.5 = 15
            Assert.AreEqual(15f, damage, 0.01f);

            // Attacker level 5, 5 defence
            float reducedDamage = CombatFormulas.CalculateDamage(effect, 5f, 5f, config);
            // Raw = 15, reduction = min(5, 15 * 0.75) = 5, result = 10
            Assert.AreEqual(10f, reducedDamage, 0.01f);
        }

        [Test]
        public void DamageNeverBelowOne()
        {
            var config = new SimulationConfig
            {
                CombatDamageScalingPerLevel = 0.1f,
                MaxDefenceReductionPercent = 75f,
            };

            var effect = new AbilityEffect(EffectType.Damage, 1f, SkillType.Melee, 0.1f);

            // Very weak attack, high defence
            float damage = CombatFormulas.CalculateDamage(effect, 1f, 1000f, config);
            Assert.AreEqual(1f, damage, 0.01f, "Damage should never be below 1");
        }

        [Test]
        public void DefenceReductionCappedAtMaxPercent()
        {
            var config = new SimulationConfig
            {
                CombatDamageScalingPerLevel = 0.1f,
                MaxDefenceReductionPercent = 75f,
            };

            var effect = new AbilityEffect(EffectType.Damage, 10f, SkillType.Melee, 1.0f);

            // Attacker level 1, enormous defence
            float damage = CombatFormulas.CalculateDamage(effect, 1f, 10000f, config);
            // Raw = 10 * 1.0 * (1 + 1 * 0.1) = 11
            // Reduction capped at 75% of 11 = 8.25
            // Result = 11 - 8.25 = 2.75
            Assert.AreEqual(2.75f, damage, 0.01f);
        }

        [Test]
        public void HealFormulaAppliesScaling()
        {
            var config = new SimulationConfig
            {
                CombatDamageScalingPerLevel = 0.1f,
            };

            var effect = new AbilityEffect(EffectType.Heal, 20f, SkillType.Restoration, 1.0f);

            float heal = CombatFormulas.CalculateHeal(effect, 5f, config);
            // 20 * 1.0 * (1 + 5 * 0.1) = 20 * 1.5 = 30
            Assert.AreEqual(30f, heal, 0.01f);
        }

        [Test]
        public void CombatXpScalesWithActionTime()
        {
            var config = new SimulationConfig
            {
                CombatXpPerActionTimeTick = 1.0f,
            };

            float xp10 = CombatFormulas.CalculateCombatXp(10, config);
            Assert.AreEqual(10f, xp10, 0.01f);

            float xp20 = CombatFormulas.CalculateCombatXp(20, config);
            Assert.AreEqual(20f, xp20, 0.01f);
        }

        [Test]
        public void EffectConditionTargetHpBelowPercent()
        {
            var condition = new AbilityEffectCondition(AbilityEffectConditionType.TargetHpBelowPercent, 35f);

            // Target at 30% HP: should be true
            Assert.IsTrue(CombatFormulas.IsEffectConditionMet(condition, 30f, 100f, false));

            // Target at 40% HP: should be false
            Assert.IsFalse(CombatFormulas.IsEffectConditionMet(condition, 40f, 100f, false));

            // Target at exactly 35% HP: should be false (< not <=)
            Assert.IsFalse(CombatFormulas.IsEffectConditionMet(condition, 35f, 100f, false));
        }

        [Test]
        public void EffectConditionNullMeansAlwaysTrue()
        {
            Assert.IsTrue(CombatFormulas.IsEffectConditionMet(null, 50f, 100f, false));
        }

        [Test]
        public void EffectConditionIsKillingBlow()
        {
            var condition = new AbilityEffectCondition(AbilityEffectConditionType.IsKillingBlow, 0f);

            Assert.IsTrue(CombatFormulas.IsEffectConditionMet(condition, 0f, 100f, true));
            Assert.IsFalse(CombatFormulas.IsEffectConditionMet(condition, 0f, 100f, false));
        }

        [Test]
        public void RunnerCombatFieldsInitializeCorrectly()
        {
            var runner = new Runner();

            Assert.AreEqual(-1f, runner.CurrentHitpoints, "HP should be uninitialized (-1)");
            Assert.AreEqual(-1f, runner.CurrentMana, "Mana should be uninitialized (-1)");
            Assert.IsNull(runner.CombatStyleId, "No combat style by default");
            Assert.IsNull(runner.Fighting, "Not in fighting state");
            Assert.IsNull(runner.Death, "Not in death state");
        }

        [Test]
        public void FightingStateTracksCombatProgress()
        {
            var fighting = new FightingState
            {
                NodeId = "goblin_camp",
                CurrentAbilityId = "fireball",
                ActionTicksRemaining = 10,
                ActionTicksTotal = 20,
            };

            Assert.IsTrue(fighting.IsActing);
            Assert.AreEqual(0.5f, fighting.ActionProgress, 0.01f);

            fighting.ActionTicksRemaining = 0;
            Assert.IsFalse(fighting.IsActing);
            Assert.AreEqual(1.0f, fighting.ActionProgress, 0.01f);
        }

        [Test]
        public void EnemySpawnEntryDefaults()
        {
            var spawn = new EnemySpawnEntry("goblin_grunt", 3, 100, 30);

            Assert.AreEqual("goblin_grunt", spawn.EnemyConfigId);
            Assert.AreEqual(3, spawn.InitialCount);
            Assert.AreEqual(100, spawn.RespawnTimeTicks);
            Assert.AreEqual(30, spawn.MaxCount);
        }

        [Test]
        public void CombatStyleDeepCopyCreatesNewId()
        {
            var style = new CombatStyle
            {
                Id = "original-id",
                Name = "Tank",
            };
            style.TargetingRules.Add(new TargetingRule
            {
                Selection = TargetSelection.NearestEnemy,
                Label = "Target nearest",
            });
            style.AbilityRules.Add(new AbilityRule
            {
                AbilityId = "basic_attack",
                Label = "Basic Attack",
                CanInterrupt = true,
            });

            var copy = style.DeepCopy();

            Assert.AreNotEqual(style.Id, copy.Id, "Deep copy should generate a new Id");
            Assert.AreEqual("Tank", copy.Name);
            Assert.AreEqual(1, copy.TargetingRules.Count);
            Assert.AreEqual(TargetSelection.NearestEnemy, copy.TargetingRules[0].Selection);
            Assert.AreEqual(1, copy.AbilityRules.Count);
            Assert.AreEqual("basic_attack", copy.AbilityRules[0].AbilityId);
            Assert.IsTrue(copy.AbilityRules[0].CanInterrupt);

            // Verify deep copy (not shared reference)
            copy.TargetingRules[0].Label = "Modified";
            Assert.AreEqual("Target nearest", style.TargetingRules[0].Label,
                "Modifying copy should not affect original");
        }

        [Test]
        public void WorldNodeHasEnemySpawns()
        {
            var map = new Simulation.World.WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("goblin_camp", "Goblin Camp", -25f, 30f, "Node_GoblinCamp",
                System.Array.Empty<Simulation.Gathering.GatherableConfig>(),
                new[]
                {
                    new EnemySpawnEntry("goblin_grunt", 3, 100),
                    new EnemySpawnEntry("goblin_shaman", 1, 200),
                });
            map.Initialize();

            var node = map.GetNode("goblin_camp");
            Assert.IsNotNull(node);
            Assert.AreEqual(2, node.EnemySpawns.Length);
            Assert.AreEqual("goblin_grunt", node.EnemySpawns[0].EnemyConfigId);
            Assert.AreEqual(3, node.EnemySpawns[0].InitialCount);
        }

        [Test]
        public void GameStateHasCombatStyleLibrary()
        {
            var state = new GameState();
            Assert.IsNotNull(state.CombatStyleLibrary);
            Assert.AreEqual(0, state.CombatStyleLibrary.Count);
            Assert.IsNotNull(state.EncounterStates);
            Assert.AreEqual(0, state.EncounterStates.Count);
        }

        [Test]
        public void FightHereActionType()
        {
            var action = Simulation.Automation.AutomationAction.FightHere();
            Assert.AreEqual(Simulation.Automation.ActionType.FightHere, action.Type);
        }

        [Test]
        public void RespawnTimeFormula()
        {
            var config = new SimulationConfig
            {
                DeathRespawnBaseTime = 10f,
                DeathRespawnTravelMultiplier = 1.2f,
            };

            // 20 second travel time to hub
            float respawn = CombatFormulas.CalculateRespawnTime(20f, config);
            // 10 + 20 * 1.2 = 10 + 24 = 34
            Assert.AreEqual(34f, respawn, 0.01f);

            // 0 travel time (died at hub)
            float respawnAtHub = CombatFormulas.CalculateRespawnTime(0f, config);
            Assert.AreEqual(10f, respawnAtHub, 0.01f);
        }

        [Test]
        public void RunnerDefenceCalculation()
        {
            var config = new SimulationConfig
            {
                DefenceReductionPerLevel = 0.5f,
            };

            float def10 = CombatFormulas.CalculateRunnerDefence(10f, config);
            Assert.AreEqual(5f, def10, 0.01f);
        }

        [Test]
        public void SelfHpConditionWorksWithCombatHp()
        {
            var config = new SimulationConfig
            {
                BaseHitpoints = 100f,
                HitpointsPerLevel = 0f, // simple: 100 HP at all levels
            };
            var runner = new Runner();
            runner.CurrentHitpoints = 30f; // 30% HP

            var state = new GameState();
            var ctx = new Simulation.Automation.EvaluationContext(runner, state, config);

            // HP < 50%: should be true
            var condBelow50 = Simulation.Automation.Condition.SelfHP(
                Simulation.Automation.ComparisonOperator.LessThan, 50f);
            Assert.IsTrue(Simulation.Automation.RuleEvaluator.EvaluateCondition(condBelow50, ctx));

            // HP > 50%: should be false
            var condAbove50 = Simulation.Automation.Condition.SelfHP(
                Simulation.Automation.ComparisonOperator.GreaterThan, 50f);
            Assert.IsFalse(Simulation.Automation.RuleEvaluator.EvaluateCondition(condAbove50, ctx));
        }

        [Test]
        public void SelfHpConditionReturnsFalseWhenHpUninitialized()
        {
            var config = new SimulationConfig();
            var runner = new Runner(); // HP = -1 (uninitialized)
            var state = new GameState();
            var ctx = new Simulation.Automation.EvaluationContext(runner, state, config);

            var condition = Simulation.Automation.Condition.SelfHP(
                Simulation.Automation.ComparisonOperator.LessThan, 50f);
            Assert.IsFalse(Simulation.Automation.RuleEvaluator.EvaluateCondition(condition, ctx),
                "SelfHP should return false when HP is uninitialized");
        }

        [Test]
        public void CullingFrostEffectConditionDesign()
        {
            // Verify the Culling Frost design: 0.4x normally, 2.5x if target < 35% HP
            var normalEffect = new AbilityEffect(EffectType.Damage, 15f, SkillType.Magic, 0.4f);
            var executeEffect = new AbilityEffect(EffectType.Damage, 15f, SkillType.Magic, 2.5f)
            {
                Condition = new AbilityEffectCondition(AbilityEffectConditionType.TargetHpBelowPercent, 35f),
            };

            var config = new SimulationConfig { CombatDamageScalingPerLevel = 0.1f, MaxDefenceReductionPercent = 75f };

            // Normal hit at Magic 8 (no defence)
            float normalDmg = CombatFormulas.CalculateDamage(normalEffect, 8f, 0f, config);
            // 15 * 0.4 * (1 + 8 * 0.1) = 6 * 1.8 = 10.8
            Assert.AreEqual(10.8f, normalDmg, 0.1f);

            // Execute hit at Magic 8 (no defence)
            float execDmg = CombatFormulas.CalculateDamage(executeEffect, 8f, 0f, config);
            // 15 * 2.5 * (1 + 8 * 0.1) = 37.5 * 1.8 = 67.5
            Assert.AreEqual(67.5f, execDmg, 0.1f);

            // Execute condition check
            Assert.IsTrue(CombatFormulas.IsEffectConditionMet(executeEffect.Condition, 30f, 100f, false),
                "Culling Frost execute should trigger at 30% HP");
            Assert.IsFalse(CombatFormulas.IsEffectConditionMet(executeEffect.Condition, 40f, 100f, false),
                "Culling Frost execute should NOT trigger at 40% HP");
        }
    }
}
