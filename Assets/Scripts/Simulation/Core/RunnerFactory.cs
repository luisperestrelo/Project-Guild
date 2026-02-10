using System;
using System.Collections.Generic;
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
        /// Create a runner from an exact definition. No RNG involved.
        /// </summary>
        public static Runner CreateFromDefinition(RunnerDefinition def, string startingNodeId = "hub",
            int inventorySize = 28)
        {
            var runner = new Runner
            {
                Id = Guid.NewGuid().ToString(),
                Name = def.Name,
                State = RunnerState.Idle,
                CurrentNodeId = startingNodeId,
                Inventory = new Inventory(inventorySize),
            };

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                runner.Skills[i].Level = def.SkillLevels[i];
                runner.Skills[i].HasPassion = def.SkillPassions[i];
            }

            return runner;
        }

        // ─── Biased Creation ─────────────────────────────────────────

        /// <summary>
        /// Constraints for biased runner generation. Fields left null use default random behavior.
        /// </summary>
        public class BiasConstraints
        {
            /// <summary>
            /// If set, one random skill from this list will be guaranteed to have
            /// passion and a level in the upper half of the range.
            /// Currently this is intended to be used for theh tutorial-reward pawn, who's going to be skewed towards 1 gathering skill
            /// </summary>
            public SkillType[] GuaranteedPassionPool;

            /// <summary>
            /// Skills in this list will have their levels reduced (lower half of range).
            /// </summary>
            public SkillType[] WeakenedSkills;

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

            // Weaken specified skills (lower half of starting range)
            if (bias.WeakenedSkills != null)
            {
                int midpoint = (config.MinStartingLevel + config.MaxStartingLevel) / 2;
                foreach (var skill in bias.WeakenedSkills)
                {
                    int idx = (int)skill;
                    runner.Skills[idx].Level = rng.Next(config.MinStartingLevel, midpoint + 1);
                }
            }

            // Guarantee passion + decent level on one random skill from the pool
            if (bias.GuaranteedPassionPool != null && bias.GuaranteedPassionPool.Length > 0)
            {
                var chosen = bias.GuaranteedPassionPool[rng.Next(bias.GuaranteedPassionPool.Length)];
                int idx = (int)chosen;
                runner.Skills[idx].HasPassion = true;

                // Upper half of the starting range
                int midpoint = (config.MinStartingLevel + config.MaxStartingLevel) / 2;
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
