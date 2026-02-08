using NUnit.Framework;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class SkillTests
    {
        private SimulationConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = new SimulationConfig();
        }

        [Test]
        public void Skill_EffectiveLevel_NoPassion_EqualsRawLevel()
        {
            var skill = new Skill { Type = SkillType.Mining, Level = 20, HasPassion = false };

            Assert.AreEqual(20f, skill.GetEffectiveLevel(_config));
        }

        [Test]
        public void Skill_EffectiveLevel_WithPassion_AppliesMultiplier()
        {
            var skill = new Skill { Type = SkillType.Mining, Level = 20, HasPassion = true };

            Assert.AreEqual(20f * _config.PassionEffectivenessMultiplier, skill.GetEffectiveLevel(_config), 0.001f);
        }

        [Test]
        public void Skill_AddXp_NoLevelUp_AccumulatesXp()
        {
            var skill = new Skill { Type = SkillType.Mining, Level = 1, Xp = 0f };
            float xpToNext = skill.GetXpToNextLevel(_config);

            bool leveledUp = skill.AddXp(xpToNext * 0.5f, _config);

            Assert.IsFalse(leveledUp);
            Assert.AreEqual(1, skill.Level);
            Assert.Greater(skill.Xp, 0f);
        }

        [Test]
        public void Skill_AddXp_EnoughForLevelUp_IncrementsLevel()
        {
            var skill = new Skill { Type = SkillType.Mining, Level = 1, Xp = 0f };
            float xpNeeded = skill.GetXpToNextLevel(_config);

            bool leveledUp = skill.AddXp(xpNeeded + 1f, _config);

            Assert.IsTrue(leveledUp);
            Assert.AreEqual(2, skill.Level);
        }

        [Test]
        public void Skill_AddXp_EnoughForMultipleLevelUps_SkipsCorrectly()
        {
            var skill = new Skill { Type = SkillType.Mining, Level = 1, Xp = 0f };

            // Add a huge amount of XP
            bool leveledUp = skill.AddXp(100000f, _config);

            Assert.IsTrue(leveledUp);
            Assert.Greater(skill.Level, 5);
        }

        [Test]
        public void Skill_AddXp_WithPassion_GainsMoreXp()
        {
            var withPassion = new Skill { Type = SkillType.Mining, Level = 1, Xp = 0f, HasPassion = true };
            var without = new Skill { Type = SkillType.Mining, Level = 1, Xp = 0f, HasPassion = false };

            withPassion.AddXp(100f, _config);
            without.AddXp(100f, _config);

            // Passionate learner should have gained more XP (or leveled further)
            Assert.Greater(
                withPassion.Level * 1000 + withPassion.Xp,
                without.Level * 1000 + without.Xp
            );
        }

        [Test]
        public void Skill_LevelProgress_IsZeroToOne()
        {
            var skill = new Skill { Type = SkillType.Mining, Level = 5, Xp = 0f };

            Assert.AreEqual(0f, skill.GetLevelProgress(_config), 0.001f);

            skill.Xp = skill.GetXpToNextLevel(_config) * 0.5f;
            Assert.AreEqual(0.5f, skill.GetLevelProgress(_config), 0.01f);
        }

        [Test]
        public void Skill_XpToNextLevel_IncreasesWithLevel()
        {
            var low = new Skill { Type = SkillType.Mining, Level = 1 };
            var high = new Skill { Type = SkillType.Mining, Level = 50 };

            Assert.Greater(high.GetXpToNextLevel(_config), low.GetXpToNextLevel(_config));
        }

        [Test]
        public void SkillType_IsCombat_CorrectForAllTypes()
        {
            Assert.IsTrue(SkillType.Melee.IsCombat());
            Assert.IsTrue(SkillType.Execution.IsCombat());
            Assert.IsFalse(SkillType.Mining.IsCombat());
            Assert.IsFalse(SkillType.Athletics.IsCombat());
        }

        [Test]
        public void SkillType_IsGathering_CorrectForAllTypes()
        {
            Assert.IsTrue(SkillType.Mining.IsGathering());
            Assert.IsTrue(SkillType.Foraging.IsGathering());
            Assert.IsFalse(SkillType.Melee.IsGathering());
            Assert.IsFalse(SkillType.Cooking.IsGathering());
        }

        [Test]
        public void Skill_ChangingConfigMultiplier_AffectsEffectiveLevel()
        {
            var skill = new Skill { Type = SkillType.Mining, Level = 10, HasPassion = true };
            var customConfig = new SimulationConfig { PassionEffectivenessMultiplier = 1.20f };

            Assert.AreEqual(12f, skill.GetEffectiveLevel(customConfig), 0.001f);
        }
    }
}
