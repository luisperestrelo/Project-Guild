using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// Per-node combat encounter state. Created when a runner starts fighting at a node,
    /// cleaned up when the last runner leaves or dies.
    /// </summary>
    [Serializable]
    public class EncounterState
    {
        /// <summary>
        /// Which node this encounter is at.
        /// </summary>
        public string NodeId;

        /// <summary>
        /// All enemy instances in this encounter (alive and dead/respawning).
        /// </summary>
        public List<EnemyInstance> Enemies = new();

        /// <summary>
        /// Whether this encounter is currently active (has at least one runner fighting).
        /// </summary>
        public bool IsActive;

        public EncounterState() { }

        public EncounterState(string nodeId)
        {
            NodeId = nodeId;
            IsActive = true;
        }

        /// <summary>
        /// Get all alive enemies in this encounter.
        /// </summary>
        public List<EnemyInstance> GetAliveEnemies()
        {
            var alive = new List<EnemyInstance>();
            foreach (var enemy in Enemies)
            {
                if (enemy.IsAlive)
                    alive.Add(enemy);
            }
            return alive;
        }

        /// <summary>
        /// Find an enemy instance by its unique ID.
        /// </summary>
        public EnemyInstance FindEnemy(string instanceId)
        {
            foreach (var enemy in Enemies)
            {
                if (enemy.InstanceId == instanceId)
                    return enemy;
            }
            return null;
        }
    }
}
