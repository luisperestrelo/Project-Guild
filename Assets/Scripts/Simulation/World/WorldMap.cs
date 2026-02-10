using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Gathering;

namespace ProjectGuild.Simulation.World
{
    /// <summary>
    /// A location in the world that runners can travel to and interact with.
    /// Nodes are connected by edges with travel distances.
    /// </summary>
    [Serializable]
    public class WorldNode
    {
        public string Id;
        public string Name;

        /// <summary>
        /// Position in world-space for visual placement. X and Z only (Y is ground level).
        /// This is a simple 2D coordinate for the world map — the view layer converts
        /// this to a 3D position.
        /// </summary>
        public float WorldX;
        public float WorldZ;

        /// <summary>
        /// Node color for visual representation (RGB, 0-1 range).
        /// Stored as floats to keep the simulation layer free of UnityEngine dependencies.
        /// The view layer constructs a Color from these values.
        /// </summary>
        public float ColorR = 0.5f;
        public float ColorG = 0.5f;
        public float ColorB = 0.5f;

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

        /// <summary>
        /// The node ID of the hub (home base). Used by BeginAutoReturn to know
        /// where to send runners for deposits. Set during map creation.
        /// </summary>
        public string HubNodeId;

        /// <summary>
        /// Multiplier applied to Euclidean distances when no edge path exists.
        /// Converts map-UI position units into gameplay travel distance.
        /// Authored edges are NOT affected by this — they already have tuned values.
        /// </summary>
        public float TravelDistanceScale = 0.5f;

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
        /// Euclidean distance between two nodes based on their world positions.
        /// Always returns a positive value if both nodes exist, -1 if either is missing.
        /// Used as a fallback when no edge path exists.
        /// </summary>
        public float GetEuclideanDistance(string fromNodeId, string toNodeId)
        {
            var from = GetNode(fromNodeId);
            var to = GetNode(toNodeId);
            if (from == null || to == null) return -1f;

            float dx = to.WorldX - from.WorldX;
            float dz = to.WorldZ - from.WorldZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
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
            {
                // No edge path — fall back to direct Euclidean distance.
                // For V1 any node can travel to any node regardless of edges.
                float euclidean = GetEuclideanDistance(fromNodeId, toNodeId);
                if (euclidean > 0)
                {
                    path = new List<string> { fromNodeId, toNodeId };
                    return euclidean * TravelDistanceScale;
                }
                return -1f;
            }

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

        public WorldMap AddNode(string id, string name, float worldX = 0f, float worldZ = 0f,
            params GatherableConfig[] gatherables)
        {
            Nodes.Add(new WorldNode
            {
                Id = id,
                Name = name,
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
        /// Topology only — gatherables come from WorldNodeAsset SOs via the WorldMapAsset pipeline.
        /// This method is a fallback when no WorldMapAsset is assigned.
        /// </summary>
        public static WorldMap CreateStarterMap()
        {
            var map = new WorldMap();
            map.HubNodeId = "hub";

            // Hub at origin (blue)
            map.AddNode("hub", "Guild Hall", 0f, 0f);
            map.GetNode("hub").ColorR = 0.2f; map.GetNode("hub").ColorG = 0.6f; map.GetNode("hub").ColorB = 1f;

            // ─── Single-gatherable nodes (tutorial-distance) ────────
            map.AddNode("copper_mine", "Copper Mine", -15f, 10f);
            map.AddNode("pine_forest", "Pine Forest", 10f, 15f);
            map.AddNode("sunlit_pond", "Sunlit Pond", 15f, -5f);
            map.AddNode("herb_garden", "Herb Garden", -10f, -12f);

            // ─── Mixed-gatherable test nodes ────────────────────────
            map.AddNode("overgrown_mine", "Overgrown Mine (Trees first, Ore second)", -20f, -5f);
            map.AddNode("deep_mine", "Deep Mine (Copper + Iron Lv15)", -30f, 15f);
            map.AddNode("lakeside_grove", "Lakeside Grove (Logs, Fish, Herbs)", 20f, 25f);

            // ─── Combat zones ───────────────────────────────────────
            map.AddNode("goblin_camp", "Goblin Camp", -25f, 30f);
            map.AddNode("dark_cavern", "Dark Cavern", 0f, 50f);

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
