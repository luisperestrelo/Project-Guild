namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// All 15 skills in the game. The integer value is used as an array index
    /// for efficient storage (Runner stores skills as Skill[SkillCount]).
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

        public static string GetDescription(this SkillType skill) => skill switch
        {
            SkillType.Melee => "Effectiveness with melee weapons.",
            SkillType.Ranged => "Effectiveness with bows, crossbows, and thrown weapons.",
            SkillType.Defence => "Reduces incoming damage.",
            SkillType.Hitpoints => "Maximum health.",
            SkillType.Magic => "Effectiveness with offensive spells.",
            SkillType.Restoration => "Primarily healing magic, but principles of life-force can also be used to harm. Uses mana.",
            SkillType.Execution => "Proficiency at raid mechanics. Higher execution means fewer avoidable hits.",
            SkillType.Mining => "Extracting ore and stone.",
            SkillType.Woodcutting => "Harvesting timber.",
            SkillType.Fishing => "Catching fish.",
            SkillType.Foraging => "Gathering herbs, vegetables, and spices.",
            SkillType.Engineering => "Crafting gear, tools, and structures.",
            SkillType.PotionMaking => "Brewing potions and alchemical consumables.",
            SkillType.Cooking => "Preparing stat-boosting meals.",
            SkillType.Athletics => "Movement speed.",
            _ => "",
        };
    }
}
