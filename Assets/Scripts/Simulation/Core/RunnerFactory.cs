using System;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Creates runners with randomized starting skills and passions.
    ///
    /// Design notes:
    /// - Starting skill levels are randomized within a range (not all level 1)
    /// - No archetypes — skill distribution is mostly random, but odds of better pawns improve throughout progression. 
    /// - Passions are randomly assigned; a runner can have 0 to several
    /// - Names are generated (placeholder system for now)
    /// </summary>
    public static class RunnerFactory
    {
        // Starting skill level range (inclusive)
        private const int MinStartingLevel = 1;
        private const int MaxStartingLevel = 10;

        // Chance for each individual skill to have passion
        private const float PassionChance = 0.2f;

        // Placeholder name parts for generated names
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

        /// <summary>
        /// Create a new runner with randomized skills and passions.
        /// </summary>
        /// <param name="rng">Random number generator (passed in for deterministic testing)</param>
        /// <param name="startingNodeId">The world node where this runner spawns</param>
        public static Runner Create(Random rng, string startingNodeId)
        {
            var runner = new Runner
            {
                Id = Guid.NewGuid().ToString(),
                Name = GenerateName(rng),
                State = RunnerState.Idle,
                CurrentNodeId = startingNodeId,
            };

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                runner.Skills[i].Level = rng.Next(MinStartingLevel, MaxStartingLevel + 1);
                runner.Skills[i].HasPassion = rng.NextDouble() < PassionChance;
            }

            return runner;
        }

        /// <summary>
        /// Create the 3 fixed starting runners. These are identical for all players
        /// (same seed produces same runners).
        /// </summary>
        public static Runner[] CreateStartingRunners(string startingNodeId)
        {
            // Fixed seed so all players get the same 3 starting runners
            var rng = new Random(42);
            return new[]
            {
                Create(rng, startingNodeId),
                Create(rng, startingNodeId),
                Create(rng, startingNodeId),
            };
        }

        private static string GenerateName(Random rng)
        {
            string first = FirstNames[rng.Next(FirstNames.Length)];
            string last = LastNames[rng.Next(LastNames.Length)];
            return $"{first} {last}";
        }
    }
}
