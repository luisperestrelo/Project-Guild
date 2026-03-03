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

        [Header("Runner Positioning")]
        [Tooltip("Where runners appear when entering this node. " +
            "Multiple points for multiple runners arriving simultaneously.")]
        [SerializeField] private Transform[] _spawnPoints = new Transform[0];

        [Tooltip("Gathering positions grouped by gatherable index. " +
            "Element 0 = spots for gatherable 0 (e.g. ore veins), element 1 = spots for gatherable 1, etc. " +
            "Each group can have multiple spots so runners don't stack on each other.")]
        [SerializeField] private GatherableSpotGroup[] _gatheringSpotGroups = new GatherableSpotGroup[0];

        public string NodeId => _nodeId;
        public Transform[] SpawnPoints => _spawnPoints;
        public GatherableSpotGroup[] GatheringSpotGroups => _gatheringSpotGroups;

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

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_nodeId))
                Debug.LogWarning($"[NodeSceneRoot] '{gameObject.name}' has no NodeId assigned.", this);
        }
    }
}
