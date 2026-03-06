using System.Collections.Generic;
using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for new micro conditions added in Batch C:
    /// EnemyCountAtNode, AllyCountAtNode, AlliesInCombatAtNode.
    /// Tests both the RuleEvaluator (micro) and CombatStyleEvaluator (combat) versions.
    /// </summary>
    [TestFixture]
    public class CombatConditionTests
    {
        private GameSimulation _sim;
        private SimulationConfig _config;

        private void Setup(int runnerCount = 1)
        {
            var basicAttack = new AbilityConfig
            {
                Id = "basic_attack",
                Name = "Basic Attack",
                SkillType = SkillType.Melee,
                ActionTimeTicks = 10,
                Effects = { new AbilityEffect(EffectType.Damage, 10f, SkillType.Melee, 1.0f) },
            };

            var goblin = new EnemyConfig
            {
                Id = "goblin_grunt",
                Name = "Goblin Grunt",
                MaxHitpoints = 100f,
                BaseDamage = 1f,
                AttackSpeedTicks = 100,
                LootTable = { new LootTableEntry("goblin_tooth", 1.0f, 1, 1) },
            };

            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new Simulation.Items.ItemDefinition("goblin_tooth", "Goblin Tooth",
                        Simulation.Items.ItemCategory.Misc),
                },
                AbilityDefinitions = new[] { basicAttack },
                EnemyDefinitions = new[] { goblin },
                BaseHitpoints = 200f,
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
                defs.Add(new RunnerFactory.RunnerDefinition { Name = $"Runner{i}" }
                    .WithSkill(SkillType.Melee, 5)
                    .WithSkill(SkillType.Hitpoints, 5));
            }

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("goblin_camp", "Goblin Camp", 10f, 0f, null,
                System.Array.Empty<Simulation.Gathering.GatherableConfig>(),
                new[] { new EnemySpawnEntry("goblin_grunt", 5, 100) });
            map.AddEdge("hub", "goblin_camp", 10f);
            map.Initialize();

            _sim.StartNewGame(defs.ToArray(), map, "goblin_camp");
        }

        // ─── EnemyCountAtNode (Micro) ──────────────────────

        [Test]
        public void EnemyCountAtNodeConditionMatchesAliveEnemies()
        {
            Setup();
            var runner = _sim.CurrentGameState.Runners[0];

            // Set up combat style and enter fighting first to create an encounter
            runner.CombatStyleId = DefaultRulesets.BasicMeleeCombatStyleId;
            _sim.CurrentGameState.CombatStyleLibrary.Add(DefaultRulesets.CreateBasicMeleeCombatStyle());

            var fightMicro = new Ruleset { Id = "fight", Name = "Fight" };
            fightMicro.Rules.Add(new Rule
            {
                Label = "Fight",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FightHere(),
                Enabled = true,
            });
            _sim.CurrentGameState.MicroRulesetLibrary.Add(fightMicro);

            var seq = new TaskSequence
            {
                Id = "seq", Name = "Fight",
                TargetNodeId = "goblin_camp", Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "goblin_camp"),
                    new TaskStep(TaskStepType.Work, microRulesetId: fightMicro.Id),
                },
            };
            _sim.AssignRunner(runner.Id, seq, "test");
            _sim.Tick(); // enter fighting

            // Now evaluate the condition
            var ctx = new EvaluationContext(runner, _sim.CurrentGameState, _config);
            var condition = Condition.EnemyCountAtNode(ComparisonOperator.GreaterOrEqual, 3);
            bool result = RuleEvaluator.EvaluateCondition(condition, ctx);
            Assert.IsTrue(result, "Should have >= 3 alive enemies at goblin_camp (spawned 5)");

            condition = Condition.EnemyCountAtNode(ComparisonOperator.GreaterOrEqual, 10);
            result = RuleEvaluator.EvaluateCondition(condition, ctx);
            Assert.IsFalse(result, "Should NOT have >= 10 enemies");
        }

        [Test]
        public void EnemyCountAtNodeZeroWhenNoEncounter()
        {
            Setup();
            var runner = _sim.CurrentGameState.Runners[0];

            // No encounter created yet
            var ctx = new EvaluationContext(runner, _sim.CurrentGameState, _config);
            var condition = Condition.EnemyCountAtNode(ComparisonOperator.Equal, 0);
            bool result = RuleEvaluator.EvaluateCondition(condition, ctx);
            Assert.IsTrue(result, "Should have 0 enemies when no encounter exists");
        }

        // ─── AllyCountAtNode (Micro) ───────────────────────

        [Test]
        public void AllyCountAtNodeCountsOtherRunnersAtSameNode()
        {
            Setup(runnerCount: 3);

            // All runners start at goblin_camp
            var runner0 = _sim.CurrentGameState.Runners[0];
            var runner1 = _sim.CurrentGameState.Runners[1];
            var runner2 = _sim.CurrentGameState.Runners[2];

            var ctx = new EvaluationContext(runner0, _sim.CurrentGameState, _config);
            var condition = Condition.AllyCountAtNode(ComparisonOperator.GreaterOrEqual, 2);
            bool result = RuleEvaluator.EvaluateCondition(condition, ctx);
            Assert.IsTrue(result, "Runner0 should see 2 allies (Runner1, Runner2) at the same node");

            // Move one runner away
            runner2.CurrentNodeId = "hub";
            result = RuleEvaluator.EvaluateCondition(condition, ctx);
            Assert.IsFalse(result, "Runner0 should only see 1 ally after Runner2 moved to hub");

            var singleAllyCondition = Condition.AllyCountAtNode(ComparisonOperator.GreaterOrEqual, 1);
            result = RuleEvaluator.EvaluateCondition(singleAllyCondition, ctx);
            Assert.IsTrue(result, "Runner0 should see 1 ally (Runner1) at the same node");
        }

        // ─── AlliesInCombatAtNode (Micro) ──────────────────

        [Test]
        public void AlliesInCombatAtNodeCountsFightingRunners()
        {
            Setup(runnerCount: 3);

            var runner0 = _sim.CurrentGameState.Runners[0];
            var runner1 = _sim.CurrentGameState.Runners[1];
            var runner2 = _sim.CurrentGameState.Runners[2];

            // Runner1 is fighting, Runner2 is idle
            runner1.State = RunnerState.Fighting;
            runner1.Fighting = new FightingState { NodeId = "goblin_camp" };
            runner2.State = RunnerState.Idle;

            var ctx = new EvaluationContext(runner0, _sim.CurrentGameState, _config);
            var condition = Condition.AlliesInCombatAtNode(ComparisonOperator.GreaterOrEqual, 1);
            bool result = RuleEvaluator.EvaluateCondition(condition, ctx);
            Assert.IsTrue(result, "Runner0 should see 1 ally in combat (Runner1)");

            var twoInCombat = Condition.AlliesInCombatAtNode(ComparisonOperator.GreaterOrEqual, 2);
            result = RuleEvaluator.EvaluateCondition(twoInCombat, ctx);
            Assert.IsFalse(result, "Should not have 2 allies in combat (only Runner1)");
        }

        // ─── Combat Condition: EnemyCountAtNode ─────────────

        [Test]
        public void CombatConditionEnemyCountAtNode()
        {
            Setup();
            var runner = _sim.CurrentGameState.Runners[0];

            // Create encounter with enemies
            var encounter = new EncounterState("goblin_camp") { IsActive = true };
            encounter.Enemies.Add(new EnemyInstance { InstanceId = "e1", ConfigId = "goblin_grunt", CurrentHp = 10f });
            encounter.Enemies.Add(new EnemyInstance { InstanceId = "e2", ConfigId = "goblin_grunt", CurrentHp = 0f }); // dead
            encounter.Enemies.Add(new EnemyInstance { InstanceId = "e3", ConfigId = "goblin_grunt", CurrentHp = 20f });
            _sim.CurrentGameState.EncounterStates["goblin_camp"] = encounter;

            var combatCtx = new CombatEvaluationContext(runner, encounter, _sim.CurrentGameState, _config);
            var condition = CombatCondition.EnemyCountAtNode(ComparisonOperator.Equal, 2);
            bool result = CombatStyleEvaluator.EvaluateCombatCondition(condition, combatCtx);
            Assert.IsTrue(result, "Should count 2 alive enemies (one is dead)");
        }

        // ─── Combat Condition: AlliesInCombatAtNode ─────────

        [Test]
        public void CombatConditionAlliesInCombatAtNode()
        {
            Setup(runnerCount: 2);
            var runner0 = _sim.CurrentGameState.Runners[0];
            var runner1 = _sim.CurrentGameState.Runners[1];

            runner1.State = RunnerState.Fighting;
            runner1.Fighting = new FightingState { NodeId = "goblin_camp" };

            var encounter = new EncounterState("goblin_camp") { IsActive = true };
            _sim.CurrentGameState.EncounterStates["goblin_camp"] = encounter;

            var combatCtx = new CombatEvaluationContext(runner0, encounter, _sim.CurrentGameState, _config);
            var condition = CombatCondition.AlliesInCombatAtNode(ComparisonOperator.GreaterOrEqual, 1);
            bool result = CombatStyleEvaluator.EvaluateCombatCondition(condition, combatCtx);
            Assert.IsTrue(result, "Should see 1 ally in combat");
        }
    }
}
