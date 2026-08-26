using Xunit;
using AutoBossGrabber;
using System;
using System.IO;

namespace AutoBoss.Tests;

[Collection("PluginEnv")]
public class MapNameResolverTests
{
    [Fact]
    public void RegisterMap_ThenResolve_CaseInsensitive()
    {
        var r = new MapNameResolver();
        r.RegisterMap(1, "L\u00E0ng Kakarot"); // "Làng Kakarot"

        Assert.Equal(1, r.Resolve("l\u00E0ng kakarot"));
        Assert.Equal(1, r.Resolve("L\u00C0NG KAKAROT"));
        Assert.Equal(1, r.Resolve("L\u00E0ng Kakarot"));
    }

    [Fact]
    public void Resolve_UnknownName_ReturnsMinusOne()
    {
        var r = new MapNameResolver();
        Assert.Equal(-1, r.Resolve("Khong Ton Tai"));
    }

    [Fact]
    public void RegisterMap_SameIdNewName_ReplacesOldName()
    {
        var r = new MapNameResolver();
        r.RegisterMap(1, "Cu");
        r.RegisterMap(1, "Moi");

        Assert.Equal(-1, r.Resolve("Cu"));
        Assert.Equal(1, r.Resolve("Moi"));
        Assert.Single(r.GetAllMapIds());
    }

    [Fact]
    public void JsonRoundTrip_PreservesMappings()
    {
        var r = new MapNameResolver();
        r.RegisterMap(1, "Quay");
        r.RegisterMap(2, "Cung");

        var clone = MapNameResolver.FromJson(r.ToJson());

        Assert.Equal(r.Count, clone.Count);
        Assert.Equal(2, clone.Resolve("cung"));
        Assert.Equal("Quay", clone.GetName(1));
    }
}

[Collection("PluginEnv")]
public class GraphCacheTests : IDisposable
{
    private readonly string _tmpDir;

    public GraphCacheTests()
    {
        TestBootstrap.Initialize();
        _tmpDir = Path.Combine(Path.GetTempPath(), "autoboss-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
    }

    private static (MapGraph Graph, MapNameResolver Resolver) BuildSample()
    {
        // Dung MapPoint (thuan .NET) thay vi UnityEngine.Vector3 de khong can il2cpp runtime.
        var g = new MapGraph();
        g.AddEdge(1, 2, new MapPoint(10f, 20f, 30f));
        var r = new MapNameResolver();
        r.RegisterMap(1, "A");
        r.RegisterMap(2, "B");
        return (g, r);
    }

    [Fact]
    public void Save_TryLoad_RoundTrip()
    {
        var cache = new GraphCache(_tmpDir);
        var (g, r) = BuildSample();

        cache.Save(g, r);

        Assert.True(cache.CacheExists());

        var ok = cache.TryLoad(out var loadedGraph, out var loadedResolver);
        Assert.True(ok);
        Assert.Equal(g.EdgeCount, loadedGraph!.EdgeCount);
        Assert.Equal(2, loadedResolver!.Resolve("b"));
    }

    [Fact]
    public void TryLoad_NoCacheFile_ReturnsFalse()
    {
        var cache = new GraphCache(_tmpDir);
        var ok = cache.TryLoad(out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryLoad_CorruptedFile_ReturnsFalse()
    {
        var cache = new GraphCache(_tmpDir);
        Directory.CreateDirectory(_tmpDir);
        File.WriteAllText(cache.GetCachePath(), "{ not valid json !!!");

        var ok = cache.TryLoad(out var graph, out _);

        Assert.False(ok);
        Assert.Null(graph);
    }

    [Fact]
    public void Delete_RemovesCacheFile()
    {
        var cache = new GraphCache(_tmpDir);
        var (g, r) = BuildSample();

        cache.Save(g, r);
        Assert.True(cache.CacheExists());

        cache.Delete();
        Assert.False(cache.CacheExists());
    }
}
