using NUnit.Framework;
using AutoBossGrabber;
using System.Collections.Generic;
using UnityEngine;

namespace AutoBossGrabber.Tests
{
    [TestFixture]
    [Category("Unit")]
    public class BFSPathfinderTests
    {
        private MapGraph _graph;
        private BFSPathfinder _pathfinder;
        
        [SetUp]
        public void Setup()
        {
            _graph = new MapGraph();
            _pathfinder = new BFSPathfinder();
        }
        
        [Test]
        public void Test_BFS_ShortestPath_SimpleGraph()
        {
            // Arrange: Create simple linear graph 1 -> 2 -> 3
            _graph.AddEdge(1, 2, new Vector3(100, 100, 0));
            _graph.AddEdge(2, 3, new Vector3(200, 100, 0));
            
            // Act
            var path = _pathfinder.ComputePath("Map3");
            
            // Assert
            Assert.IsNotNull(path);
            Assert.AreEqual(3, path.Count);
            Assert.AreEqual(new List<int> { 1, 2, 3 }, path);
        }
        
        [Test]
        public void Test_BFS_NoPath_Unreachable()
        {
            // Arrange: Create disconnected graph
            _graph.AddEdge(1, 2, Vector3.zero);
            _graph.AddEdge(3, 4, Vector3.zero); // Separate component
            
            // Act: Try to reach map 4 from map 1
            var path = _pathfinder.ComputePath("Map4");
            
            // Assert
            Assert.IsNull(path, "BFS should return null for unreachable destination");
        }
        
        [Test]
        public void Test_BFS_SameSourceDestination()
        {
            // Arrange
            _graph.AddEdge(1, 2, Vector3.zero);
            
            // Act: Source == Destination
            var path = _pathfinder.ComputePath("Map1");
            
            // Assert
            Assert.IsNotNull(path);
            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(1, path[0]);
        }
        
        [Test]
        public void Test_BFS_MultipleRoutes_PicksShortest()
        {
            // Arrange: Create graph with multiple paths
            //   1 -> 2 -> 3 -> 4  (3 hops)
            //   1 -> 5 -> 4        (2 hops - shortest)
            _graph.AddEdge(1, 2, Vector3.zero);
            _graph.AddEdge(2, 3, Vector3.zero);
            _graph.AddEdge(3, 4, Vector3.zero);
            _graph.AddEdge(1, 5, Vector3.zero);
            _graph.AddEdge(5, 4, Vector3.zero);
            
            // Act
            var path = _pathfinder.ComputePath("Map4");
            
            // Assert
            Assert.IsNotNull(path);
            Assert.AreEqual(3, path.Count, "BFS must pick shortest path");
            Assert.AreEqual(new List<int> { 1, 5, 4 }, path);
        }
        
        [Test]
        public void Test_BFS_CycleDetection()
        {
            // Arrange: Create graph with cycle
            //   1 -> 2 -> 3
            //   ^         |
            //   +----<----+
            _graph.AddEdge(1, 2, Vector3.zero);
            _graph.AddEdge(2, 3, Vector3.zero);
            _graph.AddEdge(3, 1, Vector3.zero); // Back edge creating cycle
            
            // Act: BFS should handle cycle gracefully
            var path = _pathfinder.ComputePath("Map3");
            
            // Assert
            Assert.IsNotNull(path);
            Assert.AreEqual(3, path.Count);
            Assert.AreEqual(new List<int> { 1, 2, 3 }, path);
            
            // Verify no node appears twice (no infinite loop)
            var uniqueNodes = new HashSet<int>(path);
            Assert.AreEqual(path.Count, uniqueNodes.Count, "Path should not contain duplicate nodes");
        }
    }
    
    [TestFixture]
    [Category("Unit")]
    public class MapGraphTests
    {
        private MapGraph _graph;
        
        [SetUp]
        public void Setup()
        {
            _graph = new MapGraph();
        }
        
        [Test]
        public void Test_MapGraph_AddEdge()
        {
            // Act
            _graph.AddEdge(1, 2, new Vector3(100, 200, 0));
            
            // Assert
            Assert.IsTrue(_graph.ContainsMap(1));
            var edges = _graph.GetEdges(1);
            Assert.AreEqual(1, edges.Count());
            
            var edge = edges.First();
            Assert.AreEqual(2, edge.DestinationMapId);
            Assert.AreEqual(new Vector3(100, 200, 0), edge.PortalPosition);
        }
        
        [Test]
        public void Test_MapGraph_GetEdges_NonExistentMap()
        {
            // Act
            var edges = _graph.GetEdges(999);
            
            // Assert
            Assert.IsEmpty(edges, "Non-existent map should return empty enumerable");
        }
        
        [Test]
        public void Test_MapGraph_Serialization()
        {
            // Arrange
            _graph.AddEdge(1, 2, new Vector3(10, 20, 0));
            _graph.AddEdge(1, 3, new Vector3(30, 40, 0));
            _graph.AddEdge(2, 3, new Vector3(50, 60, 0));
            
            // Act: Serialize and deserialize
            string json = _graph.ToJson();
            var deserialized = MapGraph.FromJson(json);
            
            // Assert
            Assert.AreEqual(_graph.NodeCount, deserialized.NodeCount);
            Assert.AreEqual(_graph.EdgeCount, deserialized.EdgeCount);
            
            var edges1 = deserialized.GetEdges(1).ToList();
            Assert.AreEqual(2, edges1.Count);
            
            var edge12 = edges1.First(e => e.DestinationMapId == 2);
            Assert.AreEqual(new Vector3(10, 20, 0), edge12.PortalPosition);
        }
        
        [Test]
        public void Test_MapGraph_BidirectionalEdges()
        {
            // Arrange: Add both directions
            _graph.AddEdge(1, 2, new Vector3(100, 100, 0));
            _graph.AddEdge(2, 1, new Vector3(100, 100, 0));
            
            // Assert
            Assert.IsTrue(_graph.ContainsMap(1));
            Assert.IsTrue(_graph.ContainsMap(2));
            
            var edges1to2 = _graph.GetEdges(1);
            Assert.AreEqual(1, edges1to2.Count());
            Assert.AreEqual(2, edges1to2.First().DestinationMapId);
            
            var edges2to1 = _graph.GetEdges(2);
            Assert.AreEqual(1, edges2to1.Count());
            Assert.AreEqual(1, edges2to1.First().DestinationMapId);
        }
    }
    
    [TestFixture]
    [Category("Unit")]
    public class GraphCacheTests
    {
        private GraphCache _cache;
        private string _testCachePath;
        
        [SetUp]
        public void Setup()
        {
            _cache = new GraphCache();
            _testCachePath = "test_cache.json";
        }
        
        [TearDown]
        public void Cleanup()
        {
            if (System.IO.File.Exists(_testCachePath))
            {
                System.IO.File.Delete(_testCachePath);
            }
        }
        
        [Test]
        public void Test_GraphCache_SaveAndLoad()
        {
            // Arrange
            var graph = new MapGraph();
            graph.AddEdge(1, 2, new Vector3(10, 20, 0));
            graph.AddEdge(2, 3, new Vector3(30, 40, 0));
            
            // Act: Save
            _cache.Save(graph);
            
            // Assert: Load
            var loaded = _cache.Load();
            Assert.IsNotNull(loaded);
            Assert.AreEqual(2, loaded.NodeCount);
            Assert.AreEqual(2, loaded.EdgeCount);
        }
        
        [Test]
        public void Test_GraphCache_Clear()
        {
            // Arrange: Create cache file
            var graph = new MapGraph();
            graph.AddEdge(1, 2, Vector3.zero);
            _cache.Save(graph);
            
            // Act
            _cache.Clear();
            
            // Assert
            var loaded = _cache.Load();
            Assert.IsNull(loaded, "Cache should be null after Clear()");
        }
    }
    
    [TestFixture]
    [Category("Unit")]
    public class MapNameResolverTests
    {
        private MapNameResolver _resolver;
        
        [SetUp]
        public void Setup()
        {
            _resolver = new MapNameResolver();
        }
        
        [Test]
        [TestCase("Làng Kakarot", 1)]
        [TestCase("làng kakarot", 1)] // Case insensitive
        [TestCase("Lang Kakarot", 1)] // Accent normalization
        [TestCase("Cung Điện", 5)]
        [TestCase("cung dien", 5)]
        public void Test_MapNameResolver_ExactMatch(string input, int expectedId)
        {
            // Act
            int mapId = _resolver.Resolve(input);
            
            // Assert
            Assert.AreEqual(expectedId, mapId);
        }
        
        [Test]
        public void Test_MapNameResolver_UnknownMap()
        {
            // Act
            int mapId = _resolver.Resolve("Unknown Map That Does Not Exist");
            
            // Assert
            Assert.AreEqual(-1, mapId, "Unknown maps should return -1");
        }
        
        [Test]
        [TestCase("Gừng", 10)] // Vietnamese normalization
        [TestCase("Gung", 10)] // ASCII version
        public void Test_MapNameResolver_FuzzyMatch(string input, int expectedId)
        {
            // Act
            int mapId = _resolver.Resolve(input);
            
            // Assert
            Assert.AreEqual(expectedId, mapId);
        }
    }
}
