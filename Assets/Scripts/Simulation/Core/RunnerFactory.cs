using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Creates runners with various generation strategies:
    /// - Fully random (standard acquisition)
    /// - Hand-tuned (starting runners with exact, predefined stats)
    /// - Biased (semi-deterministic, e.g. tutorial reward runner skewed toward gathering)
    ///
    /// All generation reads tuning values from SimulationConfig rather than hardcoding them.
    /// RNG is always passed in explicitly for deterministic testing.
    /// </summary>
    public static class RunnerFactory
    {
        // ─── Name Lists ──────────────────────────────────────────────

        private static readonly string[] FirstNames =
        {
            "Aldric", "Brenna", "Corin", "Dahlia", "Edric",
            "Faye", "Gareth", "Hilda", "Ivar", "Juna",
            "Kael", "Lyra", "Magnus", "Nessa", "Orin",
            "Petra", "Quinn", "Rowan", "Sable", "Theron",
            "Uma", "Vesper", "Wren", "Xara", "Yorick", "Zara",
        };

        private static readonly string[] LastNames =
        {
            "Ashford", "Blackwood", "Crestfall", "Dawnmere", "Emberlyn",
            "Foxglove", "Greywood", "Holloway", "Ironvale", "Jasperwood",
            "Knightley", "Larkspur", "Mooreland", "Northwind", "Oakshield",
            "Pinecroft", "Quicksilver", "Ravenscroft", "Stormwind", "Thornfield",
        };

        private static readonly string[] EasterEggNames =
        {
            "IMJUNGROAN", "Krillson"
            // Full names go here. These are rolled as-is (no first+last combination).
            // Example: "Luis Perestrelo"
        };

        // ─── Standard Random Creation ────────────────────────────────

        /// <summary>
        /// Create a fully random runner using config-driven ranges.
        /// This is the standard creation path for runners acquired during gameplay.
        /// </summary>
        public static Runner Create(Random rng, SimulationConfig config, string startingNodeId = "hub")
        {
            var runner = new Runner
            {
                Id = Guid.NewGuid().ToString(),
                Name = GenerateName(rng, config),
                State = RunnerState.Idle,
                CurrentNodeId = startingNodeId,
                Inventory = new Inventory(config.InventorySize),
            };

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                runner.Skills[i].Level = rng.Next(config.MinStartingLevel, config.MaxStartingLevel + 1);
                runner.Skills[i].HasPassion = rng.NextDouble() < config.PassionChance;
            }

            runner.MacroRuleset = DefaultRulesets.CreateDefaultMacro();
            runner.MicroRuleset = DefaultRulesets.CreateDefaultMicro();
            return runner;
        }

        // ─── Hand-Tuned Creation ─────────────────────────────────────

        /// <summary>
        /// Definition for a hand-tuned runner with exact stats.
        /// Used for starting runners where we need full control over balance.
        /// </summary>
        public class RunnerDefinition
        {
            public string Name;
            public int[] SkillLevels;      // Length must be SkillCount, indexed by SkillType
            public bool[] SkillPassions;   // Length must be SkillCount, indexed by SkillType

            /// <summary>
            /// Helper to set a specific skill's level and passion in a readable way.
            /// Returns itself for chaining.
            /// </summary>
            public RunnerDefinition WithSkill(SkillType skill, int level, bool passion = false)
            {
                SkillLevels[(int)skill] = level;
                SkillPassions[(int)skill] = passion;
                return this;
            }

            public RunnerDefinition()
            {
                SkillLevels = new int[SkillTypeExtensions.SkillCount];
                SkillPassions = new bool[SkillTypeExtensions.SkillCount];
                // Default all skills to level 1, no passion
                for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
                    SkillLevels[i] = 1;
            }
        }

        /// <summary>
        /// Create a runner from an exact definition.
        /// If Name is null, a random name is generated using the provided RNG and config.
        /// </summary>
        public static Runner CreateFromDefinition(RunnerDefinition def, string startingNodeId = "hub",
            int inventorySize = 28, Random rng = null, SimulationConfig config = null)
        {
            string name = def.Name;
            if (name == null)
            {
                rng ??= new Random();
                config ??= new SimulationConfig();
                name = GenerateName(rng, config);
            }

            var runner = new Runner
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                State = RunnerState.Idle,
                CurrentNodeId = startingNodeId,
                Inventory = new Inventory(inventorySize),
            };

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                runner.Skills[i].Level = def.SkillLevels[i];
                runner.Skills[i].HasPassion = def.SkillPassions[i];
            }

            runner.MacroRuleset = DefaultRulesets.CreateDefaultMacro();
            runner.MicroRuleset = DefaultRulesets.CreateDefaultMicro();
            return runner;
        }

        // ─── Biased Creation ─────────────────────────────────────────

        /// <summary>
        /// Constraints for biased runner generation. Fields left null use default random behavior.
        /// Each field controls exactly one thing and they compose independently.
        /// </summary>
        public class BiasConstraints
        {
            /// <summary>
            /// ONE random skill from this pool gets both a boosted level (upper half of range)
            /// AND passion. Used for the tutorial-reward pawn: e.g. pass in all gathering skills,
            /// and the pawn will come out with one random gathering skill as their clear specialty.
            /// </summary>
            public SkillType[] PickOneSkillToBoostedAndPassionate;

            /// <summary>
            /// ALL listed skills get boosted levels (upper half of starting range).
            /// Does not grant passion — only affects starting level.
            /// </summary>
            public SkillType[] BoostedSkills;

            /// <summary>
            /// ALL listed skills get reduced levels (lower half of starting range).
            /// Does not affect passion — the skill may still randomly have passion.
            /// </summary>
            public SkillType[] WeakenedSkills;

            /// <summary>
            /// ALL listed skills get reduced levels (lower half of starting range)
            /// AND passion is removed. The runner has no talent or interest in these skills.
            /// </summary>
            public SkillType[] WeakenedNoPassionSkills;

            /// <summary>
            /// Optional override name. If null, uses normal name generation.
            /// </summary>
            public string ForcedName;
        }

        /// <summary>
        /// Create a semi-deterministic runner. Most skills are random, but certain
        /// constraints are enforced (e.g. "must have passion for one gathering skill,
        /// combat skills skewed lower").
        ///
        /// Used for scripted moments like the tutorial reward runner.
        /// </summary>
        public static Runner CreateBiased(Random rng, SimulationConfig config, BiasConstraints bias,
            string startingNodeId = "hub")
        {
            var runner = Create(rng, config, startingNodeId);

            // Override name if forced
            if (bias.ForcedName != null)
                runner.Name = bias.ForcedName;

            int midpoint = (config.MinStartingLevel + config.MaxStartingLevel) / 2;

            // Weaken specified skills (lower half of starting range, passion untouched)
            if (bias.WeakenedSkills != null)
            {
                foreach (var skill in bias.WeakenedSkills)
                {
                    int idx = (int)skill;
                    runner.Skills[idx].Level = rng.Next(config.MinStartingLevel, midpoint + 1);
                }
            }

            // Weaken specified skills AND remove passion
            if (bias.WeakenedNoPassionSkills != null)
            {
                foreach (var skill in bias.WeakenedNoPassionSkills)
                {
                    int idx = (int)skill;
                    runner.Skills[idx].Level = rng.Next(config.MinStartingLevel, midpoint + 1);
                    runner.Skills[idx].HasPassion = false;
                }
            }

            // Boost specified skills (upper half of starting range, no passion)
            if (bias.BoostedSkills != null)
            {
                foreach (var skill in bias.BoostedSkills)
                {
                    int idx = (int)skill;
                    runner.Skills[idx].Level = rng.Next(midpoint + 1, config.MaxStartingLevel + 1);
                }
            }

            // Pick one random skill from pool → boosted level + passion
            if (bias.PickOneSkillToBoostedAndPassionate != null && bias.PickOneSkillToBoostedAndPassionate.Length > 0)
            {
                var chosen = bias.PickOneSkillToBoostedAndPassionate[
                    rng.Next(bias.PickOneSkillToBoostedAndPassionate.Length)];
                int idx = (int)chosen;
                runner.Skills[idx].HasPassion = true;
                runner.Skills[idx].Level = rng.Next(midpoint + 1, config.MaxStartingLevel + 1);
            }

            return runner;
        }

        // ─── Name Generation ─────────────────────────────────────────

        private static string GenerateName(Random rng, SimulationConfig config)
        {
            // Roll for easter egg name
            if (EasterEggNames.Length > 0 && rng.NextDouble() < config.EasterEggNameChance)
            {
                return EasterEggNames[rng.Next(EasterEggNames.Length)];
            }

            string first = FirstNames[rng.Next(FirstNames.Length)];
            string last = LastNames[rng.Next(LastNames.Length)];
            return $"{first} {last}";
        }
    }
}
