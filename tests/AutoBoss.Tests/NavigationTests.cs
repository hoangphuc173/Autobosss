using Xunit;
using AutoBossGrabber;
using System.Collections.Generic;
using System.Linq;

namespace AutoBoss.Tests;

[Collection("PluginEnv")]
public class MapGraphTests
{
    [Fact]
    public void AddEdge_IncreasesNodeAndEdgeCount()
    {
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(0f, 0f, 0f));
        g.AddEdge(2, 3, new MapPoint(1, 0, 0));

        Assert.Equal(2, g.NodeCount);   // nodes: 1, 2
        Assert.Equal(2, g.EdgeCount);
    }

    [Fact]
    public void AddEdge_SameSource_GroupsEdges()
    {
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(0f, 0f, 0f));
        g.AddEdge(1, 3, new MapPoint(0f, 0f, 0f));

        Assert.Equal(1, g.NodeCount);
        Assert.Equal(2, g.GetEdges(1).Count());
    }

    [Fact]
    public void GetEdges_UnknownMap_ReturnsEmpty()
    {
        var g = new MapGraph();
        Assert.Empty(g.GetEdges(99));
        Assert.False(g.ContainsMap(99));
    }

    [Fact]
    public void JsonRoundTrip_PreservesAdjacency()
    {
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(10f, 20f, 30f));
        g.AddEdge(2, 3, new MapPoint(-5f, 0f, 5f));

        var clone = MapGraph.FromJson(g.ToJson());

        Assert.Equal(g.NodeCount, clone.NodeCount);
        Assert.Equal(g.EdgeCount, clone.EdgeCount);

        var edge12 = clone.GetEdges(1).Single(e => e.DestinationMapId == 2);
        Assert.Equal(new MapPoint(10f, 20f, 30f), edge12.PortalPosition);
    }
}

[Collection("PluginEnv")]
public class BfsPathfinderTests
{
    private static BFSPathfinder CreateFinder(MapGraph graph)
    {
        // Inject provider + cache rong trong tmp dir -> khong cham vao game.
        var finder = new BFSPathfinder(
            currentMapIdProvider: () => 1,
            cache: new GraphCache(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "autoboss-tests", System.Guid.NewGuid().ToString("N"))));
        finder.GetType().GetField("_graph",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(finder, graph);
        return finder;
    }

    [Fact]
    public void FindPath_LinearGraph_ShortestHopCount()
    {
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(0f, 0f, 0f));
        g.AddEdge(2, 3, new MapPoint(0f, 0f, 0f));

        var path = BFSPathfinder.FindPath(g, 1, 3);

        Assert.NotNull(path);
        Assert.Equal(new List<int> { 1, 2, 3 }, path);
    }

    [Fact]
    public void FindPath_UnreachableDestination_ReturnsNull()
    {
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(0f, 0f, 0f));
        g.AddEdge(3, 4, new MapPoint(0f, 0f, 0f));

        Assert.Null(BFSPathfinder.FindPath(g, 1, 4));
    }

    [Fact]
    public void FindPath_SameSourceDestination_SingleNode()
    {
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(0f, 0f, 0f));

        var path = BFSPathfinder.FindPath(g, 1, 1);

        Assert.NotNull(path);
        Assert.Single(path);
        Assert.Equal(1, path[0]);
    }

    [Fact]
    public void FindPath_MultipleRoutes_PicksShortest()
    {
        //   1 -> 2 -> 3 -> 4  (3 hops)
        //   1 -> 5 -> 4       (2 hops - shortest)
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(0f, 0f, 0f));
        g.AddEdge(2, 3, new MapPoint(0f, 0f, 0f));
        g.AddEdge(3, 4, new MapPoint(0f, 0f, 0f));
        g.AddEdge(1, 5, new MapPoint(0f, 0f, 0f));
        g.AddEdge(5, 4, new MapPoint(0f, 0f, 0f));

        var path = BFSPathfinder.FindPath(g, 1, 4);

        Assert.NotNull(path);
        Assert.Equal(3, path.Count);
        Assert.Contains(5, path);
    }

    [Fact]
    public void FindPath_Cycles_TerminateWithoutInfiniteLoop()
    {
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(0f, 0f, 0f));
        g.AddEdge(2, 1, new MapPoint(0f, 0f, 0f));
        g.AddEdge(2, 3, new MapPoint(0f, 0f, 0f));

        var path = BFSPathfinder.FindPath(g, 1, 3);

        Assert.NotNull(path);
        Assert.Equal(new List<int> { 1, 2, 3 }, path);
    }

    [Fact]
    public void FindPath_SourceMissingFromGraph_ReturnsNull()
    {
        var g = new MapGraph();
        g.AddEdge(9, 8, new MapPoint(0f, 0f, 0f));

        Assert.Null(BFSPathfinder.FindPath(g, 1, 8));
    }

    [Fact]
    public void FindPath_NullGraph_ReturnsNull()
    {
        Assert.Null(BFSPathfinder.FindPath(null!, 1, 2));
    }
}
