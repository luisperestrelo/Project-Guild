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
    /// Tests for the core combat tick: fighting state, damage dealing, enemy death/loot,
    /// enemy respawn, runner damage, XP awards, disengage, interrupts, mana, cooldowns.
    /// </summary>
    [TestFixture]
    public class CombatTickTests
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
            CooldownTicks = 30,
            ManaCost = 20f,
            UnlockLevel = 0,
            Effects = { new AbilityEffect(EffectType.Damage, 15f, SkillType.Magic, 1.0f) },
        };

        private static readonly EnemyConfig GoblinGrunt = new()
        {
            Id = "goblin_grunt",
            Name = "Goblin Grunt",
            Level = 3,
            MaxHitpoints = 30f,
            BaseDamage = 5f,
            BaseDefence = 0f,
            AttackSpeedTicks = 10,
            AiBehavior = EnemyAiBehavior.Aggressive,
            LootTable = { new LootTableEntry("goblin_tooth", 1.0f, 1, 1) },
        };

        private void Setup(int meleeLevel = 5, int hitpointsLevel = 5,
            AbilityConfig[] abilities = null, EnemyConfig[] enemies = null)
        {
            var basicAttackCopy = new AbilityConfig
            {
                Id = BasicAttack.Id, Name = BasicAttack.Name, SkillType = BasicAttack.SkillType,
                ActionTimeTicks = BasicAttack.ActionTimeTicks, CooldownTicks = BasicAttack.CooldownTicks,
                ManaCost = BasicAttack.ManaCost, UnlockLevel = BasicAttack.UnlockLevel,
                Effects = new List<AbilityEffect>(BasicAttack.Effects),
            };

            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("goblin_tooth", "Goblin Tooth", ItemCategory.Misc),
                },
                AbilityDefinitions = abilities ?? new[] { basicAttackCopy },
                EnemyDefinitions = enemies ?? new[] { GoblinGrunt },
                CombatDamageScalingPerLevel = 0.1f,
                BaseHitpoints = 50f,
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

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Fighter" }
                    .WithSkill(SkillType.Melee, meleeLevel)
                    .WithSkill(SkillType.Hitpoints, hitpointsLevel)
                    .WithSkill(SkillType.Defence, 1)
                    .WithSkill(SkillType.Magic, 5)
                    .WithSkill(SkillType.Restoration, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("goblin_camp", "Goblin Camp", 10f, 0f, null,
                System.Array.Empty<Simulation.Gathering.GatherableConfig>(),
                new[] { new EnemySpawnEntry("goblin_grunt", 3, 100) });
            map.AddEdge("hub", "goblin_camp", 10f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "goblin_camp");
            _runner = _sim.CurrentGameState.Runners[0];

            // Default combat style for tests: target nearest, use first ability
            SetupCombatStyle(_config.AbilityDefinitions[0].Id);
        }

        private void SetupCombatStyle(string abilityId)
        {
            var style = new CombatStyle
            {
                Id = "test-melee",
                Name = "Test Melee",
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
                        Label = "Use ability",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = abilityId,
                        Enabled = true,
                    },
                },
            };

            // Replace existing style if present
            _sim.CurrentGameState.CombatStyleLibrary.RemoveAll(s => s.Id == "test-melee");
            _sim.CurrentGameState.CombatStyleLibrary.Add(style);

            foreach (var r in _sim.CurrentGameState.Runners)
                r.CombatStyleId = "test-melee";
        }

        private Ruleset CreateFightMicro()
        {
            var micro = new Ruleset
            {
                Id = "fight-micro",
                Name = "Fight Micro",
                Category = RulesetCategory.Gathering,
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

        private void AssignFightSequence(string microId)
        {
            var seq = new TaskSequence
            {
                Id = "fight-seq",
                Name = "Fight at Goblin Camp",
                TargetNodeId = "goblin_camp",
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "goblin_camp"),
                    new TaskStep(TaskStepType.Work, microRulesetId: microId),
                },
            };
            _sim.AssignRunner(_runner.Id, seq, "test");
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

        [Test]
        public void RunnerEntersFightingState()
        {
            Setup();
            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            // Runner starts at goblin_camp, first tick should enter fighting
            _sim.Tick();
            Assert.AreEqual(RunnerState.Fighting, _runner.State);
            Assert.IsNotNull(_runner.Fighting);
            Assert.AreEqual("goblin_camp", _runner.Fighting.NodeId);
        }

        [Test]
        public void RunnerHpInitializedOnFirstCombat()
        {
            Setup();
            Assert.AreEqual(-1f, _runner.CurrentHitpoints, "HP uninitialized before combat");

            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);
            _sim.Tick();

            Assert.Greater(_runner.CurrentHitpoints, 0f, "HP should be initialized");
            Assert.Greater(_runner.CurrentMana, 0f, "Mana should be initialized");
        }

        [Test]
        public void EncounterCreatedWithEnemies()
        {
            Setup();
            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);
            _sim.Tick();

            Assert.IsTrue(_sim.CurrentGameState.EncounterStates.ContainsKey("goblin_camp"));
            var encounter = _sim.CurrentGameState.EncounterStates["goblin_camp"];
            Assert.AreEqual(3, encounter.Enemies.Count, "Should spawn 3 goblins (InitialCount)");
            Assert.IsTrue(encounter.IsActive);
        }

        [Test]
        public void RunnerDealsDamageToEnemy()
        {
            Setup();
            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            // Enter fighting
            _sim.Tick();
            Assert.AreEqual(RunnerState.Fighting, _runner.State);

            // Runner picks target and starts action commitment (10 ticks)
            // After 10 more ticks, the action resolves and damages enemy
            var encounter = _sim.CurrentGameState.EncounterStates["goblin_camp"];
            float initialHp = encounter.Enemies[0].CurrentHp;

            // Tick through the action time
            for (int i = 0; i < 10; i++)
                _sim.Tick();

            float afterHp = encounter.Enemies[0].CurrentHp;
            Assert.Less(afterHp, initialHp, "Enemy should have taken damage");
        }

        [Test]
        public void EnemyDiesAndDropsLoot()
        {
            Setup();

            // Use a weak enemy that will die quickly
            var weakEnemy = new EnemyConfig
            {
                Id = "goblin_grunt",
                Name = "Goblin Grunt",
                MaxHitpoints = 5f, // Very low HP
                BaseDamage = 1f,
                AttackSpeedTicks = 100, // Very slow attacks
                LootTable = { new LootTableEntry("goblin_tooth", 1.0f, 1, 1) },
            };
            Setup(meleeLevel: 10, enemies: new[] { weakEnemy });

            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            bool enemyDied = false;
            bool lootDropped = false;
            _sim.Events.Subscribe<EnemyDied>(e => enemyDied = true);
            _sim.Events.Subscribe<LootDropped>(e => lootDropped = true);

            // Tick until enemy dies
            TickUntil(() => enemyDied, 100);

            Assert.IsTrue(enemyDied, "Enemy should have died");
            Assert.IsTrue(lootDropped, "Loot should have dropped");
            Assert.Greater(_runner.Inventory.Slots.Count, 0, "Runner should have loot in inventory");
        }

        [Test]
        public void EnemyRespawnsAfterTimer()
        {
            Setup();
            var weakEnemy = new EnemyConfig
            {
                Id = "goblin_grunt", Name = "Goblin Grunt",
                MaxHitpoints = 5f, BaseDamage = 1f, AttackSpeedTicks = 100,
            };
            Setup(meleeLevel: 10, enemies: new[] { weakEnemy });

            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            // Kill an enemy
            bool enemyDied = false;
            _sim.Events.Subscribe<EnemyDied>(e => enemyDied = true);
            TickUntil(() => enemyDied, 100);
            Assert.IsTrue(enemyDied);

            var encounter = _sim.CurrentGameState.EncounterStates["goblin_camp"];
            // Find the dead enemy
            EnemyInstance deadEnemy = null;
            foreach (var e in encounter.Enemies)
            {
                if (!e.IsAlive)
                {
                    deadEnemy = e;
                    break;
                }
            }
            Assert.IsNotNull(deadEnemy, "Should have a dead enemy");

            // Wait for respawn (100 ticks)
            int spawned = 0;
            _sim.Events.Subscribe<EnemySpawned>(e => spawned++);
            TickUntil(() => spawned > 0, 200);

            Assert.Greater(spawned, 0, "Enemy should have respawned");
            Assert.IsTrue(deadEnemy.IsAlive, "Previously dead enemy should be alive again");
        }

        [Test]
        public void RunnerTakesDamageFromEnemy()
        {
            Setup();
            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            // Enter fighting
            _sim.Tick();
            float initialHp = _runner.CurrentHitpoints;

            // Enemies attack after their AttackSpeedTicks
            bool tookDamage = false;
            _sim.Events.Subscribe<RunnerTookDamage>(e => tookDamage = true);

            // Tick enough for enemy to attack (AttackSpeedTicks = 10)
            TickUntil(() => tookDamage, 50);

            Assert.IsTrue(tookDamage, "Runner should have taken damage");
            Assert.Less(_runner.CurrentHitpoints, initialHp, "HP should be lower");
        }

        [Test]
        public void RunnerDiesAtZeroHp()
        {
            // Use a very weak runner and very strong enemy
            var strongEnemy = new EnemyConfig
            {
                Id = "goblin_grunt", Name = "Goblin Grunt",
                MaxHitpoints = 1000f, BaseDamage = 200f, AttackSpeedTicks = 5,
            };
            Setup(meleeLevel: 1, hitpointsLevel: 1, enemies: new[] { strongEnemy });

            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            bool died = false;
            _sim.Events.Subscribe<RunnerDied>(e => died = true);

            TickUntil(() => died, 100);

            Assert.IsTrue(died, "Runner should have died");
            Assert.AreEqual(RunnerState.Dead, _runner.State);
            Assert.IsNotNull(_runner.Death);
            Assert.IsNull(_runner.Fighting);
        }

        [Test]
        public void MultipleRunnersShareEncounter()
        {
            _config = new SimulationConfig
            {
                AbilityDefinitions = new[]
                {
                    new AbilityConfig
                    {
                        Id = "basic_attack", Name = "Basic Attack", SkillType = SkillType.Melee,
                        ActionTimeTicks = 10,
                        Effects = { new AbilityEffect(EffectType.Damage, 10f, SkillType.Melee, 1.0f) },
                    },
                },
                EnemyDefinitions = new[] { GoblinGrunt },
                BaseHitpoints = 200f,
                HitpointsPerLevel = 5f,
                BaseMana = 50f,
                BaseDisengageTimeTicks = 5,
                MinDisengageTimeTicks = 5,
                DisengageReductionPerAthleticsLevel = 0f,
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Fighter1" }
                    .WithSkill(SkillType.Melee, 5).WithSkill(SkillType.Hitpoints, 5),
                new RunnerFactory.RunnerDefinition { Name = "Fighter2" }
                    .WithSkill(SkillType.Melee, 5).WithSkill(SkillType.Hitpoints, 5),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("goblin_camp", "Goblin Camp", 10f, 0f, null,
                System.Array.Empty<Simulation.Gathering.GatherableConfig>(),
                new[] { new EnemySpawnEntry("goblin_grunt", 3, 100) });
            map.AddEdge("hub", "goblin_camp", 10f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "goblin_camp");

            // Add combat style for both runners
            var style = new CombatStyle
            {
                Id = "test-melee",
                Name = "Test Melee",
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
                        Label = "Basic Attack",
                        Conditions = { CombatCondition.Always() },
                        AbilityId = "basic_attack",
                        Enabled = true,
                    },
                },
            };
            _sim.CurrentGameState.CombatStyleLibrary.Add(style);

            var micro = new Ruleset
            {
                Id = "fight-micro", Name = "Fight",
                Category = RulesetCategory.Gathering,
            };
            micro.Rules.Add(new Rule
            {
                Label = "Fight",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FightHere(),
                Enabled = true,
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(micro);

            foreach (var r in _sim.CurrentGameState.Runners)
            {
                r.CombatStyleId = "test-melee";
                var seq = new TaskSequence
                {
                    Id = $"fight-seq-{r.Id}", Name = "Fight",
                    TargetNodeId = "goblin_camp", Loop = true,
                    Steps = new List<TaskStep>
                    {
                        new TaskStep(TaskStepType.TravelTo, "goblin_camp"),
                        new TaskStep(TaskStepType.Work, microRulesetId: micro.Id),
                    },
                };
                _sim.AssignRunner(r.Id, seq, "test");
            }

            _sim.Tick();

            // Both runners should be fighting, sharing one encounter
            Assert.AreEqual(RunnerState.Fighting, _sim.CurrentGameState.Runners[0].State);
            Assert.AreEqual(RunnerState.Fighting, _sim.CurrentGameState.Runners[1].State);
            Assert.AreEqual(1, _sim.CurrentGameState.EncounterStates.Count,
                "Should be one shared encounter");
        }

        [Test]
        public void EncounterResetsWhenEmpty()
        {
            Setup();
            var strongEnemy = new EnemyConfig
            {
                Id = "goblin_grunt", Name = "Goblin Grunt",
                MaxHitpoints = 1000f, BaseDamage = 200f, AttackSpeedTicks = 5,
            };
            Setup(meleeLevel: 1, hitpointsLevel: 1, enemies: new[] { strongEnemy });

            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            // Enter combat, then die
            TickUntil(() => _runner.State == RunnerState.Dead, 100);

            // Encounter should be cleaned up (no fighters left)
            Assert.IsFalse(_sim.CurrentGameState.EncounterStates.ContainsKey("goblin_camp"),
                "Encounter should be removed when no fighters remain");
        }

        [Test]
        public void CombatXpAwardedOnAbilityCompletion()
        {
            Setup();
            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            float initialXp = _runner.GetSkill(SkillType.Melee).Xp;

            // Enter fighting + complete one action (10 ticks)
            _sim.Tick(); // enter fighting
            for (int i = 0; i < 10; i++)
                _sim.Tick();

            float afterXp = _runner.GetSkill(SkillType.Melee).Xp;
            Assert.Greater(afterXp, initialXp,
                "Melee XP should increase after completing an ability");
        }

        [Test]
        public void DefensiveXpOnTakingDamage()
        {
            Setup();
            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            float initialHpXp = _runner.GetSkill(SkillType.Hitpoints).Xp;
            float initialDefXp = _runner.GetSkill(SkillType.Defence).Xp;

            // Wait for runner to take damage
            bool tookDamage = false;
            _sim.Events.Subscribe<RunnerTookDamage>(e => tookDamage = true);
            TickUntil(() => tookDamage, 50);

            Assert.Greater(_runner.GetSkill(SkillType.Hitpoints).Xp, initialHpXp,
                "Hitpoints XP should increase from taking damage");
            Assert.Greater(_runner.GetSkill(SkillType.Defence).Xp, initialDefXp,
                "Defence XP should increase from taking damage");
        }

        [Test]
        public void DisengageFromCombat()
        {
            Setup();
            // Create a micro that fights, then a FinishTask rule
            var micro = new Ruleset
            {
                Id = "fight-then-leave",
                Name = "Fight then Leave",
                Category = RulesetCategory.Gathering,
            };
            // InventorySlots < 28 means we have items. Use Always for first pass.
            micro.Rules.Add(new Rule
            {
                Label = "Leave when full",
                Conditions = { Condition.InventorySlots(ComparisonOperator.LessThan, 27) },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });
            micro.Rules.Add(new Rule
            {
                Label = "Fight",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FightHere(),
                Enabled = true,
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(micro);
            AssignFightSequence(micro.Id);

            // Enter fighting
            _sim.Tick();
            Assert.AreEqual(RunnerState.Fighting, _runner.State);

            // Add items to trigger FinishTask on next re-eval
            _runner.Inventory.TryAdd(new ItemDefinition("goblin_tooth", "Goblin Tooth", ItemCategory.Misc));
            _runner.Inventory.TryAdd(new ItemDefinition("goblin_tooth", "Goblin Tooth", ItemCategory.Misc));

            // Complete an action to trigger re-eval
            for (int i = 0; i < 10; i++)
                _sim.Tick();

            // Runner should be disengaging now
            Assert.IsTrue(_runner.Fighting?.IsDisengaging ?? false,
                "Runner should be disengaging after FinishTask");

            // Wait for disengage to complete (5 ticks)
            TickUntil(() => _runner.State != RunnerState.Fighting, 20);

            Assert.AreEqual(RunnerState.Idle, _runner.State,
                "Runner should be idle after disengage completes");
        }

        [Test]
        public void InterruptRuleDuringCombat()
        {
            Setup();
            var micro = new Ruleset
            {
                Id = "fight-interrupt",
                Name = "Fight with Interrupt",
                Category = RulesetCategory.Gathering,
            };
            // Interrupt rule: if inventory has items, finish task (can interrupt mid-action)
            micro.Rules.Add(new Rule
            {
                Label = "Interrupt on loot",
                Conditions = { Condition.InventorySlots(ComparisonOperator.LessThan, 28) },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
                CanInterrupt = true,
            });
            micro.Rules.Add(new Rule
            {
                Label = "Fight",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FightHere(),
                Enabled = true,
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(micro);
            AssignFightSequence(micro.Id);

            // Enter fighting
            _sim.Tick();

            // Start action commitment
            _sim.Tick();
            Assert.IsTrue(_runner.Fighting?.IsActing ?? false, "Should be mid-action");

            // Add items to trigger interrupt check
            _runner.Inventory.TryAdd(new ItemDefinition("goblin_tooth", "Goblin Tooth", ItemCategory.Misc));
            _sim.Tick(); // interrupt check fires

            Assert.IsTrue(_runner.Fighting?.IsDisengaging ?? false,
                "Interrupt rule should trigger disengage mid-action");
        }

        [Test]
        public void ManaConsumedAndRegenerated()
        {
            var healingTouch = new AbilityConfig
            {
                Id = "healing_touch", Name = "Healing Touch", SkillType = SkillType.Restoration,
                ActionTimeTicks = 5, CooldownTicks = 0, ManaCost = 20f,
                Effects = { new AbilityEffect(EffectType.HealSelf, 15f, SkillType.Restoration, 1.0f) },
            };
            Setup(abilities: new[] { healingTouch });

            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            // Runner enters fighting during AssignRunner (ExecuteCurrentStep processes instantly)
            Assert.AreEqual(RunnerState.Fighting, _runner.State);
            float manaAfterEntry = _runner.CurrentMana;

            // First tick: TickFighting selects fireball and consumes mana
            _sim.Tick();
            float manaAfterCast = _runner.CurrentMana;
            Assert.Less(manaAfterCast, manaAfterEntry, "Mana should decrease when casting");

            // Mid-action ticks: mana regens each tick while acting
            float manaBeforeRegen = _runner.CurrentMana;
            _sim.Tick();
            Assert.Greater(_runner.CurrentMana, manaBeforeRegen, "Mana should regenerate each tick");
        }

        [Test]
        public void AbilityCooldownsTracked()
        {
            var cooldownAbility = new AbilityConfig
            {
                Id = "power_attack", Name = "Power Attack", SkillType = SkillType.Melee,
                ActionTimeTicks = 5, CooldownTicks = 20, ManaCost = 0f,
                Effects = { new AbilityEffect(EffectType.Damage, 20f, SkillType.Melee, 1.0f) },
            };
            Setup(abilities: new[] { cooldownAbility });

            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            // Enter fighting and complete first action
            _sim.Tick();
            for (int i = 0; i < 5; i++)
                _sim.Tick();

            // After completion, cooldown should be set
            Assert.IsTrue(
                _runner.Fighting?.CooldownTrackers?.ContainsKey("power_attack") ?? false,
                "Ability should be on cooldown after use");
        }

        [Test]
        public void NoAvailableAbilityRunnerIdles()
        {
            var highLevelAbility = new AbilityConfig
            {
                Id = "high_level_ability", Name = "High Level", SkillType = SkillType.Melee,
                ActionTimeTicks = 5, CooldownTicks = 0, ManaCost = 0f,
                UnlockLevel = 99, // Runner won't have this level
                Effects = { new AbilityEffect(EffectType.Damage, 20f, SkillType.Melee, 1.0f) },
            };
            Setup(meleeLevel: 1, abilities: new[] { highLevelAbility });

            // Combat style references the high-level ability (runner can't use it)
            SetupCombatStyle("high_level_ability");

            var micro = CreateFightMicro();
            AssignFightSequence(micro.Id);

            // Enter fighting
            _sim.Tick();
            Assert.AreEqual(RunnerState.Fighting, _runner.State);

            // Should be fighting but not acting (no ability available)
            _sim.Tick();
            Assert.IsFalse(_runner.Fighting?.IsActing ?? true,
                "Runner should not be able to act without available abilities");
        }
    }
}
