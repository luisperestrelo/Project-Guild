using System;
using UnityEngine;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Data
{
    /// <summary>
    /// ScriptableObject for authoring a world map in the Unity inspector.
    /// Composes WorldNodeAssets into a connected graph with edges.
    /// The hub node is identified by a direct SO reference — no string matching.
    ///
    /// The simulation layer receives a plain C# WorldMap at runtime via ToWorldMap().
    /// </summary>
    [CreateAssetMenu(fileName = "New World Map", menuName = "Project Guild/World Map")]
    public class WorldMapAsset : ScriptableObject
    {
        [Tooltip("The hub node (home base). Runners auto-return here to deposit items.")]
        public WorldNodeAsset HubNode;

        [Tooltip("All nodes in this map. The hub node should also be included in this list.")]
        public WorldNodeAsset[] Nodes = new WorldNodeAsset[0];

        [Tooltip("Optional edge overrides. Each edge is bidirectional.\n" +
            "If two nodes have no edge, travel distance defaults to Euclidean distance between their map positions.\n" +
            "Add edges to define custom travel distances, or leave empty to use Euclidean for everything.")]
        public WorldEdgeEntry[] Edges = new WorldEdgeEntry[0];

        /// <summary>
        /// A connection between two nodes with a travel distance.
        /// Uses direct SO references — no string-ID matching.
        /// </summary>
        [Serializable]
        public struct WorldEdgeEntry
        {
            [Tooltip("First node in the connection.")]
            public WorldNodeAsset NodeA;

            [Tooltip("Second node in the connection.")]
            public WorldNodeAsset NodeB;

            [Tooltip("Travel distance between the two nodes (at base speed 1.0/sec, distance ≈ seconds).")]
            public float Distance;
        }

        private void OnValidate()
        {
            if (HubNode == null)
                Debug.LogWarning($"[WorldMapAsset] '{name}' has no Hub Node assigned.", this);

            // Check hub is in the nodes list
            if (HubNode != null && Nodes != null)
            {
                bool hubFound = false;
                for (int i = 0; i < Nodes.Length; i++)
                {
                    if (Nodes[i] == HubNode) { hubFound = true; break; }
                }
                if (!hubFound)
                    Debug.LogWarning($"[WorldMapAsset] Hub node '{HubNode.Id}' is not in the Nodes list.", this);
            }

            // Check for duplicate node IDs
            if (Nodes != null)
            {
                var seen = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < Nodes.Length; i++)
                {
                    if (Nodes[i] == null) continue;
                    if (!seen.Add(Nodes[i].Id))
                        Debug.LogWarning($"[WorldMapAsset] Duplicate node ID '{Nodes[i].Id}' at index {i}.", this);
                }
            }

            // Check edges reference nodes that are in the list
            if (Edges != null && Nodes != null)
            {
                var nodeSet = new System.Collections.Generic.HashSet<WorldNodeAsset>();
                for (int i = 0; i < Nodes.Length; i++)
                {
                    if (Nodes[i] != null) nodeSet.Add(Nodes[i]);
                }

                for (int i = 0; i < Edges.Length; i++)
                {
                    if (Edges[i].NodeA != null && !nodeSet.Contains(Edges[i].NodeA))
                        Debug.LogWarning($"[WorldMapAsset] Edge {i}: NodeA '{Edges[i].NodeA.Id}' is not in the Nodes list.", this);
                    if (Edges[i].NodeB != null && !nodeSet.Contains(Edges[i].NodeB))
                        Debug.LogWarning($"[WorldMapAsset] Edge {i}: NodeB '{Edges[i].NodeB.Id}' is not in the Nodes list.", this);
                }
            }
        }

        /// <summary>
        /// Convert to the plain C# WorldMap used by the simulation.
        /// All nodes are converted with their gatherables already populated.
        /// Edges resolve SO references to string IDs.
        /// </summary>
        public WorldMap ToWorldMap()
        {
            var map = new WorldMap();

            // Set hub
            map.HubNodeId = HubNode != null ? HubNode.Id : "hub";

            // Convert nodes (validate unique IDs)
            var seenIds = new System.Collections.Generic.HashSet<string>();
            foreach (var nodeAsset in Nodes)
            {
                if (nodeAsset == null) continue;
                if (!seenIds.Add(nodeAsset.Id))
                {
                    Debug.LogError($"[WorldMapAsset] Duplicate node ID '{nodeAsset.Id}' — skipping. Each node must have a unique ID.");
                    continue;
                }
                map.Nodes.Add(nodeAsset.ToWorldNode());
            }

            // Convert edges (resolve SO references to string IDs)
            foreach (var edge in Edges)
            {
                if (edge.NodeA == null || edge.NodeB == null) continue;
                map.Edges.Add(new WorldEdge
                {
                    NodeIdA = edge.NodeA.Id,
                    NodeIdB = edge.NodeB.Id,
                    Distance = edge.Distance,
                });
            }

            map.Initialize();
            return map;
        }
    }
}
