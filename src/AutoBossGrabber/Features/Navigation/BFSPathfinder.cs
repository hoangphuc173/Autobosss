using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Main pathfinding component that computes shortest paths between maps using BFS algorithm.
/// Manages graph construction, caching, and provides thread-safe path computation API.
/// 
/// Design: BFS Pathfinder - Core Component
/// Requirements: 1.1 (Build graph from MapGateway objects)
///               2.1-2.8 (BFS algorithm implementation)
///               3.1-3.5 (Graph caching and invalidation)
///               4.1-4.4 (TELEPORT_TO_MAP command support)
/// </summary>
public class BFSPathfinder
{
    private MapGraph _graph;
    private MapNameResolver _nameResolver;
    private GraphCache _cache;
    private readonly object _graphLock = new object();
    
    /// <summary>
    /// Gets the current map graph. May be null if not yet initialized.
    /// </summary>
    public MapGraph Graph => _graph;
    
    /// <summary>
    /// Initializes BFSPathfinder with lazy graph loading.
    /// Graph will be loaded from cache or built on first ComputePath() call.
    /// </summary>
    public BFSPathfinder()
    {
        _cache = new GraphCache();
        _nameResolver = new MapNameResolver();
        _graph = null; // Lazy initialization
    }
    
    /// <summary>
    /// Computes shortest path from current map to target map.
    /// Thread-safe: can be called from any thread.
    /// 
    /// Requirements: 2.1, 4.1, 11.2
    /// </summary>
    /// <param name="targetMapName">Human-readable map name (case-insensitive)</param>
    /// <returns>Path as list of map IDs (source to destination), or null if unreachable/unknown</returns>
    public List<int> ComputePath(string targetMapName)
    {
        // Ensure graph is loaded (from cache or fresh build)
        EnsureGraphLoaded();
        
        // Get current map ID from game
        int currentMapId = PathfinderGameAPI.GetCurrentMapId();
        
        // Resolve target map name to ID
        int targetMapId = _nameResolver.Resolve(targetMapName);
        
        if (targetMapId == -1)
        {
            Plugin.Log.LogError($"[BFSPathfinder] Unknown map name: {targetMapName}");
            return null;
        }
        
        Plugin.Log.LogInfo($"[BFSPathfinder] Computing path: {currentMapId} ? {targetMapId} ({targetMapName})");
        
        // Compute path using BFS
        lock (_graphLock)
        {
            var sw = Stopwatch.StartNew();
            var path = BFS(currentMapId, targetMapId);
            sw.Stop();
            
            if (path != null)
            {
                string pathStr = string.Join(" ? ", path);
                Plugin.Log.LogInfo($"[BFSPathfinder] Path found ({path.Count} hops, {sw.ElapsedMilliseconds}ms): {pathStr}");
            }
            else
            {
                Plugin.Log.LogWarning($"[BFSPathfinder] No path found from {currentMapId} to {targetMapId}");
            }
            
            return path;
        }
    }
    
    /// <summary>
    /// Performs breadth-first search from source to destination.
    /// Guarantees shortest path discovery.
    /// 
    /// Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.7, 2.8
    /// </summary>
    /// <param name="source">Source map ID</param>
    /// <param name="destination">Destination map ID</param>
    /// <returns>Path as list of map IDs, or null if unreachable</returns>
    private List<int> BFS(int source, int destination)
    {
        // Edge case: source == destination (Requirement 2.8)
        if (source == destination)
        {
            return new List<int> { source };
        }
        
        // Check if source exists in graph
        if (!_graph.ContainsMap(source))
        {
            Plugin.Log.LogWarning($"[BFSPathfinder] Source map {source} not in graph");
            return null;
        }
        
        // BFS data structures
        var queue = new Queue<int>();
        var visited = new HashSet<int>();
        var parent = new Dictionary<int, int>();
        
        // Initialize BFS
        queue.Enqueue(source);
        visited.Add(source);
        
        // BFS level-order traversal (Requirement 2.2)
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            
            // Explore all neighbors
            foreach (var edge in _graph.GetEdges(current))
            {
                int neighbor = edge.DestinationMapId;
                
                // Skip if already visited (Requirement 2.4 - avoid cycles)
                if (visited.Contains(neighbor))
                {
                    continue;
                }
                
                // Mark as visited and track parent
                visited.Add(neighbor);
                parent[neighbor] = current;
                queue.Enqueue(neighbor);
                
                // Check if we reached destination
                if (neighbor == destination)
                {
                    return ReconstructPath(parent, source, destination);
                }
            }
        }
        
        // No path found (Requirement 2.5 - handle unreachable destination)
        return null;
    }
    
    /// <summary>
    /// Reconstructs path from BFS parent dictionary.
    /// 
    /// Requirements: 2.3 (Path validity - first element is source, last is destination)
    /// </summary>
    /// <param name="parent">Parent dictionary from BFS</param>
    /// <param name="source">Source map ID</param>
    /// <param name="destination">Destination map ID</param>
    /// <returns>Path from source to destination</returns>
    private List<int> ReconstructPath(Dictionary<int, int> parent, int source, int destination)
    {
        var path = new List<int>();
        int current = destination;
        
        // Backtrack from destination to source
        while (current != source)
        {
            path.Add(current);
            current = parent[current];
        }
        
        path.Add(source);
        path.Reverse();
        
        return path;
    }
    
    /// <summary>
    /// Builds graph by scanning MapGateway objects in game world.
    /// Extracts portal connections and registers map names.
    /// 
    /// Requirements: 1.1, 1.2, 1.6, 1.7, 1.8
    /// </summary>
    public void BuildGraph()
    {
        lock (_graphLock)
        {
            Plugin.Log.LogInfo("[BFSPathfinder] Building map graph...");
            var sw = Stopwatch.StartNew();
            
            _graph = new MapGraph();
            
            // Scan all MapGateway objects (Requirement 1.1)
            var gateways = PathfinderGameAPI.FindAllMapGateways();
            
            if (gateways == null || gateways.Count == 0)
            {
                Plugin.Log.LogWarning("[BFSPathfinder] No MapGateway objects found (Requirement 1.7 - degraded mode)");
                return;
            }
            
            // Extract portal connections (Requirement 1.2)
            foreach (var gateway in gateways)
            {
                int sourceMap = gateway.SourceMapId;
                int destMap = gateway.DestinationMapId;
                Vector3 portalPos = gateway.Position;
                
                // Add directed edge
                _graph.AddEdge(sourceMap, destMap, portalPos);
                
                // Register map names (Requirement 1.6)
                _nameResolver.RegisterMap(sourceMap, gateway.SourceMapName);
                _nameResolver.RegisterMap(destMap, gateway.DestinationMapName);
            }
            
            sw.Stop();
            
            // Log statistics (Requirement 1.6, 1.8)
            Plugin.Log.LogInfo($"[BFSPathfinder] Graph built: {_graph.NodeCount} maps, " +
                $"{_graph.EdgeCount} portals in {sw.ElapsedMilliseconds}ms");
            
            // Save to cache
            _cache.Save(_graph, _nameResolver);
        }
    }
    
    /// <summary>
    /// Ensures graph is loaded (from cache or fresh build).
    /// Thread-safe lazy initialization.
    /// 
    /// Requirements: 3.2, 3.3, 3.4
    /// </summary>
    private void EnsureGraphLoaded()
    {
        lock (_graphLock)
        {
            if (_graph != null)
            {
                return; // Graph already loaded
            }
            
            // Try to load from cache (Requirement 3.2)
            if (_cache.TryLoad(out MapGraph cachedGraph, out MapNameResolver cachedResolver))
            {
                _graph = cachedGraph;
                _nameResolver = cachedResolver;
                Plugin.Log.LogInfo("[BFSPathfinder] Loaded graph from cache (Requirement 3.3)");
            }
            else
            {
                // Cache miss or expired - build from game data (Requirement 3.4)
                BuildGraph();
            }
        }
    }
    
    /// <summary>
    /// Invalidates cached graph and forces rebuild on next pathfinding request.
    /// Used when maps are unlocked or game data changes.
    /// 
    /// Requirements: 3.5
    /// </summary>
    public void InvalidateCache()
    {
        lock (_graphLock)
        {
            _cache.Delete();
            _graph = null;
            Plugin.Log.LogInfo("[BFSPathfinder] Cache invalidated - will rebuild on next request");
        }
    }
    
    /// <summary>
    /// Gets graph statistics for debugging.
    /// Returns null if graph is not loaded.
    /// </summary>
    public string GetStatistics()
    {
        lock (_graphLock)
        {
            if (_graph == null)
            {
                return "Graph not loaded";
            }
            
            return $"{_graph.NodeCount} maps, {_graph.EdgeCount} portals, {_nameResolver.Count} names registered";
        }
    }
    
    /// <summary>
    /// Forces graph rebuild (bypasses cache).
    /// Used for testing or troubleshooting.
    /// </summary>
    public void ForceRebuild()
    {
        lock (_graphLock)
        {
            BuildGraph();
        }
    }
}
