using System;
using UnityEngine;

namespace ProjectGuild.View
{
    /// <summary>
    /// A group of physical positions for one gatherable type within a node scene.
    /// E.g. a copper mine might have 4 ore vein positions — all for gatherable index 0.
    /// Multiple runners gathering the same resource get spread across these spots.
    /// </summary>
    [Serializable]
    public class GatherableSpotGroup
    {
        [Tooltip("Physical positions for this gatherable (e.g. ore veins, tree stumps, fishing spots). " +
            "Multiple runners gathering this resource get spread across these spots.")]
        public Transform[] Spots = new Transform[0];
    }

    /// <summary>
    /// Attached to the root GameObject in each additive node scene.
    /// WorldSceneManager finds this after loading to position the scene
    /// and to locate spawn/gathering points within it.
    ///
    /// Each node scene has exactly one of these on its root object.
    /// Author scenes at local origin — WorldSceneManager moves the root
    /// to the correct world-space offset at load time.
    /// </summary>
    public class NodeSceneRoot : MonoBehaviour
    {
        [Tooltip("Node ID this scene represents (must match WorldNode.Id, e.g. 'copper_mine').")]
        [SerializeField] private string _nodeId;

        [Header("Scene Overview")]
        [Tooltip("The central point of interest in this scene. Used as the camera target " +
            "when viewing this node without following a runner (e.g. Guild Hall button).\n" +
            "Place near the center of the scene's playable area. Falls back to this transform's position if unset.")]
        [SerializeField] private Transform _sceneFocalPoint;

        [Header("Runner Positioning")]
        [Tooltip("Where runners appear when entering this node (round-robin for entrance nodes, " +
            "fallback for circumference nodes when no directional points are set). " +
            "Multiple points for multiple runners arriving simultaneously.")]
        [SerializeField] private Transform[] _spawnPoints = new Transform[0];

        [Tooltip("Directional spawn points around the node perimeter (circumference/area nodes only). " +
            "Runners enter from the point closest to their overworld approach direction. " +
            "Leave empty for entrance nodes (caves, dungeons) — they use _spawnPoints round-robin.\n\n" +
            "Auto-generate these with the editor button, then nudge to sensible positions.")]
        [SerializeField] private Transform[] _directionalSpawnPoints = new Transform[0];

        [Header("Combat")]
        [Tooltip("Positions where enemies spawn in this node's combat area. " +
            "Enemies are spread across these points. Leave empty for non-combat nodes.")]
        [SerializeField] private Transform[] _enemySpawnPoints = new Transform[0];

        [Tooltip("Center of the combat area. Runners in Fighting state are positioned around this point. " +
            "Falls back to scene focal point if unset.")]
        [SerializeField] private Transform _combatAreaCenter;

        [Header("Gathering")]
        [Tooltip("Gathering positions grouped by gatherable index. " +
            "Element 0 = spots for gatherable 0 (e.g. ore veins), element 1 = spots for gatherable 1, etc. " +
            "Each group can have multiple spots so runners don't stack on each other.")]
        [SerializeField] private GatherableSpotGroup[] _gatheringSpotGroups = new GatherableSpotGroup[0];

        public string NodeId => _nodeId;
        public Transform[] SpawnPoints => _spawnPoints;
        public Transform[] DirectionalSpawnPoints => _directionalSpawnPoints;
        public GatherableSpotGroup[] GatheringSpotGroups => _gatheringSpotGroups;
        public Transform[] EnemySpawnPoints => _enemySpawnPoints;

        /// <summary>
        /// Center of the combat area. Runners fighting at this node are positioned around this point.
        /// Falls back to SceneFocalPosition if not explicitly set.
        /// </summary>
        public Vector3 CombatAreaCenter =>
            _combatAreaCenter != null ? _combatAreaCenter.position : SceneFocalPosition;

        /// <summary>
        /// True if this node has directional spawn points configured (circumference/area node).
        /// Entrance nodes leave this empty and use SpawnPoints round-robin instead.
        /// </summary>
        public bool HasDirectionalSpawns => _directionalSpawnPoints != null && _directionalSpawnPoints.Length > 0;

        /// <summary>
        /// Position where runners walk during Deposit step.
        /// Base returns null — overridden by GuildHallSceneRoot.
        /// </summary>
        public virtual Vector3? DepositPointPosition => null;

        /// <summary>
        /// The scene's focal point position. Used as the camera target when viewing
        /// this scene without following a runner. Falls back to this transform's position.
        /// </summary>
        public Vector3 SceneFocalPosition =>
            _sceneFocalPoint != null ? _sceneFocalPoint.position : transform.position;

        /// <summary>
        /// Get the world-space position for a runner gathering at the given gatherable index.
        /// runnerIndexInGroup spreads multiple runners across spots within the same group
        /// (e.g. runner 0 goes to ore vein A, runner 1 goes to ore vein B).
        /// Falls back to spawn points, then to this transform's position.
        /// </summary>
        public Vector3 GetGatheringPosition(int gatherableIndex, int runnerIndexInGroup)
        {
            if (gatherableIndex < _gatheringSpotGroups.Length)
            {
                var group = _gatheringSpotGroups[gatherableIndex];
                if (group != null && group.Spots.Length > 0)
                {
                    int spotIndex = runnerIndexInGroup % group.Spots.Length;
                    if (group.Spots[spotIndex] != null)
                        return group.Spots[spotIndex].position;
                }
            }

            // Fallback: use spawn point
            if (_spawnPoints.Length > 0)
            {
                int spawnIndex = runnerIndexInGroup % _spawnPoints.Length;
                if (_spawnPoints[spawnIndex] != null)
                    return _spawnPoints[spawnIndex].position;
            }

            return transform.position;
        }

        /// <summary>
        /// Get the best directional spawn position for a runner approaching from the given
        /// world-space direction. Selects the point whose direction from the scene focal center
        /// is most aligned with the approach direction (highest dot product).
        ///
        /// Falls back to round-robin GetSpawnPosition if no directional points are configured.
        /// </summary>
        /// <param name="approachDirectionXZ">Flattened XZ direction from destination toward source
        /// (the direction the runner is approaching FROM).</param>
        /// <param name="fallbackArrivalIndex">Round-robin index for fallback spawn.</param>
        public Vector3 GetDirectionalSpawnPosition(Vector3 approachDirectionXZ, int fallbackArrivalIndex)
        {
            if (_directionalSpawnPoints == null || _directionalSpawnPoints.Length == 0)
                return GetSpawnPosition(fallbackArrivalIndex);

            Vector3 focalCenter = SceneFocalPosition;
            float bestDot = float.MinValue;
            Vector3 bestPosition = focalCenter;

            Vector3 approachFlat = new Vector3(approachDirectionXZ.x, 0f, approachDirectionXZ.z);
            if (approachFlat.sqrMagnitude < 0.001f)
                return GetSpawnPosition(fallbackArrivalIndex);
            approachFlat.Normalize();

            for (int i = 0; i < _directionalSpawnPoints.Length; i++)
            {
                if (_directionalSpawnPoints[i] == null) continue;

                Vector3 pointDir = _directionalSpawnPoints[i].position - focalCenter;
                pointDir.y = 0f;
                if (pointDir.sqrMagnitude < 0.001f) continue;
                pointDir.Normalize();

                float dot = Vector3.Dot(approachFlat, pointDir);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestPosition = _directionalSpawnPoints[i].position;
                }
            }

            return bestPosition;
        }

        /// <summary>
        /// Get a spawn position for a runner entering this node.
        /// Spreads across available spawn points by runner arrival order.
        /// </summary>
        public Vector3 GetSpawnPosition(int arrivalIndex)
        {
            if (_spawnPoints.Length > 0)
            {
                int spawnIndex = arrivalIndex % _spawnPoints.Length;
                if (_spawnPoints[spawnIndex] != null)
                    return _spawnPoints[spawnIndex].position;
            }

            return transform.position;
        }

        /// <summary>
        /// Get the exit position for a runner departing this node toward a destination.
        /// Circumference nodes: directional spawn closest to the departure direction.
        /// Entrance nodes: first spawn point (the entrance/cave mouth).
        /// Used by NodeGeometryProvider for exit distance calculation and by arrival direction logic.
        /// </summary>
        public Vector3 GetExitPosition(Vector3 departureDirectionXZ)
        {
            if (HasDirectionalSpawns && departureDirectionXZ.sqrMagnitude > 0.001f)
                return GetDirectionalSpawnPosition(departureDirectionXZ, 0);
            return GetSpawnPosition(0);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_nodeId))
                Debug.LogWarning($"[NodeSceneRoot] '{gameObject.name}' has no NodeId assigned.", this);
        }
    }
}
