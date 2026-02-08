using System;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Represents a single skill on a runner. Skills ARE stats — there are no hidden base stats.
    /// Level is the primary progression number. XP accumulates toward the next level.
    /// Passion provides a small effectiveness multiplier and faster XP gain.
    /// </summary>
    [Serializable]
    public class Skill
    {
        public SkillType Type;
        public int Level;
        public float Xp;
        public bool HasPassion;

        /// <summary>
        /// Passion multiplier applied to the effective level.
        /// A runner with level 10 and passion has 10 * 1.05 = 10.5 effective level.
        /// </summary>
        public const float PassionEffectivenessMultiplier = 1.05f;

        /// <summary>
        /// Passion multiplier applied to XP gain rate.
        /// </summary>
        public const float PassionXpMultiplier = 1.5f;

        /// <summary>
        /// The effective level, accounting for passion bonus.
        /// This is the value used in all gameplay calculations and displayed in UI.
        /// </summary>
        public float EffectiveLevel => HasPassion
            ? Level * PassionEffectivenessMultiplier
            : Level;

        /// <summary>
        /// XP required to reach the next level from the current level.
        /// Uses a scaled curve so early levels are fast and later levels are slow.
        /// </summary>
        public float XpToNextLevel => GetXpForLevel(Level + 1) - GetXpForLevel(Level);

        /// <summary>
        /// Progress toward the next level, 0.0 to 1.0.
        /// </summary>
        public float LevelProgress => XpToNextLevel > 0 ? Xp / XpToNextLevel : 0f;

        /// <summary>
        /// Add XP to this skill, applying passion bonus if applicable.
        /// Returns true if the skill leveled up.
        /// </summary>
        public bool AddXp(float baseXp)
        {
            float actualXp = HasPassion ? baseXp * PassionXpMultiplier : baseXp;
            Xp += actualXp;

            bool leveledUp = false;
            while (Xp >= XpToNextLevel)
            {
                Xp -= XpToNextLevel;
                Level++;
                leveledUp = true;
            }

            return leveledUp;
        }

        /// <summary>
        /// Total cumulative XP required to reach a given level from level 0.
        /// Formula: sum of (level^1.5 * 50) for each level.
        /// Level 1 requires 50 XP, level 10 requires ~1581 XP cumulative, etc.
        /// </summary>
        public static float GetXpForLevel(int level)
        {
            float total = 0;
            for (int i = 1; i <= level; i++)
            {
                total += (float)Math.Pow(i, 1.5) * 50f;
            }
            return total;
        }
    }
}
