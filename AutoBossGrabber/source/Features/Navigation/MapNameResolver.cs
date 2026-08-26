using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AutoBossGrabber;

/// <summary>
/// Bidirectional mapping between map IDs and human-readable map names.
/// Supports case-insensitive name resolution for user-friendly TELEPORT_TO_MAP commands.
/// 
/// Design: BFS Pathfinder - MapNameResolver Component
/// Requirements: 11.1 (Maintain bidirectional mapping between map names and map IDs)
///               11.2 (Resolve map names to IDs for TELEPORT_TO_MAP)
///               11.3 (Support case-insensitive name resolution)
/// </summary>
public class MapNameResolver
{
    /// <summary>
    /// Map ID → human-readable name dictionary.
    /// </summary>
    [JsonProperty("idToName")]
    private Dictionary<int, string> _idToName;
    
    /// <summary>
    /// Human-readable name → Map ID dictionary (case-insensitive).
    /// </summary>
    [JsonIgnore]
    private Dictionary<string, int> _nameToId;
    
    /// <summary>
    /// Initializes an empty MapNameResolver.
    /// </summary>
    public MapNameResolver()
    {
        _idToName = new Dictionary<int, string>();
        _nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Registers a map name/ID pair.
    /// If the map ID already exists, the name will be updated.
    /// 
    /// Requirements: 11.1
    /// </summary>
    /// <param name="mapId">The map ID</param>
    /// <param name="mapName">The human-readable map name</param>
    public void RegisterMap(int mapId, string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            Plugin.Log.LogWarning($"[MapNameResolver] Attempted to register map {mapId} with null/empty name");
            return;
        }
        
        // Remove old name if map ID already exists
        if (_idToName.ContainsKey(mapId))
        {
            string oldName = _idToName[mapId];
            _nameToId.Remove(oldName);
        }
        
        // Register new mapping
        _idToName[mapId] = mapName;
        _nameToId[mapName] = mapId;
    }
    
    /// <summary>
    /// Resolves a map name to its ID (case-insensitive).
    /// Returns -1 if the map name is not found.
    /// 
    /// Requirements: 11.2, 11.3
    /// </summary>
    /// <param name="mapName">The map name to resolve</param>
    /// <returns>Map ID, or -1 if not found</returns>
    public int Resolve(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return -1;
        }
        
        return _nameToId.TryGetValue(mapName, out int mapId) ? mapId : -1;
    }
    
    /// <summary>
    /// Gets the map name from a map ID.
    /// Returns null if the map ID is not found.
    /// 
    /// Requirements: 11.1
    /// </summary>
    /// <param name="mapId">The map ID</param>
    /// <returns>Map name, or null if not found</returns>
    public string GetName(int mapId)
    {
        return _idToName.TryGetValue(mapId, out string name) ? name : null;
    }
    
    /// <summary>
    /// Gets all registered map IDs.
    /// </summary>
    /// <returns>Collection of all map IDs</returns>
    public IEnumerable<int> GetAllMapIds()
    {
        return _idToName.Keys;
    }
    
    /// <summary>
    /// Gets all registered map names.
    /// </summary>
    /// <returns>Collection of all map names</returns>
    public IEnumerable<string> GetAllMapNames()
    {
        return _idToName.Values;
    }
    
    /// <summary>
    /// Gets the total number of registered maps.
    /// </summary>
    public int Count => _idToName.Count;
    
    /// <summary>
    /// Serializes the resolver to JSON format for caching.
    /// Only serializes _idToName dictionary; _nameToId will be reconstructed on load.
    /// 
    /// Requirements: 11.1
    /// </summary>
    /// <returns>JSON string representation</returns>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(_idToName, Formatting.Indented);
    }
    
    /// <summary>
    /// Deserializes a resolver from JSON format.
    /// Reconstructs the _nameToId dictionary from _idToName.
    /// </summary>
    /// <param name="json">JSON string containing serialized data</param>
    /// <returns>Reconstructed MapNameResolver instance</returns>
    /// <exception cref="JsonException">If JSON is malformed</exception>
    public static MapNameResolver FromJson(string json)
    {
        var resolver = new MapNameResolver();
        resolver._idToName = JsonConvert.DeserializeObject<Dictionary<int, string>>(json);
        
        // Handle null deserialization result
        if (resolver._idToName == null)
        {
            resolver._idToName = new Dictionary<int, string>();
        }
        
        // Reconstruct _nameToId from _idToName
        resolver._nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in resolver._idToName)
        {
            resolver._nameToId[kvp.Value] = kvp.Key;
        }
        
        return resolver;
    }
    
    /// <summary>
    /// Provides a human-readable representation.
    /// </summary>
    public override string ToString()
    {
        return $"MapNameResolver: {Count} maps registered";
    }
}
