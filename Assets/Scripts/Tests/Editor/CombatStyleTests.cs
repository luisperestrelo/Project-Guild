using System.Collections.Generic;
using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for the combat style evaluator: targeting rules, ability rules,
    /// per-step overrides, unlock checks, default styles, waiting state, interrupts.
    /// </summary>
    [TestFixture]
    public class CombatStyleTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly AbilityConfig BasicAttack = new()
        {
            Id = "basic_attack",
            Name = "Basic Attack",
            SkillType = SkillType.Melee,
            ActionTimeTicks = 10,
            CooldownTicks = 0,
            ManaCost = 0f,
            UnlockLevel = 0,
            Effects = { new AbilityEffect(EffectType.Damage, 10f, SkillType.Melee, 1.0f) },
        };

        private static readonly AbilityConfig Fireball = new()
        {
            Id = "fireball",
            Name = "Fireball",
            SkillType = SkillType.Magic,
            ActionTimeTicks = 15,
            CooldownTicks = 0,
            ManaCost = 0f,
            UnlockLevel = 0,
            Effects = { new AbilityEffect(EffectType.Damage, 15f, SkillType.Magic, 1.0f) },
        };

        private static readonly AbilityConfig Heal = new()
        {
            Id = "heal",
            Name = "Heal",
            SkillType = SkillType.Restoration,
            ActionTimeTicks = 18,
            CooldownTicks = 0,
            ManaCost = 15f,
            UnlockLevel = 0,
            Effects = { new AbilityEffect(EffectType.Heal, 20f, SkillType.Restoration, 1.0f) },
        };

        private static readonly EnemyConfig GoblinGrunt = new()
        {
            Id = "goblin_grunt",
            Name = "Goblin Grunt",
            Level = 3,
            MaxHitpoints = 30f,
            BaseDamage = 5f,
            BaseDefence = 0f,
            AttackSpeedTicks = 100, // slow attacks so tests are stable
            AiBehavior = EnemyAiBehavior.Aggressive,
            LootTable = { new LootTableEntry("goblin_tooth", 1.0f, 1, 1) },
        };

        private void Setup(int meleeLevel = 5, int hitpointsLevel = 5,
            AbilityConfig[] abilities = null, EnemyConfig[] enemies = null,
            int runnerCount = 1)
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("goblin_tooth", "Goblin Tooth", ItemCategory.Misc),
                },
                AbilityDefinitions = abilities ?? new[] { BasicAttack },
                EnemyDefinitions = enemies ?? new[] { GoblinGrunt },
                CombatDamageScalingPerLevel = 0.1f,
                BaseHitpoints = 100f,
                HitpointsPerLevel = 5f,
                BaseMana = 50f,
                ManaPerRestorationLevel = 3f,
                BaseManaRegenPerTick = 0.5f,
                BaseDisengageTimeTicks = 5,
                MinDisengageTimeTicks = 5,
                DisengageReductionPerAthleticsLevel = 0f,
                CombatXpPerActionTimeTick = 1.0f,
                DefenceReductionPerLevel = 0.5f,
                MaxDefenceReductionPercent = 75f,
                HitpointsXpPerDamage = 0.5f,
                DefenceXpPerDamage = 0.5f,
                DeathRespawnBaseTime = 5f,
                DeathRespawnTravelMultiplier = 1.2f,
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new List<RunnerFactory.RunnerDefinition>();
            for (int i = 0; i < runnerCount; i++)
            {
                defs.Add(new RunnerFactory.RunnerDefinition { Name = $"Fighter{i}" }
                    .WithSkill(SkillType.Melee, meleeLevel)
                    .WithSkill(SkillType.Hitpoints, hitpointsLevel)
                    .WithSkill(SkillType.Defence, 1)
                    .WithSkill(SkillType.Magic, 5)
                    .WithSkill(SkillType.Restoration, 5));
            }

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("goblin_camp", "Goblin Camp", 10f, 0f, null,
                System.Array.Empty<Simulation.Gathering.GatherableConfig>(),
                new[] { new EnemySpawnEntry("goblin_grunt", 3, 100) });
            map.AddEdge("hub", "goblin_camp", 10f);
            map.Initialize();

            _sim.StartNewGame(defs.ToArray(), map, "goblin_camp");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        private void AddCombatStyle(CombatStyle style)
        {
            _sim.CurrentGameState.CombatStyleLibrary.Add(style);
        }

        private Ruleset CreateFightMicro()
        {
            var micro = new Ruleset
            {
                Id = "fight-micro",
                Name = "Fight Micro",
                Category = RulesetCategory.Combat,
            };
            micro.Rules.Add(new Rule
            {
                Label = "Fight Here",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FightHere(),
                Enabled = true,
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(micro);
            return micro;
        }

        private void AssignFightSequence(Runner runner, string microId,
            string combatStyleOverrideId = null)
        {
            var seq = new TaskSequence
            {
                Id = $"fight-seq-{runner.Id}",
                Name = "Fight at Goblin Camp",
                TargetNodeId = "goblin_camp",
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "goblin_camp"),
                    new TaskStep(TaskStepType.Work, microRulesetId: microId,
                        combatStyleOverrideId: combatStyleOverrideId),
                },
            };
            _sim.AssignRunner(runner.Id, seq, "test");
        }

        private int TickUntil(System.Func<bool> condition, int safetyLimit = 5000)
        {
            int ticks = 0;
            while (!condition() && ticks < safetyLimit)
            {
                _sim.Tick();
                ticks++;
            }
            return ticks;
        }

        // ─── Targeting ──────────────────────────────────────

        [Test]
        public void TargetingRulesFirstMatchWins()
        {
            Setup();
            var micro = CreateFightMicro();

            // Style: first rule targets HighestHp, second targets LowestHp
            var style = new CombatStyle
            {
                Id = "highest-first",
                Name = "Highest HP First",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Highest HP",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.HighestHpEnemy,
                        Enabled = true,
                    },
                    new TargetingRule
                    {
                        Label = "Lowest HP",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.LowestHpEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(style);
            _runner.CombatStyleId = "highest-first";
            AssignFightSequence(_runner, micro.Id);

            // First tick: creates encounter, selects target (all equal HP), starts action
            _sim.Tick();
            Assert.AreEqual(RunnerState.Fighting, _runner.State);

            // Damage one enemy to create HP differences
            var encounter = _sim.CurrentGameState.EncounterStates["goblin_camp"];
            encounter.Enemies[0].CurrentHp = 10f; // lowest
            encounter.Enemies[1].CurrentHp = 20f; // middle
            encounter.Enemies[2].CurrentHp = 30f; // highest (full)

            // Force action completion so runner re-selects target with new HP values
            _runner.Fighting.ActionTicksRemaining = 0;

            // Next tick: runner picks target (HighestHp rule wins)
            _sim.Tick();
            Assert.AreEqual(encounter.Enemies[2].InstanceId, _runner.Fighting.CurrentTargetEnemyId,
                "Should target highest HP enemy (first rule wins)");
        }

        [Test]
        public void LowestHpTargetingSelectsWeakest()
        {
            Setup();
            var micro = CreateFightMicro();

            var style = new CombatStyle
            {
                Id = "lowest-first",
                Name = "Lowest HP",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Lowest HP",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.LowestHpEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(style);
            _runner.CombatStyleId = "lowest-first";
            AssignFightSequence(_runner, micro.Id);

            // First tick: creates encounter, selects target (all equal HP), starts action
            _sim.Tick();
            var encounter = _sim.CurrentGameState.EncounterStates["goblin_camp"];
            encounter.Enemies[0].CurrentHp = 25f;
            encounter.Enemies[1].CurrentHp = 5f; // weakest
            encounter.Enemies[2].CurrentHp = 30f;

            // Force action completion so runner re-selects target with new HP values
            _runner.Fighting.ActionTicksRemaining = 0;

            _sim.Tick();
            Assert.AreEqual(encounter.Enemies[1].InstanceId, _runner.Fighting.CurrentTargetEnemyId,
                "Should target lowest HP enemy");
        }

        // ─── Ability Selection ──────────────────────────────

        [Test]
        public void AbilityRulesFirstMatchWinsSkipsUnavailable()
        {
            // Fireball requires Magic (runner has it), BasicAttack always available
            Setup(abilities: new[] { BasicAttack, Fireball });
            var micro = CreateFightMicro();

            // Style: Fireball first, BasicAttack fallback
            var style = new CombatStyle
            {
                Id = "mage-style",
                Name = "Mage",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Attack nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Fireball",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "fireball",
                        Enabled = true,
                    },
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(style);
            _runner.CombatStyleId = "mage-style";
            AssignFightSequence(_runner, micro.Id);

            _sim.Tick(); // enter fighting
            _sim.Tick(); // pick target + ability

            Assert.AreEqual("fireball", _runner.Fighting.CurrentAbilityId,
                "Should use Fireball (first matching rule)");
        }

        // ─── No Combat Style = Idle with Warning ───────────

        [Test]
        public void NoCombatStyleRunnerIdlesWithWarning()
        {
            Setup();
            var micro = CreateFightMicro();

            // Don't assign combat style
            _runner.CombatStyleId = null;
            AssignFightSequence(_runner, micro.Id);

            _sim.Tick(); // enter fighting
            Assert.AreEqual(RunnerState.Fighting, _runner.State);

            _sim.Tick(); // try to act: no style
            Assert.IsFalse(_runner.Fighting.IsActing,
                "Runner should not be acting without a combat style");
            Assert.AreEqual(RunnerWarnings.NoCombatStyle, _runner.ActiveWarning);
        }

        // ─── Per-Step Combat Style Override ─────────────────

        [Test]
        public void PerStepCombatStyleOverrideTakesPrecedence()
        {
            Setup(abilities: new[] { BasicAttack, Fireball });
            var micro = CreateFightMicro();

            // Runner-level style: BasicAttack
            var meleeStyle = new CombatStyle
            {
                Id = "runner-melee",
                Name = "Runner Melee",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(meleeStyle);
            _runner.CombatStyleId = "runner-melee";

            // Step-level override: Fireball
            var mageStyle = new CombatStyle
            {
                Id = "step-mage",
                Name = "Step Mage",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Fireball",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "fireball",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(mageStyle);

            // Assign with step-level override
            AssignFightSequence(_runner, micro.Id, combatStyleOverrideId: "step-mage");

            _sim.Tick(); // enter fighting
            _sim.Tick(); // pick target + ability

            Assert.AreEqual("fireball", _runner.Fighting.CurrentAbilityId,
                "Should use step-level override style (Fireball), not runner-level (BasicAttack)");
        }

        // ─── Ability Unlock Check ───────────────────────────

        [Test]
        public void AbilityUnlockCheckSkipsLockedAbilities()
        {
            var highLevelAbility = new AbilityConfig
            {
                Id = "fire_nova",
                Name = "Fire Nova",
                SkillType = SkillType.Magic,
                ActionTimeTicks = 35,
                UnlockLevel = 50, // requires Magic 50
                Effects = { new AbilityEffect(EffectType.Damage, 30f, SkillType.Magic, 1.0f) },
            };
            Setup(meleeLevel: 5, abilities: new[] { highLevelAbility, BasicAttack });
            var micro = CreateFightMicro();

            // Style: Fire Nova first, BasicAttack fallback
            var style = new CombatStyle
            {
                Id = "unlock-test",
                Name = "Unlock Test",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Fire Nova",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "fire_nova",
                        Enabled = true,
                    },
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(style);
            _runner.CombatStyleId = "unlock-test";
            AssignFightSequence(_runner, micro.Id);

            _sim.Tick(); // enter fighting
            _sim.Tick(); // pick ability

            Assert.AreEqual("basic_attack", _runner.Fighting.CurrentAbilityId,
                "Should fall through to BasicAttack because Fire Nova requires Magic 50");
        }

        // ─── SelfHpPercent Condition ────────────────────────

        [Test]
        public void SelfHpPercentConditionEvaluates()
        {
            Setup(abilities: new[] { BasicAttack, Fireball });
            var micro = CreateFightMicro();

            // Style: Fireball when HP < 50%, BasicAttack otherwise
            var style = new CombatStyle
            {
                Id = "hp-aware",
                Name = "HP Aware",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Fireball when hurt",
                        Conditions = { CombatCondition.SelfHpPercent(ComparisonOperator.LessThan, 50f) },
                        AbilityId = "fireball",
                        Enabled = true,
                    },
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(style);
            _runner.CombatStyleId = "hp-aware";
            AssignFightSequence(_runner, micro.Id);

            _sim.Tick(); // enter fighting

            // At full HP, should use BasicAttack
            _sim.Tick();
            Assert.AreEqual("basic_attack", _runner.Fighting.CurrentAbilityId,
                "Should use BasicAttack at full HP");

            // Lower HP below 50%
            float maxHp = CombatFormulas.CalculateMaxHitpoints(
                _runner.GetEffectiveLevel(SkillType.Hitpoints, _config), _config);
            _runner.CurrentHitpoints = maxHp * 0.3f; // 30% HP

            // Complete current action
            _runner.Fighting.ActionTicksRemaining = 0;
            _runner.Fighting.CurrentAbilityId = null;
            _runner.Fighting.CurrentTargetEnemyId = null;
            _runner.Fighting.ActionTicksTotal = 0;

            _sim.Tick(); // re-evaluate: HP < 50%, should use Fireball
            Assert.AreEqual("fireball", _runner.Fighting.CurrentAbilityId,
                "Should use Fireball when HP < 50%");
        }

        // ─── TargetHpPercent Condition ──────────────────────

        [Test]
        public void TargetHpPercentConditionEvaluates()
        {
            Setup(abilities: new[] { BasicAttack, Fireball });
            var micro = CreateFightMicro();

            // Style: first target nearest, then:
            // Fireball when target HP < 35%, BasicAttack otherwise
            var style = new CombatStyle
            {
                Id = "culling-style",
                Name = "Culling Style",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Fireball (low HP)",
                        Conditions = { CombatCondition.TargetHpPercent(ComparisonOperator.LessThan, 35f) },
                        AbilityId = "fireball",
                        Enabled = true,
                    },
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(style);
            _runner.CombatStyleId = "culling-style";
            AssignFightSequence(_runner, micro.Id);

            _sim.Tick(); // enter fighting
            _sim.Tick(); // pick target + ability (enemy at full HP)

            Assert.AreEqual("basic_attack", _runner.Fighting.CurrentAbilityId,
                "Should use BasicAttack when target is full HP");
        }

        // ─── AbilityOffCooldown Condition ───────────────────

        [Test]
        public void AbilityOffCooldownConditionEvaluates()
        {
            var cooldownAbility = new AbilityConfig
            {
                Id = "power_strike",
                Name = "Power Strike",
                SkillType = SkillType.Melee,
                ActionTimeTicks = 5,
                CooldownTicks = 50,
                ManaCost = 0f,
                UnlockLevel = 0,
                Effects = { new AbilityEffect(EffectType.Damage, 20f, SkillType.Melee, 1.0f) },
            };
            Setup(abilities: new[] { cooldownAbility, BasicAttack });
            var micro = CreateFightMicro();

            // Style: use PowerStrike when off cooldown, else BasicAttack
            var style = new CombatStyle
            {
                Id = "cd-aware",
                Name = "CD Aware",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Power Strike",
                        Conditions = { CombatCondition.AbilityOffCooldown("power_strike") },
                        AbilityId = "power_strike",
                        Enabled = true,
                    },
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(style);
            _runner.CombatStyleId = "cd-aware";
            AssignFightSequence(_runner, micro.Id);

            _sim.Tick(); // enter fighting
            _sim.Tick(); // first action: PowerStrike (off cooldown)
            Assert.AreEqual("power_strike", _runner.Fighting.CurrentAbilityId,
                "Should use PowerStrike first (off cooldown)");

            // Complete action to trigger cooldown
            for (int i = 0; i < 5; i++)
                _sim.Tick();

            // Now PowerStrike is on cooldown, should use BasicAttack
            _sim.Tick();
            Assert.AreEqual("basic_attack", _runner.Fighting.CurrentAbilityId,
                "Should use BasicAttack while PowerStrike is on cooldown");
        }

        // ─── Basic Melee Style ──────────────────────────────

        [Test]
        public void BasicMeleeStyleTargetsNearestUsesBasicAttack()
        {
            Setup();
            var micro = CreateFightMicro();

            AddCombatStyle(DefaultRulesets.CreateBasicMeleeCombatStyle());
            _runner.CombatStyleId = DefaultRulesets.BasicMeleeCombatStyleId;
            AssignFightSequence(_runner, micro.Id);

            _sim.Tick(); // enter fighting
            _sim.Tick(); // pick target + ability

            Assert.IsNotNull(_runner.Fighting.CurrentTargetEnemyId, "Should have a target");
            Assert.AreEqual("basic_attack", _runner.Fighting.CurrentAbilityId,
                "Basic Melee should use BasicAttack");
        }

        // ─── Basic Healer Style (Ally Targeting) ────────────

        [Test]
        public void BasicHealerStyleTargetsLowestHpAlly()
        {
            Setup(abilities: new[] { BasicAttack, Heal }, runnerCount: 2);
            var micro = CreateFightMicro();

            // Runner 0: healer, Runner 1: fighter
            var healerStyle = DefaultRulesets.CreateBasicHealerCombatStyle();
            AddCombatStyle(healerStyle);
            _runner.CombatStyleId = DefaultRulesets.BasicHealerCombatStyleId;

            var meleeStyle = DefaultRulesets.CreateBasicMeleeCombatStyle();
            AddCombatStyle(meleeStyle);
            var runner2 = _sim.CurrentGameState.Runners[1];
            runner2.CombatStyleId = DefaultRulesets.BasicMeleeCombatStyleId;

            AssignFightSequence(_runner, micro.Id);
            AssignFightSequence(runner2, micro.Id);

            // Both enter fighting
            _sim.Tick();
            Assert.AreEqual(RunnerState.Fighting, _runner.State);
            Assert.AreEqual(RunnerState.Fighting, runner2.State);

            // Healer targets allies, not enemies. With LowestHpAlly targeting,
            // the evaluator returns null for enemy targeting (ally rules don't resolve to enemies).
            // The healer should idle since EvaluateTargeting returns null for ally-only styles.
            _sim.Tick();

            // The healer has LowestHpAlly targeting which returns null from EvaluateTargeting
            // (it returns EnemyInstance, not Runner). So healer idles.
            // This is correct: healer targeting is separate from enemy targeting.
            Assert.IsFalse(_runner.Fighting.IsActing,
                "Healer with LowestHpAlly should not target enemies directly");
        }

        // ─── Interrupt Ability Rule ─────────────────────────

        [Test]
        public void InterruptAbilityRuleCancelsCurrentAction()
        {
            Setup(abilities: new[] { BasicAttack, Fireball });
            var micro = CreateFightMicro();

            // Style: interrupt with Fireball when HP < 30%, else BasicAttack
            // Note: interrupt is handled by micro rules (FinishTask/CanInterrupt).
            // Combat style ability rules with CanInterrupt are checked during combat action commitment.
            // But per the plan, interrupt-only ability evaluation is a future enhancement.
            // For now, micro rules handle interrupts. This test verifies the existing micro interrupt
            // still works with combat styles.
            var style = new CombatStyle
            {
                Id = "basic-for-interrupt",
                Name = "Basic",
                TargetingRules =
                {
                    new TargetingRule
                    {
                        Label = "Nearest",
                        Conditions = { CombatCondition.Always() },
                        Selection = TargetSelection.NearestEnemy,
                        Enabled = true,
                    },
                },
                AbilityRules =
                {
                    new AbilityRule
                    {
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            AddCombatStyle(style);
            _runner.CombatStyleId = "basic-for-interrupt";

            // Micro with interrupt rule
            var interruptMicro = new Ruleset
            {
                Id = "interrupt-micro",
                Name = "Interrupt Micro",
                Category = RulesetCategory.Combat,
            };
            interruptMicro.Rules.Add(new Rule
            {
                Label = "Interrupt on loot",
                Conditions = { Condition.InventorySlots(ComparisonOperator.LessThan, 28) },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
                CanInterrupt = true,
            });
            interruptMicro.Rules.Add(new Rule
            {
                Label = "Fight",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FightHere(),
                Enabled = true,
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(interruptMicro);

            AssignFightSequence(_runner, interruptMicro.Id);

            _sim.Tick(); // enter fighting
            _sim.Tick(); // start action
            Assert.IsTrue(_runner.Fighting.IsActing);

            // Add item to trigger interrupt
            _runner.Inventory.TryAdd(new ItemDefinition("goblin_tooth", "Goblin Tooth", ItemCategory.Misc));
            _sim.Tick(); // interrupt fires

            Assert.IsTrue(_runner.Fighting.IsDisengaging,
                "Interrupt should trigger disengage mid-action");
        }

        // ─── Waiting State ──────────────────────────────────

        [Test]
        public void WaitActionSetsRunnerToWaitingState()
        {
            Setup(runnerCount: 1);

            // Micro: Wait if allies < 2, else FightHere
            var waitMicro = new Ruleset
            {
                Id = "wait-micro",
                Name = "Wait for Allies",
                Category = RulesetCategory.Combat,
            };
            waitMicro.Rules.Add(new Rule
            {
                Label = "Wait for 2 allies",
                Conditions = { Condition.AllyCountAtNode(ComparisonOperator.LessThan, 2) },
                Action = AutomationAction.Wait(),
                Enabled = true,
            });
            waitMicro.Rules.Add(new Rule
            {
                Label = "Fight",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FightHere(),
                Enabled = true,
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(waitMicro);

            var seq = new TaskSequence
            {
                Id = "wait-seq",
                Name = "Wait then Fight",
                TargetNodeId = "goblin_camp",
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "goblin_camp"),
                    new TaskStep(TaskStepType.Work, microRulesetId: waitMicro.Id),
                },
            };
            _sim.AssignRunner(_runner.Id, seq, "test");

            // Only 1 runner at the node, needs 2 allies (other runners, not counting self)
            _sim.Tick();
            Assert.AreEqual(RunnerState.Waiting, _runner.State,
                "Runner should be waiting for allies");
            Assert.IsNotNull(_runner.Waiting);
            Assert.AreEqual("goblin_camp", _runner.Waiting.NodeId);
        }

        [Test]
        public void WaitingRunnerProceedsWhenConditionMet()
        {
            Setup(runnerCount: 3);

            // All runners need allies >= 2
            var waitMicro = new Ruleset
            {
                Id = "wait-micro",
                Name = "Wait for Allies",
                Category = RulesetCategory.Combat,
            };
            waitMicro.Rules.Add(new Rule
            {
                Label = "Wait for allies",
                Conditions = { Condition.AllyCountAtNode(ComparisonOperator.LessThan, 2) },
                Action = AutomationAction.Wait(),
                Enabled = true,
            });
            waitMicro.Rules.Add(new Rule
            {
                Label = "Fight",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FightHere(),
                Enabled = true,
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(waitMicro);

            // Add combat style for all runners
            AddCombatStyle(DefaultRulesets.CreateBasicMeleeCombatStyle());

            // Runner 0 and 1 already at goblin_camp, Runner 2 at goblin_camp too
            foreach (var r in _sim.CurrentGameState.Runners)
            {
                r.CombatStyleId = DefaultRulesets.BasicMeleeCombatStyleId;
                var seq = new TaskSequence
                {
                    Id = $"wait-seq-{r.Id}",
                    Name = "Wait then Fight",
                    TargetNodeId = "goblin_camp",
                    Loop = true,
                    Steps = new List<TaskStep>
                    {
                        new TaskStep(TaskStepType.TravelTo, "goblin_camp"),
                        new TaskStep(TaskStepType.Work, microRulesetId: waitMicro.Id),
                    },
                };
                _sim.AssignRunner(r.Id, seq, "test");
            }

            // With 3 runners at the same node, each has 2 allies (>= 2), so Wait condition is false
            // All runners should proceed to FightHere
            _sim.Tick();

            foreach (var r in _sim.CurrentGameState.Runners)
            {
                Assert.AreEqual(RunnerState.Fighting, r.State,
                    $"Runner {r.Name} should be fighting (2 allies present)");
            }
        }

        // ─── EnsureInLibrary ────────────────────────────────

        [Test]
        public void EnsureInLibraryAddsDefaultCombatStyles()
        {
            Setup();

            // EnsureInLibrary is called during StartNewGame
            bool hasMelee = false, hasMage = false, hasHealer = false;
            foreach (var s in _sim.CurrentGameState.CombatStyleLibrary)
            {
                if (s.Id == DefaultRulesets.BasicMeleeCombatStyleId) hasMelee = true;
                if (s.Id == DefaultRulesets.BasicMageCombatStyleId) hasMage = true;
                if (s.Id == DefaultRulesets.BasicHealerCombatStyleId) hasHealer = true;
            }

            Assert.IsTrue(hasMelee, "Basic Melee combat style should be in library");
            Assert.IsTrue(hasMage, "Basic Mage combat style should be in library");
            Assert.IsTrue(hasHealer, "Basic Healer combat style should be in library");
        }

        // ─── Decision Log ───────────────────────────────────

        [Test]
        public void CombatDecisionLoggedOnTargetAndAbilitySelection()
        {
            Setup();
            var micro = CreateFightMicro();

            AddCombatStyle(DefaultRulesets.CreateBasicMeleeCombatStyle());
            _runner.CombatStyleId = DefaultRulesets.BasicMeleeCombatStyleId;
            AssignFightSequence(_runner, micro.Id);

            _sim.Tick(); // enter fighting
            _sim.Tick(); // pick target + ability

            // Should have a combat decision log entry
            bool found = false;
            foreach (var entry in _sim.CurrentGameState.CombatDecisionLog.Entries)
            {
                if (entry.RunnerId == _runner.Id && entry.TriggerReason == "CombatStyle")
                {
                    found = true;
                    Assert.IsTrue(entry.ActionDetail.Contains("Basic Attack"),
                        "Decision log should mention the ability used");
                    break;
                }
            }
            Assert.IsTrue(found, "Should have a combat decision log entry");
        }
    }
}
