using System;
using Newtonsoft.Json;

namespace AutoBossGrabber;

/// <summary>
/// Container for serializing graph data and metadata to disk cache.
/// Used by GraphCache to store MapGraph and MapNameResolver state persistently.
/// 
/// Design: BFS Pathfinder - GraphCache Component
/// Requirements: 3.6 (Cached graph file shall include metadata: creation timestamp, 
///                     game version, total maps, total edges)
/// </summary>
public class CacheData
{
    /// <summary>
    /// Timestamp when the cache was created.
    /// Used to determine cache age and enforce 7-day TTL policy.
    /// </summary>
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Serialized MapGraph data as JSON string.
    /// Contains the adjacency list representation of the map graph.
    /// </summary>
    [JsonProperty("graphJson")]
    public string GraphJson { get; set; }

    /// <summary>
    /// Serialized MapNameResolver data as JSON string.
    /// Contains the bidirectional mapping between map IDs and human-readable names.
    /// </summary>
    [JsonProperty("resolverJson")]
    public string ResolverJson { get; set; }

    /// <summary>
    /// Total number of maps (nodes) in the cached graph.
    /// Used for quick validation and logging without deserializing the full graph.
    /// </summary>
    [JsonProperty("mapCount")]
    public int MapCount { get; set; }

    /// <summary>
    /// Total number of portal edges in the cached graph.
    /// Used for quick validation and logging without deserializing the full graph.
    /// </summary>
    [JsonProperty("edgeCount")]
    public int EdgeCount { get; set; }

    /// <summary>
    /// Default constructor for JSON deserialization.
    /// </summary>
    public CacheData()
    {
    }

    /// <summary>
    /// Constructor with parameters for convenient cache data creation.
    /// </summary>
    /// <param name="timestamp">Cache creation timestamp</param>
    /// <param name="graphJson">Serialized graph JSON</param>
    /// <param name="resolverJson">Serialized resolver JSON</param>
    /// <param name="mapCount">Number of maps in the graph</param>
    /// <param name="edgeCount">Number of edges in the graph</param>
    public CacheData(DateTime timestamp, string graphJson, string resolverJson, int mapCount, int edgeCount)
    {
        Timestamp = timestamp;
        GraphJson = graphJson;
        ResolverJson = resolverJson;
        MapCount = mapCount;
        EdgeCount = edgeCount;
    }

    public override string ToString()
    {
        return $"CacheData: {MapCount} maps, {EdgeCount} edges, created {Timestamp:yyyy-MM-dd HH:mm:ss}";
    }
}
