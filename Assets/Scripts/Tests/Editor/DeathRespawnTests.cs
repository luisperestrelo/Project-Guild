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
    /// Tests for death and respawn mechanics: timer countdown, HP restore,
    /// hub teleport, sequence restart, respawn time scaling, death during disengage,
    /// dead runner macro evaluation skip.
    /// </summary>
    [TestFixture]
    public class DeathRespawnTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        /// <summary>
        /// Set up a scenario where the runner will die quickly from a very strong enemy.
        /// </summary>
        private void SetupForDeath()
        {
            var basicAttack = new AbilityConfig
            {
                Id = "basic_attack", Name = "Basic Attack", SkillType = SkillType.Melee,
                ActionTimeTicks = 10,
                Effects = { new AbilityEffect(EffectType.Damage, 10f, SkillType.Melee, 1.0f) },
            };

            var strongEnemy = new EnemyConfig
            {
                Id = "boss", Name = "Boss",
                MaxHitpoints = 10000f,
                BaseDamage = 500f,
                AttackSpeedTicks = 3,
            };

            _config = new SimulationConfig
            {
                AbilityDefinitions = new[] { basicAttack },
                EnemyDefinitions = new[] { strongEnemy },
                BaseHitpoints = 50f,
                HitpointsPerLevel = 5f,
                BaseMana = 50f,
                BaseDisengageTimeTicks = 5,
                MinDisengageTimeTicks = 5,
                DisengageReductionPerAthleticsLevel = 0f,
                DeathRespawnBaseTime = 5f,
                DeathRespawnTravelMultiplier = 1.2f,
                HitpointsXpPerDamage = 0.5f,
                DefenceXpPerDamage = 0.5f,
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Victim" }
                    .WithSkill(SkillType.Melee, 1)
                    .WithSkill(SkillType.Hitpoints, 1)
                    .WithSkill(SkillType.Defence, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("boss_lair", "Boss Lair", 20f, 0f, null,
                System.Array.Empty<Simulation.Gathering.GatherableConfig>(),
                new[] { new EnemySpawnEntry("boss", 1, 100) });
            map.AddEdge("hub", "boss_lair", 20f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "boss_lair");
            _runner = _sim.CurrentGameState.Runners[0];

            // Create fight micro and assign
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

            // Add combat style so the runner can fight
            var style = new CombatStyle
            {
                Id = "test-melee", Name = "Test Melee",
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
            _runner.CombatStyleId = "test-melee";

            var seq = new TaskSequence
            {
                Id = "fight-seq", Name = "Fight at Boss",
                TargetNodeId = "boss_lair", Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "boss_lair"),
                    new TaskStep(TaskStepType.Work, microRulesetId: micro.Id),
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
        public void DeathTimerCountsDown()
        {
            SetupForDeath();

            TickUntil(() => _runner.State == RunnerState.Dead, 100);
            Assert.AreEqual(RunnerState.Dead, _runner.State);

            int initialTicks = _runner.Death.RespawnTicksRemaining;
            Assert.Greater(initialTicks, 0, "Respawn timer should be positive");

            _sim.Tick();
            Assert.AreEqual(initialTicks - 1, _runner.Death.RespawnTicksRemaining,
                "Respawn timer should count down each tick");
        }

        [Test]
        public void RespawnRestoresHpToMax()
        {
            SetupForDeath();

            TickUntil(() => _runner.State == RunnerState.Dead, 100);

            // Wait for respawn
            TickUntil(() => _runner.State != RunnerState.Dead, 2000);

            float maxHp = CombatFormulas.CalculateMaxHitpoints(
                _runner.GetEffectiveLevel(SkillType.Hitpoints, _config), _config);
            Assert.AreEqual(maxHp, _runner.CurrentHitpoints, 0.01f,
                "HP should be at max after respawn");

            float maxMana = CombatFormulas.CalculateMaxMana(
                _runner.GetEffectiveLevel(SkillType.Restoration, _config), _config);
            Assert.AreEqual(maxMana, _runner.CurrentMana, 0.01f,
                "Mana should be at max after respawn");
        }

        [Test]
        public void RespawnSetsNodeToHub()
        {
            SetupForDeath();

            TickUntil(() => _runner.State == RunnerState.Dead, 100);
            TickUntil(() => _runner.State != RunnerState.Dead, 2000);

            Assert.AreEqual("hub", _runner.CurrentNodeId,
                "Runner should respawn at hub");
        }

        [Test]
        public void DeadRunnerTaskSequenceRestartsOnRespawn()
        {
            SetupForDeath();

            // Advance past TravelTo step (step 0) to Work step (step 1)
            _sim.Tick(); // should enter fighting at step 1
            Assert.AreEqual(1, _runner.TaskSequenceCurrentStepIndex);

            // Die
            TickUntil(() => _runner.State == RunnerState.Dead, 100);

            // Step index should be reset to 0
            Assert.AreEqual(0, _runner.TaskSequenceCurrentStepIndex,
                "Task sequence should restart at step 0 on death");

            // Respawn
            TickUntil(() => _runner.State != RunnerState.Dead, 2000);

            // Runner should be executing from step 0 (TravelTo boss_lair)
            // Since runner respawns at hub, it needs to travel
            Assert.AreEqual("hub", _runner.CurrentNodeId);
        }

        [Test]
        public void RespawnTimeScalesWithTravelDistance()
        {
            SetupForDeath();

            // Die at boss_lair (20m from hub)
            TickUntil(() => _runner.State == RunnerState.Dead, 100);

            // Respawn time = base + travelTime * multiplier
            // travelTime = distance / speed = 20 / (1.0 + 0*0.05) = 20s
            // respawnTime = 5 + 20 * 1.2 = 29s = 290 ticks
            int respawnTicks = _runner.Death.RespawnTicksRemaining;
            Assert.Greater(respawnTicks, 50, "Respawn time should be > base (distance penalty)");
        }

        [Test]
        public void DeathDuringDisengageStillTriggers()
        {
            // Set up with a strong enemy and interrupt rule
            var basicAttack = new AbilityConfig
            {
                Id = "basic_attack", Name = "Basic Attack", SkillType = SkillType.Melee,
                ActionTimeTicks = 10,
                Effects = { new AbilityEffect(EffectType.Damage, 10f, SkillType.Melee, 1.0f) },
            };

            var strongEnemy = new EnemyConfig
            {
                Id = "boss", Name = "Boss",
                MaxHitpoints = 10000f,
                BaseDamage = 500f,
                AttackSpeedTicks = 2,
            };

            _config = new SimulationConfig
            {
                AbilityDefinitions = new[] { basicAttack },
                EnemyDefinitions = new[] { strongEnemy },
                BaseHitpoints = 50f,
                HitpointsPerLevel = 5f,
                BaseMana = 50f,
                BaseDisengageTimeTicks = 50, // Long disengage so enemy can kill during it
                MinDisengageTimeTicks = 50,
                DisengageReductionPerAthleticsLevel = 0f,
                DeathRespawnBaseTime = 5f,
                DeathRespawnTravelMultiplier = 1.2f,
                HitpointsXpPerDamage = 0.5f,
                DefenceXpPerDamage = 0.5f,
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Victim" }
                    .WithSkill(SkillType.Melee, 1)
                    .WithSkill(SkillType.Hitpoints, 1)
                    .WithSkill(SkillType.Defence, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("boss_lair", "Boss Lair", 20f, 0f, null,
                System.Array.Empty<Simulation.Gathering.GatherableConfig>(),
                new[] { new EnemySpawnEntry("boss", 1, 100) });
            map.AddEdge("hub", "boss_lair", 20f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "boss_lair");
            _runner = _sim.CurrentGameState.Runners[0];

            // Add combat style
            var style = new CombatStyle
            {
                Id = "test-melee", Name = "Test Melee",
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
            _runner.CombatStyleId = "test-melee";

            // Create a FightHere micro so the runner enters combat
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

            var seq = new TaskSequence
            {
                Id = "fight-seq", Name = "Fight",
                TargetNodeId = "boss_lair", Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "boss_lair"),
                    new TaskStep(TaskStepType.Work, microRulesetId: micro.Id),
                },
            };
            _sim.AssignRunner(_runner.Id, seq, "test");

            // Runner enters fighting during AssignRunner
            Assert.AreEqual(RunnerState.Fighting, _runner.State);

            // Manually force disengage with a long timer so the enemy can kill during it
            _runner.Fighting.IsDisengaging = true;
            _runner.Fighting.DisengageTicksRemaining = 500;
            Assert.IsTrue(_runner.Fighting.IsDisengaging, "Should be disengaging");

            // Enemy keeps attacking during disengage, eventually killing the runner
            TickUntil(() => _runner.State == RunnerState.Dead, 200);
            Assert.AreEqual(RunnerState.Dead, _runner.State,
                "Runner should die during disengage from enemy attacks");
        }

        [Test]
        public void DeadRunnerSkippedByMacroEval()
        {
            SetupForDeath();

            // Create a macro rule that would normally fire
            var macroRuleset = new Ruleset
            {
                Id = "test-macro", Name = "Test Macro",
                Category = RulesetCategory.General,
            };
            macroRuleset.Rules.Add(new Rule
            {
                Label = "Always assign",
                Conditions = { Condition.Always() },
                Action = AutomationAction.AssignSequence("nonexistent"),
                Enabled = true,
            });
            _sim.CurrentGameState.MacroRulesetLibrary.Add(macroRuleset);
            _runner.MacroRulesetId = macroRuleset.Id;

            // Die
            TickUntil(() => _runner.State == RunnerState.Dead, 100);

            // Macro rules should NOT fire while dead — runner stays dead
            int ticksBefore = _runner.Death.RespawnTicksRemaining;
            _sim.Tick();
            Assert.AreEqual(RunnerState.Dead, _runner.State,
                "Dead runner should not have macro rules evaluated");
            Assert.AreEqual(ticksBefore - 1, _runner.Death.RespawnTicksRemaining,
                "Death timer should still count down");
        }
    }
}
