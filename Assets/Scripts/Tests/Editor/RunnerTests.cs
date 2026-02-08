using NUnit.Framework;
using ProjectGuild.Simulation.Core;
using System;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class RunnerTests
    {
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

            float effective = runner.GetEffectiveLevel(SkillType.Melee);

            Assert.AreEqual(10f, effective);
        }

        [Test]
        public void Runner_GetEffectiveLevel_WithPassion_ReturnsMultipliedLevel()
        {
            var runner = new Runner();
            runner.Skills[(int)SkillType.Melee].Level = 10;
            runner.Skills[(int)SkillType.Melee].HasPassion = true;

            float effective = runner.GetEffectiveLevel(SkillType.Melee);

            Assert.AreEqual(10f * Skill.PassionEffectivenessMultiplier, effective, 0.001f);
        }

        [Test]
        public void RunnerFactory_Create_ProducesValidRunner()
        {
            var rng = new Random(123);
            var runner = RunnerFactory.Create(rng, "hub");

            Assert.IsNotNull(runner.Id);
            Assert.IsNotNull(runner.Name);
            Assert.AreEqual("hub", runner.CurrentNodeId);
            Assert.AreEqual(RunnerState.Idle, runner.State);
            Assert.AreEqual(SkillTypeExtensions.SkillCount, runner.Skills.Length);

            // All skills should be within the starting range
            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                Assert.GreaterOrEqual(runner.Skills[i].Level, 1);
                Assert.LessOrEqual(runner.Skills[i].Level, 10);
            }
        }

        [Test]
        public void RunnerFactory_CreateStartingRunners_ProducesThreeRunners()
        {
            var runners = RunnerFactory.CreateStartingRunners("hub");

            Assert.AreEqual(3, runners.Length);

            foreach (var runner in runners)
            {
                Assert.AreEqual("hub", runner.CurrentNodeId);
                Assert.AreEqual(RunnerState.Idle, runner.State);
            }
        }

        [Test]
        public void RunnerFactory_CreateStartingRunners_IsDeterministic()
        {
            var runners1 = RunnerFactory.CreateStartingRunners("hub");
            var runners2 = RunnerFactory.CreateStartingRunners("hub");

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(runners1[i].Name, runners2[i].Name);
                for (int s = 0; s < SkillTypeExtensions.SkillCount; s++)
                {
                    Assert.AreEqual(runners1[i].Skills[s].Level, runners2[i].Skills[s].Level);
                    Assert.AreEqual(runners1[i].Skills[s].HasPassion, runners2[i].Skills[s].HasPassion);
                }
            }
        }
    }
}
