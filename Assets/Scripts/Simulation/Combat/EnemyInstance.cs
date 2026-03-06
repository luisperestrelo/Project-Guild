using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// A live enemy in an encounter. Mutable runtime state.
    /// Multiple instances can exist for the same EnemyConfig (e.g. 3 goblin grunts).
    /// </summary>
    [Serializable]
    public class EnemyInstance
    {
        /// <summary>
        /// Unique instance identifier (generated at spawn time).
        /// </summary>
        public string InstanceId;

        /// <summary>
        /// Which enemy type this is (references EnemyConfig.Id).
        /// </summary>
        public string ConfigId;

        /// <summary>
        /// Current hitpoints. Starts at EnemyConfig.MaxHitpoints. Dead when <= 0.
        /// </summary>
        public float CurrentHp;

        /// <summary>
        /// Runner ID that taunted this enemy. Null = no taunt active.
        /// Enemy attacks this runner instead of its normal AI target.
        /// </summary>
        public string TauntedByRunnerId;

        /// <summary>
        /// Ticks remaining on the enemy's current attack/ability. 0 = ready to act.
        /// </summary>
        public int ActionTicksRemaining;

        /// <summary>
        /// Per-ability cooldown tracking. Key = abilityId, Value = ticks remaining.
        /// </summary>
        public Dictionary<string, int> CooldownTrackers = new();

        /// <summary>
        /// Ticks remaining until this enemy respawns. Only used when dead (CurrentHp <= 0).
        /// </summary>
        public int RespawnTicksRemaining;

        /// <summary>
        /// Which spawn entry index this instance belongs to (for respawn tracking).
        /// </summary>
        public int SpawnEntryIndex;

        public bool IsAlive => CurrentHp > 0f;
        public bool IsActing => ActionTicksRemaining > 0;

        public EnemyInstance() { }
    }
}
