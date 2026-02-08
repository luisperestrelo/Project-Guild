namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// All 15 skills in the game. The integer value is used as an array index
    /// for efficient storage (Runner stores skills as float[SkillCount]).
    /// </summary>
    public enum SkillType
    {
        // Combat (7)
        Melee = 0,
        Ranged = 1,
        Defence = 2,
        Hitpoints = 3,
        Magic = 4,
        Restoration = 5,
        Execution = 6,

        // Gathering (4)
        Mining = 7,
        Woodcutting = 8,
        Fishing = 9,
        Foraging = 10,

        // Production (3)
        Engineering = 11,
        PotionMaking = 12,
        Cooking = 13,

        // Support (1)
        Athletics = 14,
    }

    public static class SkillTypeExtensions
    {
        public const int SkillCount = 15;

        public static bool IsCombat(this SkillType skill) =>
            skill >= SkillType.Melee && skill <= SkillType.Execution;

        public static bool IsGathering(this SkillType skill) =>
            skill >= SkillType.Mining && skill <= SkillType.Foraging;

        public static bool IsProduction(this SkillType skill) =>
            skill >= SkillType.Engineering && skill <= SkillType.Cooking;
    }
}
