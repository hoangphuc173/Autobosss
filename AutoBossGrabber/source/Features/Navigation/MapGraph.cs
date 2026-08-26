using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;

namespace AutoBossGrabber;

/// <summary>
/// Data structure representing the game world as a directed graph.
/// Each node is a map ID, each edge is a portal connection.
/// Uses an adjacency list implementation for efficient edge traversal during BFS.
/// 
/// Design: BFS Pathfinder - MapGraph Component
/// Requirements: 1.3 (Represent maps as nodes and portals as directed edges)
///               1.4 (Support bidirectional edges where portals allow travel in both directions)
/// </summary>
public class MapGraph
{
    /// <summary>
    /// Adjacency list: mapId → list of outgoing portal edges.
    /// Dictionary provides O(1) lookup for edges from a given map.
    /// </summary>
    [JsonProperty("adjacencyList")]
    private Dictionary<int, List<PortalEdge>> _adjacencyList;

    /// <summary>
    /// Gets the total number of map nodes in the graph.
    /// </summary>
    [JsonIgnore]
    public int NodeCount => _adjacencyList.Keys.Count;

    /// <summary>
    /// Gets the total number of portal edges in the graph.
    /// </summary>
    [JsonIgnore]
    public int EdgeCount => _adjacencyList.Values.Sum(edges => edges.Count);

    /// <summary>
    /// Default constructor initializing an empty graph.
    /// </summary>
    public MapGraph()
    {
        _adjacencyList = new Dictionary<int, List<PortalEdge>>();
    }

    /// <summary>
    /// Adds a directed edge (portal connection) from source map to destination map.
    /// If the source map doesn't exist in the graph, it will be created.
    /// 
    /// Note: For bidirectional portals, call AddEdge twice (A→B and B→A).
    /// This allows asymmetric edges where some portals are one-way only.
    /// </summary>
    /// <param name="sourceMapId">The map ID where the portal is located</param>
    /// <param name="destinationMapId">The map ID the portal leads to</param>
    /// <param name="portalPosition">The world position of the portal object</param>
    public void AddEdge(int sourceMapId, int destinationMapId, Vector3 portalPosition)
    {
        // Ensure source map exists in adjacency list
        if (!_adjacencyList.ContainsKey(sourceMapId))
        {
            _adjacencyList[sourceMapId] = new List<PortalEdge>();
        }

        // Add the edge
        _adjacencyList[sourceMapId].Add(new PortalEdge
        {
            DestinationMapId = destinationMapId,
            PortalPosition = portalPosition
        });
    }

    /// <summary>
    /// Gets all outgoing edges (portal connections) from a given map.
    /// Returns an empty enumerable if the map doesn't exist or has no portals.
    /// </summary>
    /// <param name="mapId">The map ID to query</param>
    /// <returns>Enumerable of portal edges from this map</returns>
    public IEnumerable<PortalEdge> GetEdges(int mapId)
    {
        return _adjacencyList.ContainsKey(mapId)
            ? _adjacencyList[mapId]
            : Enumerable.Empty<PortalEdge>();
    }

    /// <summary>
    /// Checks if a map exists in the graph (has at least one outgoing edge).
    /// </summary>
    /// <param name="mapId">The map ID to check</param>
    /// <returns>True if the map exists in the graph</returns>
    public bool ContainsMap(int mapId)
    {
        return _adjacencyList.ContainsKey(mapId);
    }

    /// <summary>
    /// Serializes the graph to JSON format for caching.
    /// Uses Newtonsoft.Json for IL2CPP compatibility.
    /// </summary>
    /// <returns>JSON string representation of the graph</returns>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(_adjacencyList, Formatting.Indented);
    }

    /// <summary>
    /// Deserializes a graph from JSON format.
    /// </summary>
    /// <param name="json">JSON string containing serialized graph data</param>
    /// <returns>Reconstructed MapGraph instance</returns>
    /// <exception cref="JsonException">If JSON is malformed or incompatible</exception>
    public static MapGraph FromJson(string json)
    {
        var graph = new MapGraph();
        graph._adjacencyList = JsonConvert.DeserializeObject<Dictionary<int, List<PortalEdge>>>(json);
        
        // Handle null deserialization result
        if (graph._adjacencyList == null)
        {
            graph._adjacencyList = new Dictionary<int, List<PortalEdge>>();
        }
        
        return graph;
    }

    /// <summary>
    /// Gets all map IDs in the graph.
    /// Useful for debugging and analytics.
    /// </summary>
    /// <returns>Enumerable of all map IDs</returns>
    public IEnumerable<int> GetAllMapIds()
    {
        return _adjacencyList.Keys;
    }

    /// <summary>
    /// Provides a human-readable representation of the graph structure.
    /// </summary>
    public override string ToString()
    {
        return $"MapGraph: {NodeCount} maps, {EdgeCount} portals";
    }
}
