using System;
using System.IO;
using BepInEx;
using Newtonsoft.Json;

namespace AutoBossGrabber;

/// <summary>
/// Persistent storage manager for MapGraph and MapNameResolver data.
/// Implements 7-day time-to-live (TTL) cache policy with automatic invalidation.
/// 
/// Design: BFS Pathfinder - GraphCache Component
/// Requirements: 3.1 (Serialize and save graph to disk)
///               3.2 (Load cached graph on initialization)
///               3.3 (Use cached graph if less than 7 days old)
///               3.4 (Rebuild if cache doesn't exist or is expired)
///               3.7 (Validate cached graph integrity)
///               3.8 (Handle corrupted cache gracefully)
/// </summary>
public class GraphCache
{
    private readonly string _cacheDir;
    private readonly string _cacheFile = "map_graph.json";
    private readonly TimeSpan _cacheTTL = TimeSpan.FromDays(7);
    
    /// <summary>
    /// Initializes GraphCache with BepInEx config directory.
    /// Cache directory: BepInEx\config\AutoBossGrabber\
    /// </summary>
    public GraphCache()
    {
        _cacheDir = Path.Combine(Paths.ConfigPath, "AutoBossGrabber");
    }
    
    /// <summary>
    /// Attempts to load MapGraph and MapNameResolver from disk cache.
    /// Validates cache age (7-day TTL) and data integrity.
    /// 
    /// Returns true if cache is valid and loaded successfully.
    /// Returns false if cache doesn't exist, is expired, or is corrupted.
    /// 
    /// Requirements: 3.2, 3.3, 3.4, 3.7, 3.8
    /// </summary>
    /// <param name="graph">Output parameter for loaded MapGraph (null if load fails)</param>
    /// <param name="resolver">Output parameter for loaded MapNameResolver (null if load fails)</param>
    /// <returns>True if cache loaded successfully, false otherwise</returns>
    public bool TryLoad(out MapGraph graph, out MapNameResolver resolver)
    {
        graph = null;
        resolver = null;
        
        string cachePath = Path.Combine(_cacheDir, _cacheFile);
        
        // Check if cache file exists
        if (!File.Exists(cachePath))
        {
            Plugin.Log.LogInfo("[GraphCache] Cache file not found");
            return false;
        }
        
        try
        {
            // Check cache age (7-day TTL)
            var fileInfo = new FileInfo(cachePath);
            TimeSpan cacheAge = DateTime.Now - fileInfo.LastWriteTime;
            
            if (cacheAge > _cacheTTL)
            {
                Plugin.Log.LogInfo($"[GraphCache] Cache expired (age: {cacheAge.TotalDays:F1} days, TTL: {_cacheTTL.TotalDays} days)");
                return false;
            }
            
            // Read and deserialize cache file
            string json = File.ReadAllText(cachePath);
            
            if (string.IsNullOrWhiteSpace(json))
            {
                Plugin.Log.LogWarning("[GraphCache] Cache file is empty");
                return false;
            }
            
            var cacheData = JsonConvert.DeserializeObject<CacheData>(json);
            
            // Validate cache data structure
            if (cacheData == null)
            {
                Plugin.Log.LogWarning("[GraphCache] Cache data deserialization returned null");
                return false;
            }
            
            if (string.IsNullOrWhiteSpace(cacheData.GraphJson) || string.IsNullOrWhiteSpace(cacheData.ResolverJson))
            {
                Plugin.Log.LogWarning("[GraphCache] Cache data contains empty graph or resolver JSON");
                return false;
            }
            
            // Deserialize graph and resolver
            graph = MapGraph.FromJson(cacheData.GraphJson);
            resolver = MapNameResolver.FromJson(cacheData.ResolverJson);
            
            // Validate deserialized objects
            if (graph == null || resolver == null)
            {
                Plugin.Log.LogWarning("[GraphCache] Failed to deserialize graph or resolver from cache");
                graph = null;
                resolver = null;
                return false;
            }
            
            // Validate graph integrity (metadata matches actual data)
            if (graph.NodeCount != cacheData.MapCount || graph.EdgeCount != cacheData.EdgeCount)
            {
                Plugin.Log.LogWarning($"[GraphCache] Cache integrity check failed. " +
                    $"Expected: {cacheData.MapCount} maps, {cacheData.EdgeCount} edges. " +
                    $"Actual: {graph.NodeCount} maps, {graph.EdgeCount} edges");
                graph = null;
                resolver = null;
                return false;
            }
            
            Plugin.Log.LogInfo($"[GraphCache] Loaded from cache: {graph.NodeCount} maps, {graph.EdgeCount} edges, " +
                $"created {cacheData.Timestamp:yyyy-MM-dd HH:mm:ss}");
            
            return true;
        }
        catch (JsonException jsonEx)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to load cache (JSON error): {jsonEx.Message}");
            // Corrupted cache file - return false to trigger rebuild
            return false;
        }
        catch (IOException ioEx)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to load cache (I/O error): {ioEx.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to load cache (unexpected error): {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Saves MapGraph and MapNameResolver to disk cache.
    /// Creates cache directory if it doesn't exist.
    /// Includes metadata: timestamp, map count, edge count.
    /// 
    /// Requirements: 3.1, 3.6
    /// </summary>
    /// <param name="graph">MapGraph to save</param>
    /// <param name="resolver">MapNameResolver to save</param>
    public void Save(MapGraph graph, MapNameResolver resolver)
    {
        if (graph == null)
        {
            Plugin.Log.LogWarning("[GraphCache] Cannot save null graph");
            return;
        }
        
        if (resolver == null)
        {
            Plugin.Log.LogWarning("[GraphCache] Cannot save null resolver");
            return;
        }
        
        try
        {
            // Create cache directory if it doesn't exist
            Directory.CreateDirectory(_cacheDir);
            
            // Create cache data with metadata
            var cacheData = new CacheData(
                timestamp: DateTime.Now,
                graphJson: graph.ToJson(),
                resolverJson: resolver.ToJson(),
                mapCount: graph.NodeCount,
                edgeCount: graph.EdgeCount
            );
            
            // Serialize to JSON with indentation for readability
            string json = JsonConvert.SerializeObject(cacheData, Formatting.Indented);
            
            // Write to cache file
            string cachePath = Path.Combine(_cacheDir, _cacheFile);
            File.WriteAllText(cachePath, json);
            
            Plugin.Log.LogInfo($"[GraphCache] Saved to cache: {cachePath} " +
                $"({graph.NodeCount} maps, {graph.EdgeCount} edges)");
        }
        catch (IOException ioEx)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to save cache (I/O error): {ioEx.Message}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to save cache: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Deletes the cached graph file from disk.
    /// Used for cache invalidation when graph needs to be rebuilt.
    /// 
    /// Requirements: 3.5
    /// </summary>
    public void Delete()
    {
        string cachePath = Path.Combine(_cacheDir, _cacheFile);
        
        try
        {
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
                Plugin.Log.LogInfo($"[GraphCache] Cache deleted: {cachePath}");
            }
            else
            {
                Plugin.Log.LogInfo("[GraphCache] Cache file does not exist (nothing to delete)");
            }
        }
        catch (IOException ioEx)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to delete cache (I/O error): {ioEx.Message}");
        }
        catch (UnauthorizedAccessException accessEx)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to delete cache (access denied): {accessEx.Message}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to delete cache: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Gets the full path to the cache file.
    /// Useful for debugging and diagnostics.
    /// </summary>
    /// <returns>Full path to cache file</returns>
    public string GetCachePath()
    {
        return Path.Combine(_cacheDir, _cacheFile);
    }
    
    /// <summary>
    /// Checks if a valid cache file exists.
    /// Does not validate cache age or integrity.
    /// </summary>
    /// <returns>True if cache file exists, false otherwise</returns>
    public bool CacheExists()
    {
        string cachePath = Path.Combine(_cacheDir, _cacheFile);
        return File.Exists(cachePath);
    }
    
    /// <summary>
    /// Gets the age of the cached data.
    /// Returns null if cache doesn't exist.
    /// </summary>
    /// <returns>TimeSpan representing cache age, or null if cache doesn't exist</returns>
    public TimeSpan? GetCacheAge()
    {
        string cachePath = Path.Combine(_cacheDir, _cacheFile);
        
        if (!File.Exists(cachePath))
        {
            return null;
        }
        
        try
        {
            var fileInfo = new FileInfo(cachePath);
            return DateTime.Now - fileInfo.LastWriteTime;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GraphCache] Failed to get cache age: {ex.Message}");
            return null;
        }
    }
}
