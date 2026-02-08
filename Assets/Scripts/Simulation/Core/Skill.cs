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
        /// The effective level, accounting for passion bonus.
        /// This is the value used in all gameplay calculations and displayed in UI.
        /// Requires a config reference to know the passion multiplier.
        /// </summary>
        public float GetEffectiveLevel(SimulationConfig config)
        {
            return HasPassion
                ? Level * config.PassionEffectivenessMultiplier
                : Level;
        }

        /// <summary>
        /// XP required to reach the next level from the current level.
        /// </summary>
        public float GetXpToNextLevel(SimulationConfig config)
        {
            return GetXpForLevel(Level + 1, config) - GetXpForLevel(Level, config);
        }

        /// <summary>
        /// Progress toward the next level, 0.0 to 1.0.
        /// </summary>
        public float GetLevelProgress(SimulationConfig config)
        {
            float xpNeeded = GetXpToNextLevel(config);
            return xpNeeded > 0 ? Xp / xpNeeded : 0f;
        }

        /// <summary>
        /// Add XP to this skill, applying passion bonus if applicable.
        /// Returns true if the skill leveled up.
        /// </summary>
        public bool AddXp(float baseXp, SimulationConfig config)
        {
            float actualXp = HasPassion ? baseXp * config.PassionXpMultiplier : baseXp;
            Xp += actualXp;

            bool leveledUp = false;
            float xpToNext = GetXpToNextLevel(config);
            while (Xp >= xpToNext)
            {
                Xp -= xpToNext;
                Level++;
                leveledUp = true;
                xpToNext = GetXpToNextLevel(config);
            }

            return leveledUp;
        }

        /// <summary>
        /// Total cumulative XP required to reach a given level from level 0.
        /// Formula: sum of (level^exponent * base) for each level.
        /// </summary>
        public static float GetXpForLevel(int level, SimulationConfig config)
        {
            float total = 0;
            for (int i = 1; i <= level; i++)
            {
                total += (float)Math.Pow(i, config.XpCurveExponent) * config.XpCurveBase;
            }
            return total;
        }
    }
}
