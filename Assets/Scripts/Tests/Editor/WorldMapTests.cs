using NUnit.Framework;
using ProjectGuild.Simulation.World;
using System.Collections.Generic;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class WorldMapTests
    {
        private WorldMap _map;

        [SetUp]
        public void SetUp()
        {
            // Simple test map: hub -> A -> B, hub -> C
            _map = new WorldMap();
            _map.AddNode("hub", "Hub", NodeType.Hub, 0f, 0f);
            _map.AddNode("a", "Node A", NodeType.GatheringMine, 10f, 0f);
            _map.AddNode("b", "Node B", NodeType.MobZone, 20f, 0f);
            _map.AddNode("c", "Node C", NodeType.GatheringForest, 0f, 10f);
            _map.AddNode("isolated", "Isolated", NodeType.GatheringHerbs, 50f, 50f);

            _map.AddEdge("hub", "a", 5f);
            _map.AddEdge("a", "b", 8f);
            _map.AddEdge("hub", "c", 3f);
            // "isolated" has no edges — unreachable

            _map.Initialize();
        }

        [Test]
        public void GetNode_ReturnsCorrectNode()
        {
            var node = _map.GetNode("hub");
            Assert.AreEqual("Hub", node.Name);
            Assert.AreEqual(NodeType.Hub, node.Type);
        }

        [Test]
        public void GetNode_InvalidId_ReturnsNull()
        {
            Assert.IsNull(_map.GetNode("nonexistent"));
        }

        [Test]
        public void GetDirectDistance_ConnectedNodes_ReturnsDistance()
        {
            Assert.AreEqual(5f, _map.GetDirectDistance("hub", "a"));
            Assert.AreEqual(5f, _map.GetDirectDistance("a", "hub")); // Bidirectional
        }

        [Test]
        public void GetDirectDistance_NotDirectlyConnected_ReturnsNegative()
        {
            Assert.AreEqual(-1f, _map.GetDirectDistance("hub", "b"));
        }

        [Test]
        public void FindPath_DirectConnection_ReturnsSingleEdgeDistance()
        {
            float dist = _map.FindPath("hub", "a", out var path);

            Assert.AreEqual(5f, dist);
            Assert.AreEqual(2, path.Count);
            Assert.AreEqual("hub", path[0]);
            Assert.AreEqual("a", path[1]);
        }

        [Test]
        public void FindPath_MultiHop_ReturnsShortestPath()
        {
            float dist = _map.FindPath("hub", "b", out var path);

            // hub -> a -> b = 5 + 8 = 13
            Assert.AreEqual(13f, dist);
            Assert.AreEqual(3, path.Count);
            Assert.AreEqual("hub", path[0]);
            Assert.AreEqual("a", path[1]);
            Assert.AreEqual("b", path[2]);
        }

        [Test]
        public void FindPath_SameNode_ReturnsZero()
        {
            float dist = _map.FindPath("hub", "hub", out var path);

            Assert.AreEqual(0f, dist);
            Assert.AreEqual(1, path.Count);
        }

        [Test]
        public void FindPath_Unreachable_ReturnsNegative()
        {
            float dist = _map.FindPath("hub", "isolated", out var path);

            Assert.AreEqual(-1f, dist);
            Assert.IsNull(path);
        }

        [Test]
        public void StarterMap_HasHubAndNodes()
        {
            var starterMap = WorldMap.CreateStarterMap();

            Assert.IsNotNull(starterMap.GetNode("hub"));
            Assert.IsNotNull(starterMap.GetNode("copper_mine"));
            Assert.IsNotNull(starterMap.GetNode("pine_forest"));
            Assert.IsNotNull(starterMap.GetNode("dark_cavern"));

            // Hub should connect to mine
            float dist = starterMap.GetDirectDistance("hub", "copper_mine");
            Assert.Greater(dist, 0f);
        }

        [Test]
        public void StarterMap_PathfindingWorks()
        {
            var starterMap = WorldMap.CreateStarterMap();

            // Should be able to reach the raid from hub
            float dist = starterMap.FindPath("hub", "dark_cavern", out var path);
            Assert.Greater(dist, 0f);
            Assert.IsNotNull(path);
            Assert.AreEqual("hub", path[0]);
            Assert.AreEqual("dark_cavern", path[path.Count - 1]);
        }

        [Test]
        public void StarterMap_PathfindsShortcutThroughGoblinCamp()
        {
            var starterMap = WorldMap.CreateStarterMap();

            // Direct: hub -> dark_cavern = 40
            // Through goblin camp: hub -> goblin_camp (20) -> dark_cavern (22) = 42
            // So direct is shorter
            float directDist = starterMap.GetDirectDistance("hub", "dark_cavern");
            float pathDist = starterMap.FindPath("hub", "dark_cavern", out var path);

            // Pathfinder should pick the direct route
            Assert.AreEqual(directDist, pathDist);
        }
    }
}
