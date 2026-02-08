using NUnit.Framework;
using ProjectGuild.Simulation.Core;
using System;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class RunnerTests
    {
        private SimulationConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = new SimulationConfig();
        }

        [Test]
        public void Runner_DefaultConstructor_HasAllSkillsAtLevel1()
        {
            var runner = new Runner();

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                Assert.AreEqual(1, runner.Skills[i].Level);
                Assert.AreEqual((SkillType)i, runner.Skills[i].Type);
            }
        }

        [Test]
        public void Runner_GetSkill_ReturnsCorrectSkill()
        {
            var runner = new Runner();
            runner.Skills[(int)SkillType.Mining].Level = 42;

            var skill = runner.GetSkill(SkillType.Mining);

            Assert.AreEqual(42, skill.Level);
            Assert.AreEqual(SkillType.Mining, skill.Type);
        }

        [Test]
        public void Runner_GetEffectiveLevel_WithoutPassion_ReturnsRawLevel()
        {
            var runner = new Runner();
            runner.Skills[(int)SkillType.Melee].Level = 10;
            runner.Skills[(int)SkillType.Melee].HasPassion = false;

            float effective = runner.GetEffectiveLevel(SkillType.Melee, _config);

            Assert.AreEqual(10f, effective);
        }

        [Test]
        public void Runner_GetEffectiveLevel_WithPassion_ReturnsMultipliedLevel()
        {
            var runner = new Runner();
            runner.Skills[(int)SkillType.Melee].Level = 10;
            runner.Skills[(int)SkillType.Melee].HasPassion = true;

            float effective = runner.GetEffectiveLevel(SkillType.Melee, _config);

            Assert.AreEqual(10f * _config.PassionEffectivenessMultiplier, effective, 0.001f);
        }

        // ─── RunnerFactory.Create ────────────────────────────────────

        [Test]
        public void RunnerFactory_Create_ProducesValidRunner()
        {
            var rng = new Random(123);
            var runner = RunnerFactory.Create(rng, _config, "hub");

            Assert.IsNotNull(runner.Id);
            Assert.IsNotNull(runner.Name);
            Assert.AreEqual("hub", runner.CurrentNodeId);
            Assert.AreEqual(RunnerState.Idle, runner.State);
            Assert.AreEqual(SkillTypeExtensions.SkillCount, runner.Skills.Length);

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                Assert.GreaterOrEqual(runner.Skills[i].Level, _config.MinStartingLevel);
                Assert.LessOrEqual(runner.Skills[i].Level, _config.MaxStartingLevel);
            }
        }

        [Test]
        public void RunnerFactory_Create_RespectsConfigRanges()
        {
            var customConfig = new SimulationConfig
            {
                MinStartingLevel = 20,
                MaxStartingLevel = 25,
            };
            var rng = new Random(42);
            var runner = RunnerFactory.Create(rng, customConfig, "hub");

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                Assert.GreaterOrEqual(runner.Skills[i].Level, 20);
                Assert.LessOrEqual(runner.Skills[i].Level, 25);
            }
        }

        // ─── RunnerFactory.CreateFromDefinition ──────────────────────

        [Test]
        public void RunnerFactory_CreateFromDefinition_SetsExactStats()
        {
            var def = new RunnerFactory.RunnerDefinition { Name = "Test Runner" }
                .WithSkill(SkillType.Melee, 15, passion: true)
                .WithSkill(SkillType.Mining, 30)
                .WithSkill(SkillType.Athletics, 7);

            var runner = RunnerFactory.CreateFromDefinition(def);

            Assert.AreEqual("Test Runner", runner.Name);
            Assert.AreEqual(15, runner.GetSkill(SkillType.Melee).Level);
            Assert.IsTrue(runner.GetSkill(SkillType.Melee).HasPassion);
            Assert.AreEqual(30, runner.GetSkill(SkillType.Mining).Level);
            Assert.IsFalse(runner.GetSkill(SkillType.Mining).HasPassion);
            Assert.AreEqual(7, runner.GetSkill(SkillType.Athletics).Level);
            // Skills not explicitly set should be level 1
            Assert.AreEqual(1, runner.GetSkill(SkillType.Cooking).Level);
        }

        [Test]
        public void RunnerFactory_CreateFromDefinition_DefaultsToHub()
        {
            var def = new RunnerFactory.RunnerDefinition { Name = "Hubber" };
            var runner = RunnerFactory.CreateFromDefinition(def);

            Assert.AreEqual("hub", runner.CurrentNodeId);
        }

        // ─── RunnerFactory.CreateBiased ──────────────────────────────

        [Test]
        public void RunnerFactory_CreateBiased_GuaranteesPassionOnPoolSkill()
        {
            var rng = new Random(42);
            var bias = new RunnerFactory.BiasConstraints
            {
                GuaranteedPassionPool = new[]
                {
                    SkillType.Mining, SkillType.Woodcutting,
                    SkillType.Fishing, SkillType.Foraging,
                },
            };

            var runner = RunnerFactory.CreateBiased(rng, _config, bias);

            // At least one gathering skill must have passion
            bool anyGatheringPassion = false;
            foreach (var skillType in bias.GuaranteedPassionPool)
            {
                if (runner.GetSkill(skillType).HasPassion)
                {
                    anyGatheringPassion = true;
                    // And that skill should be in the upper half of the range
                    int midpoint = (_config.MinStartingLevel + _config.MaxStartingLevel) / 2;
                    Assert.GreaterOrEqual(runner.GetSkill(skillType).Level, midpoint + 1);
                    break;
                }
            }
            Assert.IsTrue(anyGatheringPassion);
        }

        [Test]
        public void RunnerFactory_CreateBiased_WeakensSpecifiedSkills()
        {
            var rng = new Random(42);
            int midpoint = (_config.MinStartingLevel + _config.MaxStartingLevel) / 2;
            var bias = new RunnerFactory.BiasConstraints
            {
                WeakenedSkills = new[] { SkillType.Melee, SkillType.Ranged, SkillType.Magic },
            };

            // Run multiple times to verify the constraint holds (RNG could get lucky once)
            for (int trial = 0; trial < 20; trial++)
            {
                var runner = RunnerFactory.CreateBiased(new Random(trial), _config, bias);
                foreach (var weakSkill in bias.WeakenedSkills)
                {
                    Assert.LessOrEqual(runner.GetSkill(weakSkill).Level, midpoint);
                }
            }
        }

        [Test]
        public void RunnerFactory_CreateBiased_ForcedNameOverridesGeneration()
        {
            var rng = new Random(42);
            var bias = new RunnerFactory.BiasConstraints { ForcedName = "Special Runner" };

            var runner = RunnerFactory.CreateBiased(rng, _config, bias);

            Assert.AreEqual("Special Runner", runner.Name);
        }
    }
}
