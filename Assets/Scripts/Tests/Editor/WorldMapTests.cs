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
            _map.HubNodeId = "hub";
            _map.AddNode("hub", "Hub", 0f, 0f);
            _map.AddNode("a", "Node A", 10f, 0f);
            _map.AddNode("b", "Node B", 20f, 0f);
            _map.AddNode("c", "Node C", 0f, 10f);
            _map.AddNode("isolated", "Isolated", 50f, 50f);

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
        public void FindPath_NoEdges_FallsBackToEuclidean()
        {
            float dist = _map.FindPath("hub", "isolated", out var path);

            // No edge path exists, so FindPath falls back to Euclidean distance.
            // hub=(0,0) to isolated=(50,50) = sqrt(50^2 + 50^2) ≈ 70.71
            Assert.Greater(dist, 0f);
            Assert.AreEqual(70.71f, dist, 0.1f);
            Assert.IsNotNull(path);
            Assert.AreEqual(2, path.Count);
            Assert.AreEqual("hub", path[0]);
            Assert.AreEqual("isolated", path[1]);
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

        // ─── ApproachRadius / Edge-to-Edge Distance Tests ─────────

        [Test]
        public void GetEuclideanDistance_ZeroRadius_ReturnsCenterToCenter()
        {
            // hub=(0,0) to a=(10,0), both default radius 0 → straight distance = 10
            float dist = _map.GetEuclideanDistance("hub", "a");
            Assert.AreEqual(10f, dist, 0.01f);
        }

        [Test]
        public void GetEuclideanDistance_WithRadius_SubtractsBothRadii()
        {
            var map = new WorldMap();
            map.AddNode("x", "X", 0f, 0f);
            map.AddNode("y", "Y", 20f, 0f);
            map.GetNode("x").ApproachRadius = 3f;
            map.GetNode("y").ApproachRadius = 5f;
            map.Initialize();

            // Center-to-center = 20, minus 3 + 5 = 12
            float dist = map.GetEuclideanDistance("x", "y");
            Assert.AreEqual(12f, dist, 0.01f);
        }

        [Test]
        public void GetEuclideanDistance_RadiiExceedDistance_ClampsToMinimum()
        {
            var map = new WorldMap();
            map.AddNode("close_a", "Close A", 0f, 0f);
            map.AddNode("close_b", "Close B", 5f, 0f);
            map.GetNode("close_a").ApproachRadius = 4f;
            map.GetNode("close_b").ApproachRadius = 4f;
            map.Initialize();

            // Center-to-center = 5, minus 4 + 4 = -3 → clamped to 0.1
            float dist = map.GetEuclideanDistance("close_a", "close_b");
            Assert.AreEqual(0.1f, dist, 0.001f);
        }

        [Test]
        public void FindPath_EuclideanFallback_SubtractsRadii()
        {
            var map = new WorldMap();
            map.HubNodeId = "origin";
            map.AddNode("origin", "Origin", 0f, 0f);
            map.AddNode("far", "Far", 30f, 40f); // distance = 50
            map.GetNode("origin").ApproachRadius = 5f;
            map.GetNode("far").ApproachRadius = 10f;
            // No edges — will fall back to Euclidean
            map.Initialize();

            float dist = map.FindPath("origin", "far", out _);

            // Euclidean = 50, minus 5 + 10 = 35
            Assert.AreEqual(35f, dist, 0.01f);
        }

        [Test]
        public void ApproachRadius_DefaultsToZero()
        {
            var node = _map.GetNode("hub");
            Assert.AreEqual(0f, node.ApproachRadius);
        }

        // ─── SceneName Tests ──────────────────────────────────────

        [Test]
        public void AddNode_WithSceneName_StoresSceneName()
        {
            var map = new WorldMap();
            map.AddNode("mine", "Mine", 10f, 20f, "Node_Mine");
            map.Initialize();

            var node = map.GetNode("mine");
            Assert.AreEqual("Node_Mine", node.SceneName);
        }

        [Test]
        public void AddNode_WithoutSceneName_SceneNameIsNull()
        {
            var map = new WorldMap();
            map.AddNode("basic", "Basic Node", 0f, 0f);
            map.Initialize();

            var node = map.GetNode("basic");
            Assert.IsNull(node.SceneName);
        }

        [Test]
        public void StarterMap_HubHasSceneName()
        {
            var starterMap = WorldMap.CreateStarterMap();
            var hub = starterMap.GetNode("hub");
            Assert.AreEqual("Node_GuildHall", hub.SceneName);
        }

        [Test]
        public void StarterMap_CopperMineHasSceneName()
        {
            var starterMap = WorldMap.CreateStarterMap();
            var mine = starterMap.GetNode("copper_mine");
            Assert.AreEqual("Node_CopperMine", mine.SceneName);
        }

        [Test]
        public void StarterMap_NodesWithoutScenesHaveNullSceneName()
        {
            var starterMap = WorldMap.CreateStarterMap();
            // Sunlit Pond has no scene assigned in the starter map
            var pond = starterMap.GetNode("sunlit_pond");
            Assert.IsNull(pond.SceneName);
        }
    }
}
