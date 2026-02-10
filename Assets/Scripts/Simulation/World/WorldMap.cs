using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Gathering;

namespace ProjectGuild.Simulation.World
{
    /// <summary>
    /// Cosmetic/categorical label for a world node. Used for visuals, UI icons,
    /// and flavor — NOT for gameplay logic. What you can do at a node is determined
    /// by its data (Gatherables[], future combat configs, etc.), not its type.
    /// </summary>
    public enum NodeType
    {
        Hub,        // Home base — bank, crafting stations, respawn point
        Mine,       // Mining location
        Forest,     // Woodcutting location
        Lake,       // Fishing location
        HerbPatch,  // Foraging location
        MobZone,    // Overworld mob farming
        Raid,       // Raid entrance
    }

    /// <summary>
    /// A location in the world that runners can travel to and interact with.
    /// Nodes are connected by edges with travel distances.
    /// </summary>
    [Serializable]
    public class WorldNode
    {
        public string Id;
        public string Name;
        public NodeType Type;

        /// <summary>
        /// Position in world-space for visual placement. X and Z only (Y is ground level).
        /// This is a simple 2D coordinate for the world map — the view layer converts
        /// this to a 3D position.
        /// </summary>
        public float WorldX;
        public float WorldZ;

        /// <summary>
        /// Gatherables available at this node. Empty for non-gathering nodes (hub, raids, etc.).
        /// A node can have multiple gatherables with different level requirements —
        /// e.g. a mine with copper (level 1) and iron (level 15).
        /// The automation layer decides which gatherable a runner works on.
        /// </summary>
        public GatherableConfig[] Gatherables = Array.Empty<GatherableConfig>();
    }

    /// <summary>
    /// A connection between two world nodes with a travel distance.
    /// Edges are bidirectional — if A connects to B, travel works both ways.
    /// </summary>
    [Serializable]
    public class WorldEdge
    {
        public string NodeIdA;
        public string NodeIdB;
        public float Distance;
    }

    /// <summary>
    /// The world map: a graph of nodes connected by edges. Handles pathfinding
    /// (currently just direct connections, will expand to multi-hop later if needed)
    /// and provides node lookups.
    ///
    /// The map is defined as data and loaded at startup — it doesn't change during gameplay
    /// (except for portals, which are a late-game feature that adds new edges).
    /// </summary>
    [Serializable]
    public class WorldMap
    {
        public List<WorldNode> Nodes = new();
        public List<WorldEdge> Edges = new();

        // Runtime lookups (not serialized)
        [NonSerialized] private Dictionary<string, WorldNode> _nodeLookup;
        [NonSerialized] private Dictionary<string, List<WorldEdge>> _adjacency;

        /// <summary>
        /// Must be called after construction or deserialization to build runtime lookups.
        /// </summary>
        public void Initialize()
        {
            _nodeLookup = new Dictionary<string, WorldNode>();
            _adjacency = new Dictionary<string, List<WorldEdge>>();

            foreach (var node in Nodes)
            {
                _nodeLookup[node.Id] = node;
                _adjacency[node.Id] = new List<WorldEdge>();
            }

            foreach (var edge in Edges)
            {
                if (_adjacency.ContainsKey(edge.NodeIdA))
                    _adjacency[edge.NodeIdA].Add(edge);
                if (_adjacency.ContainsKey(edge.NodeIdB))
                    _adjacency[edge.NodeIdB].Add(edge);
            }
        }

        public WorldNode GetNode(string nodeId)
        {
            if (_nodeLookup == null) Initialize();
            return _nodeLookup.TryGetValue(nodeId, out var node) ? node : null;
        }

        /// <summary>
        /// Get the direct travel distance between two connected nodes.
        /// Returns -1 if nodes are not directly connected.
        /// </summary>
        public float GetDirectDistance(string fromNodeId, string toNodeId)
        {
            if (_adjacency == null) Initialize();
            if (!_adjacency.TryGetValue(fromNodeId, out var edges)) return -1f;

            foreach (var edge in edges)
            {
                if (edge.NodeIdA == toNodeId || edge.NodeIdB == toNodeId)
                    return edge.Distance;
            }
            return -1f;
        }

        /// <summary>
        /// Find the shortest path between two nodes using Dijkstra's algorithm.
        /// Returns the total distance, or -1 if no path exists.
        /// Also outputs the path as a list of node IDs (including start and end).
        /// </summary>
        public float FindPath(string fromNodeId, string toNodeId, out List<string> path)
        {
            if (_adjacency == null) Initialize();
            path = null;

            if (!_adjacency.ContainsKey(fromNodeId) || !_adjacency.ContainsKey(toNodeId))
                return -1f;

            if (fromNodeId == toNodeId)
            {
                path = new List<string> { fromNodeId };
                return 0f;
            }

            // Dijkstra's
            var dist = new Dictionary<string, float>();
            var prev = new Dictionary<string, string>();
            var visited = new HashSet<string>();

            // Simple priority "queue" — fine for small maps (< 100 nodes)
            var unvisited = new List<string>();

            foreach (var node in Nodes)
            {
                dist[node.Id] = float.MaxValue;
                unvisited.Add(node.Id);
            }
            dist[fromNodeId] = 0f;

            while (unvisited.Count > 0)
            {
                // Find unvisited node with smallest distance
                string current = null;
                float currentDist = float.MaxValue;
                foreach (var id in unvisited)
                {
                    if (dist[id] < currentDist)
                    {
                        current = id;
                        currentDist = dist[id];
                    }
                }

                if (current == null || currentDist == float.MaxValue)
                    break; // No path

                if (current == toNodeId)
                    break; // Found it

                unvisited.Remove(current);
                visited.Add(current);

                foreach (var edge in _adjacency[current])
                {
                    string neighbor = edge.NodeIdA == current ? edge.NodeIdB : edge.NodeIdA;
                    if (visited.Contains(neighbor)) continue;

                    float newDist = dist[current] + edge.Distance;
                    if (newDist < dist[neighbor])
                    {
                        dist[neighbor] = newDist;
                        prev[neighbor] = current;
                    }
                }
            }

            if (!prev.ContainsKey(toNodeId) && fromNodeId != toNodeId)
                return -1f;

            // Reconstruct path
            path = new List<string>();
            string step = toNodeId;
            while (step != null)
            {
                path.Insert(0, step);
                prev.TryGetValue(step, out step);
            }

            return dist[toNodeId];
        }

        // ─── Builder helpers for creating maps in code ───────────────

        public WorldMap AddNode(string id, string name, NodeType type, float worldX = 0f, float worldZ = 0f, params GatherableConfig[] gatherables)
        {
            Nodes.Add(new WorldNode
            {
                Id = id,
                Name = name,
                Type = type,
                WorldX = worldX,
                WorldZ = worldZ,
                Gatherables = gatherables ?? Array.Empty<GatherableConfig>(),
            });
            // Invalidate lookups so they rebuild on next access
            _nodeLookup = null;
            _adjacency = null;
            return this;
        }

        public WorldMap AddEdge(string nodeIdA, string nodeIdB, float distance)
        {
            Edges.Add(new WorldEdge
            {
                NodeIdA = nodeIdA,
                NodeIdB = nodeIdB,
                Distance = distance,
            });
            _nodeLookup = null;
            _adjacency = null;
            return this;
        }

        /// <summary>
        /// Create a small starter map for testing and early development.
        /// Hub in the center, gathering nodes nearby, mixed-gatherable test nodes, one mob zone further out.
        /// Topology only — gatherables are wired onto nodes from Config.NodeGatherables
        /// after map creation (see GameSimulation.WireNodeGatherables).
        /// </summary>
        public static WorldMap CreateStarterMap()
        {
            var map = new WorldMap();

            // Hub at origin
            map.AddNode("hub", "Guild Hall", NodeType.Hub, 0f, 0f);

            // ─── Single-gatherable nodes (tutorial-distance) ────────
            map.AddNode("copper_mine", "Copper Mine", NodeType.Mine, -15f, 10f);
            map.AddNode("pine_forest", "Pine Forest", NodeType.Forest, 10f, 15f);
            map.AddNode("sunlit_pond", "Sunlit Pond", NodeType.Lake, 15f, -5f);
            map.AddNode("herb_garden", "Herb Garden", NodeType.HerbPatch, -10f, -12f);

            // ─── Mixed-gatherable test nodes ────────────────────────
            // "Overgrown Mine": trees grew over this mine — index 0 = pine logs, index 1 = copper ore.
            // Sending a runner here with default (index 0) will chop trees, not mine ore.
            map.AddNode("overgrown_mine", "Overgrown Mine (Trees first, Ore second)", NodeType.Mine, -20f, -5f);

            // "Deep Mine": copper (index 0, no level req) + iron (index 1, requires Mining 15).
            // Low-level runner can only mine copper. Tests MinLevel gating on specific indices.
            map.AddNode("deep_mine", "Deep Mine (Copper + Iron Lv15)", NodeType.Mine, -30f, 15f);

            // "Lakeside Grove": pine logs (index 0) + raw trout (index 1) + sage leaves (index 2).
            // Three gatherables, three different skills at one node.
            map.AddNode("lakeside_grove", "Lakeside Grove (Logs, Fish, Herbs)", NodeType.Forest, 20f, 25f);

            // ─── Combat zones ───────────────────────────────────────
            map.AddNode("goblin_camp", "Goblin Camp", NodeType.MobZone, -25f, 30f);
            map.AddNode("dark_cavern", "Dark Cavern", NodeType.Raid, 0f, 50f);

            // ─── Edges ──────────────────────────────────────────────
            // Nearby nodes: ~5-10 seconds of travel
            map.AddEdge("hub", "copper_mine", 8f);
            map.AddEdge("hub", "pine_forest", 7f);
            map.AddEdge("hub", "sunlit_pond", 6f);
            map.AddEdge("hub", "herb_garden", 7f);

            // Mixed test nodes: slightly further (~10-15 seconds)
            map.AddEdge("hub", "overgrown_mine", 10f);
            map.AddEdge("hub", "deep_mine", 12f);
            map.AddEdge("hub", "lakeside_grove", 11f);

            // Mob zone: ~20 seconds from hub, or shortcut through mine
            map.AddEdge("hub", "goblin_camp", 20f);
            map.AddEdge("copper_mine", "goblin_camp", 14f);

            // Raid: ~40 seconds from hub, or through goblin camp
            map.AddEdge("hub", "dark_cavern", 40f);
            map.AddEdge("goblin_camp", "dark_cavern", 22f);

            // Cross-connections between gathering nodes
            map.AddEdge("copper_mine", "pine_forest", 10f);
            map.AddEdge("pine_forest", "sunlit_pond", 12f);
            map.AddEdge("sunlit_pond", "herb_garden", 9f);
            map.AddEdge("overgrown_mine", "copper_mine", 6f);
            map.AddEdge("deep_mine", "copper_mine", 10f);
            map.AddEdge("lakeside_grove", "pine_forest", 8f);

            map.Initialize();
            return map;
        }
    }
}
