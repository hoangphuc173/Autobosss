# Design Document: BFS Pathfinder

## Overview

The BFS Pathfinder is a navigation subsystem that enables automatic map-to-map pathfinding in AutoBossGrabber. It constructs a graph representation of the game world from MapGateway portal data, applies breadth-first search to compute shortest paths, and integrates with the existing SocketClient command handler to execute TELEPORT_TO_MAP commands.

**Architecture Summary:**
- **BFSPathfinder.cs**: Core pathfinding component with graph construction and BFS algorithm
- **MapGraph.cs**: Data structure representing maps and portal connections
- **NavigationController.cs**: Orchestrates portal traversal and state management
- **MapNameResolver.cs**: Handles human-readable map name to ID resolution

**Key Design Decisions:**
1. **Lazy Graph Construction**: Build graph on first pathfinding request rather than on plugin load (reduces startup time)
2. **Persistent Caching**: Save graph to disk to avoid rebuilding every session (7-day TTL)
3. **Queue-based BFS**: Standard BFS implementation using Queue<int> for level-order traversal
4. **Main Thread Integration**: Portal interactions queued via SocketClient's existing Main_Thread_Queue pattern
5. **Graceful Degradation**: Fallback to hard-coded portal chains if graph construction fails

## Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                      SocketClient                            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ HandleCommand(TELEPORT_TO_MAP)                       │   │
│  │   1. Extract targetMap from payload                  │   │
│  │   2. Call BFSPathfinder.ComputePath()               │   │
│  │   3. Pass path to NavigationController.Execute()    │   │
│  └──────────────┬───────────────────────────────────────┘   │
└─────────────────┼───────────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────────┐
│                   BFSPathfinder                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ • MapGraph graph                                     │   │
│  │ • MapNameResolver nameResolver                       │   │
│  │ • GraphCache cache                                   │   │
│  │                                                      │   │
│  │ + ComputePath(source, dest): List<int>              │   │
│  │ + BuildGraph(): void                                 │   │
│  │ + InvalidateCache(): void                            │   │
│  └──────────────┬───────────────────────────────────────┘   │
└─────────────────┼───────────────────────────────────────────┘
                  │
         ┌────────┴────────┐
         ▼                 ▼
┌──────────────┐  ┌──────────────────┐
│  MapGraph    │  │ NavigationCtrl   │
│              │  │                  │
│ • Nodes      │  │ • Execute(path)  │
│ • Edges      │  │ • TraversePortal │
│              │  │ • VerifyArrival  │
└──────────────┘  └──────────────────┘
```

### Data Flow: TELEPORT_TO_MAP Command

```
Manager (UI)
    │
    │ TELEPORT_TO_MAP {"targetMap": "Boss Cave"}
    ▼
SocketClient (Background Thread)
    │
    │ Enqueue command to Main_Thread_Queue
    ▼
SocketClient.Update() (Unity Main Thread)
    │
    │ Dequeue and execute command
    ▼
BFSPathfinder.ComputePath("Boss Cave")
    │
    ├─→ MapNameResolver.Resolve("Boss Cave") → mapId=42
    │
    ├─→ Check cache → cache miss
    │
    ├─→ BuildGraph() → scan MapGateway objects
    │
    ├─→ BFS(currentMap=1, targetMap=42) → [1, 5, 12, 42]
    │
    └─→ Return path
    │
    ▼
NavigationController.Execute([1, 5, 12, 42])
    │
    ├─→ Step 1: Move to portal 1→5, interact
    │   └─→ Wait for map transition (polling)
    │
    ├─→ Step 2: Move to portal 5→12, interact
    │   └─→ Wait for map transition
    │
    ├─→ Step 3: Move to portal 12→42, interact
    │   └─→ Wait for map transition
    │
    └─→ Send STATUS_UPDATE to Manager (arrived)
```

## Components and Interfaces

### 1. BFSPathfinder

**Responsibility:** Main pathfinding component orchestrating graph construction, caching, and BFS computation.

```csharp
public class BFSPathfinder
{
    private MapGraph _graph;
    private MapNameResolver _nameResolver;
    private GraphCache _cache;
    private readonly object _graphLock = new object();
    
    public BFSPathfinder()
    {
        _cache = new GraphCache();
        _nameResolver = new MapNameResolver();
        _graph = null; // Lazy initialization
    }
    
    /// <summary>
    /// Computes shortest path from current map to target map.
    /// Thread-safe: can be called from any thread.
    /// </summary>
    /// <param name="targetMapName">Human-readable map name</param>
    /// <returns>Path as list of map IDs, or null if unreachable</returns>
    public List<int> ComputePath(string targetMapName)
    {
        EnsureGraphLoaded();
        
        int currentMapId = GameAPI.GetCurrentMapId();
        int targetMapId = _nameResolver.Resolve(targetMapName);
        
        if (targetMapId == -1)
        {
            Plugin.Log.LogError($"[BFSPathfinder] Unknown map name: {targetMapName}");
            return null;
        }
        
        lock (_graphLock)
        {
            return BFS(currentMapId, targetMapId);
        }
    }
    
    /// <summary>
    /// Performs breadth-first search from source to destination.
    /// </summary>
    private List<int> BFS(int source, int destination)
    {
        if (source == destination)
            return new List<int> { source };
        
        var queue = new Queue<int>();
        var visited = new HashSet<int>();
        var parent = new Dictionary<int, int>();
        
        queue.Enqueue(source);
        visited.Add(source);
        
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            
            foreach (var edge in _graph.GetEdges(current))
            {
                int neighbor = edge.DestinationMapId;
                
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    parent[neighbor] = current;
                    queue.Enqueue(neighbor);
                    
                    if (neighbor == destination)
                    {
                        return ReconstructPath(parent, source, destination);
                    }
                }
            }
        }
        
        // No path found
        return null;
    }
    
    /// <summary>
    /// Reconstructs path from parent dictionary.
    /// </summary>
    private List<int> ReconstructPath(Dictionary<int, int> parent, int source, int destination)
    {
        var path = new List<int>();
        int current = destination;
        
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
    /// </summary>
    public void BuildGraph()
    {
        lock (_graphLock)
        {
            Plugin.Log.LogInfo("[BFSPathfinder] Building map graph...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            _graph = new MapGraph();
            
            // Scan all MapGateway objects
            var gateways = GameAPI.FindAllMapGateways();
            
            foreach (var gateway in gateways)
            {
                int sourceMap = gateway.SourceMapId;
                int destMap = gateway.DestinationMapId;
                Vector3 portalPos = gateway.Position;
                
                _graph.AddEdge(sourceMap, destMap, portalPos);
                _nameResolver.RegisterMap(sourceMap, gateway.SourceMapName);
                _nameResolver.RegisterMap(destMap, gateway.DestinationMapName);
            }
            
            sw.Stop();
            Plugin.Log.LogInfo($"[BFSPathfinder] Graph built: {_graph.NodeCount} maps, {_graph.EdgeCount} portals in {sw.ElapsedMilliseconds}ms");
            
            // Save to cache
            _cache.Save(_graph, _nameResolver);
        }
    }
    
    /// <summary>
    /// Invalidates cached graph and rebuilds from game data.
    /// </summary>
    public void InvalidateCache()
    {
        lock (_graphLock)
        {
            _cache.Delete();
            _graph = null;
            Plugin.Log.LogInfo("[BFSPathfinder] Cache invalidated");
        }
    }
    
    /// <summary>
    /// Ensures graph is loaded (from cache or fresh build).
    /// </summary>
    private void EnsureGraphLoaded()
    {
        lock (_graphLock)
        {
            if (_graph != null) return;
            
            // Try to load from cache
            if (_cache.TryLoad(out MapGraph cachedGraph, out MapNameResolver cachedResolver))
            {
                _graph = cachedGraph;
                _nameResolver = cachedResolver;
                Plugin.Log.LogInfo("[BFSPathfinder] Loaded graph from cache");
            }
            else
            {
                BuildGraph();
            }
        }
    }
}
```

### 2. MapGraph

**Responsibility:** Data structure representing the graph of maps and portal connections.

```csharp
public class MapGraph
{
    // Adjacency list: mapId → list of edges
    private Dictionary<int, List<PortalEdge>> _adjacencyList;
    
    public int NodeCount => _adjacencyList.Keys.Count;
    public int EdgeCount => _adjacencyList.Values.Sum(edges => edges.Count);
    
    public MapGraph()
    {
        _adjacencyList = new Dictionary<int, List<PortalEdge>>();
    }
    
    /// <summary>
    /// Adds a directed edge from source to destination.
    /// </summary>
    public void AddEdge(int sourceMapId, int destinationMapId, Vector3 portalPosition)
    {
        if (!_adjacencyList.ContainsKey(sourceMapId))
            _adjacencyList[sourceMapId] = new List<PortalEdge>();
        
        _adjacencyList[sourceMapId].Add(new PortalEdge
        {
            DestinationMapId = destinationMapId,
            PortalPosition = portalPosition
        });
    }
    
    /// <summary>
    /// Gets all edges (portals) from a map.
    /// </summary>
    public IEnumerable<PortalEdge> GetEdges(int mapId)
    {
        return _adjacencyList.ContainsKey(mapId) 
            ? _adjacencyList[mapId] 
            : Enumerable.Empty<PortalEdge>();
    }
    
    /// <summary>
    /// Checks if a map exists in the graph.
    /// </summary>
    public bool ContainsMap(int mapId)
    {
        return _adjacencyList.ContainsKey(mapId);
    }
    
    /// <summary>
    /// Serializes graph to JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(_adjacencyList);
    }
    
    /// <summary>
    /// Deserializes graph from JSON.
    /// </summary>
    public static MapGraph FromJson(string json)
    {
        var graph = new MapGraph();
        graph._adjacencyList = JsonConvert.DeserializeObject<Dictionary<int, List<PortalEdge>>>(json);
        return graph;
    }
}

public class PortalEdge
{
    public int DestinationMapId { get; set; }
    public Vector3 PortalPosition { get; set; }
}
```

### 3. NavigationController

**Responsibility:** Executes computed paths by traversing portals sequentially.

```csharp
public class NavigationController
{
    private MapGraph _graph;
    private bool _isNavigating;
    
    public NavigationController(MapGraph graph)
    {
        _graph = graph;
        _isNavigating = false;
    }
    
    /// <summary>
    /// Executes a path by traversing portals.
    /// Must be called from Unity main thread.
    /// </summary>
    public IEnumerator ExecutePath(List<int> path)
    {
        if (path == null || path.Count < 2)
        {
            Plugin.Log.LogWarning("[NavigationController] Invalid path");
            yield break;
        }
        
        _isNavigating = true;
        Plugin.Log.LogInfo($"[NavigationController] Executing path: {string.Join(" → ", path)}");
        
        for (int i = 0; i < path.Count - 1; i++)
        {
            int currentMap = path[i];
            int nextMap = path[i + 1];
            
            // Find portal to next map
            var portal = _graph.GetEdges(currentMap)
                .FirstOrDefault(e => e.DestinationMapId == nextMap);
            
            if (portal == null)
            {
                Plugin.Log.LogError($"[NavigationController] Portal not found: {currentMap} → {nextMap}");
                yield break;
            }
            
            // Move to portal and interact
            yield return TraversePortal(portal);
            
            // Verify we arrived at expected map
            int arrivedMap = GameAPI.GetCurrentMapId();
            if (arrivedMap != nextMap)
            {
                Plugin.Log.LogError($"[NavigationController] Navigation error: expected {nextMap}, arrived at {arrivedMap}");
                yield break;
            }
        }
        
        _isNavigating = false;
        Plugin.Log.LogInfo("[NavigationController] Navigation complete");
    }
    
    /// <summary>
    /// Traverses a single portal with timeout.
    /// </summary>
    private IEnumerator TraversePortal(PortalEdge portal)
    {
        Plugin.Log.LogInfo($"[NavigationController] Traversing portal to map {portal.DestinationMapId}");
        
        // Move player to portal position
        GameAPI.MoveToPosition(portal.PortalPosition);
        yield return new WaitForSeconds(0.5f); // Wait for movement
        
        // Interact with portal
        int currentMap = GameAPI.GetCurrentMapId();
        GameAPI.InteractWithPortal(portal.PortalPosition);
        
        // Wait for map transition (poll with timeout)
        float timeout = 10f;
        float elapsed = 0f;
        
        while (elapsed < timeout)
        {
            if (GameAPI.GetCurrentMapId() != currentMap)
            {
                Plugin.Log.LogInfo($"[NavigationController] Portal traversed successfully");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        
        Plugin.Log.LogError($"[NavigationController] Portal traversal timeout after {timeout}s");
    }
    
    public void AbortNavigation()
    {
        _isNavigating = false;
        Plugin.Log.LogWarning("[NavigationController] Navigation aborted");
    }
}
```

### 4. MapNameResolver

**Responsibility:** Bidirectional mapping between human-readable map names and internal map IDs.

```csharp
public class MapNameResolver
{
    private Dictionary<int, string> _idToName;
    private Dictionary<string, int> _nameToId;
    
    public MapNameResolver()
    {
        _idToName = new Dictionary<int, string>();
        _nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Registers a map name/ID pair.
    /// </summary>
    public void RegisterMap(int mapId, string mapName)
    {
        _idToName[mapId] = mapName;
        _nameToId[mapName] = mapId;
    }
    
    /// <summary>
    /// Resolves map name to ID (case-insensitive).
    /// Returns -1 if not found.
    /// </summary>
    public int Resolve(string mapName)
    {
        return _nameToId.TryGetValue(mapName, out int mapId) ? mapId : -1;
    }
    
    /// <summary>
    /// Gets map name from ID.
    /// Returns null if not found.
    /// </summary>
    public string GetName(int mapId)
    {
        return _idToName.TryGetValue(mapId, out string name) ? name : null;
    }
    
    /// <summary>
    /// Serializes resolver to JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(_idToName);
    }
    
    /// <summary>
    /// Deserializes resolver from JSON.
    /// </summary>
    public static MapNameResolver FromJson(string json)
    {
        var resolver = new MapNameResolver();
        resolver._idToName = JsonConvert.DeserializeObject<Dictionary<int, string>>(json);
        
        foreach (var kvp in resolver._idToName)
        {
            resolver._nameToId[kvp.Value] = kvp.Key;
        }
        
        return resolver;
    }
}
```

### 5. GraphCache

**Responsibility:** Persistent storage of graph and name resolver to disk.

```csharp
public class GraphCache
{
    private readonly string _cacheDir = Path.Combine(Paths.ConfigPath, "AutoBossGrabber");
    private readonly string _cacheFile = "map_graph.json";
    private readonly TimeSpan _cacheTTL = TimeSpan.FromDays(7);
    
    /// <summary>
    /// Attempts to load graph and resolver from cache.
    /// Returns true if cache is valid and loaded successfully.
    /// </summary>
    public bool TryLoad(out MapGraph graph, out MapNameResolver resolver)
    {
        graph = null;
        resolver = null;
        
        string cachePath = Path.Combine(_cacheDir, _cacheFile);
        
        if (!File.Exists(cachePath))
        {
            Plugin.Log.LogInfo("[GraphCache] Cache file not found");
            return false;
        }
        
        // Check cache age
        var fileInfo = new FileInfo(cachePath);
        if (DateTime.Now - fileInfo.LastWriteTime > _cacheTTL)
        {
            Plugin.Log.LogInfo("[GraphCache] Cache expired (>7 days old)");
            return false;
        }
        
        try
        {
            string json = File.ReadAllText(cachePath);
            var cacheData = JsonConvert.DeserializeObject<CacheData>(json);
            
            graph = MapGraph.FromJson(cacheData.GraphJson);
            resolver = MapNameResolver.FromJson(cacheData.ResolverJson);
            
            Plugin.Log.LogInfo($"[GraphCache] Loaded from cache: {graph.NodeCount} maps, {graph.EdgeCount} edges");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to load cache: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Saves graph and resolver to cache file.
    /// </summary>
    public void Save(MapGraph graph, MapNameResolver resolver)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            
            var cacheData = new CacheData
            {
                Timestamp = DateTime.Now,
                GraphJson = graph.ToJson(),
                ResolverJson = resolver.ToJson(),
                MapCount = graph.NodeCount,
                EdgeCount = graph.EdgeCount
            };
            
            string json = JsonConvert.SerializeObject(cacheData, Formatting.Indented);
            string cachePath = Path.Combine(_cacheDir, _cacheFile);
            File.WriteAllText(cachePath, json);
            
            Plugin.Log.LogInfo($"[GraphCache] Saved to cache: {cachePath}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GraphCache] Failed to save cache: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Deletes cache file.
    /// </summary>
    public void Delete()
    {
        string cachePath = Path.Combine(_cacheDir, _cacheFile);
        
        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
            Plugin.Log.LogInfo("[GraphCache] Cache deleted");
        }
    }
}

public class CacheData
{
    public DateTime Timestamp { get; set; }
    public string GraphJson { get; set; }
    public string ResolverJson { get; set; }
    public int MapCount { get; set; }
    public int EdgeCount { get; set; }
}
```

### 6. SocketClient Integration

**Modification:** Update the TELEPORT_TO_MAP handler in SocketClient.cs.

```csharp
// In SocketClient.cs HandleCommand() method

case Commands.TELEPORT_TO_MAP:
    if (message.Payload.TryGetValue("targetMap", out object mapObj))
    {
        string targetMap = mapObj.ToString();
        Plugin.Log.LogInfo($"[SocketClient] TELEPORT_TO_MAP command received: {targetMap}");
        
        // Compute path using BFS
        var path = _pathfinder.ComputePath(targetMap);
        
        if (path == null)
        {
            Plugin.Log.LogError($"[SocketClient] No path found to map: {targetMap}");
            SendError($"Map unreachable: {targetMap}");
        }
        else
        {
            Plugin.Log.LogInfo($"[SocketClient] Path computed: {string.Join(" → ", path)}");
            
            // Execute navigation
            runner.StartCoroutine(_navigationController.ExecutePath(path));
            
            // Send acknowledgment
            SendAck($"Navigating to {targetMap}");
        }
    }
    else
    {
        Plugin.Log.LogWarning("[SocketClient] TELEPORT_TO_MAP missing 'targetMap' parameter");
        SendError("Missing required parameter: targetMap");
    }
    break;
```

## Data Models

### MapGraph Serialization Format

```json
{
  "1": [
    { "DestinationMapId": 2, "PortalPosition": { "x": 10.5, "y": 0.0, "z": 20.3 } },
    { "DestinationMapId": 5, "PortalPosition": { "x": 15.0, "y": 0.0, "z": 25.0 } }
  ],
  "2": [
    { "DestinationMapId": 1, "PortalPosition": { "x": 5.0, "y": 0.0, "z": 10.0 } },
    { "DestinationMapId": 3, "PortalPosition": { "x": 30.0, "y": 0.0, "z": 40.0 } }
  ],
  "5": [
    { "DestinationMapId": 1, "PortalPosition": { "x": 20.0, "y": 0.0, "z": 15.0 } },
    { "DestinationMapId": 12, "PortalPosition": { "x": 50.0, "y": 0.0, "z": 60.0 } }
  ]
}
```

### MapNameResolver Serialization Format

```json
{
  "1": "Town Center",
  "2": "Forest Outskirts",
  "5": "Mountain Pass",
  "12": "Boss Cave",
  "42": "Crystal Mines"
}
```

### Cache File Format

```json
{
  "Timestamp": "2024-01-15T14:30:00Z",
  "GraphJson": "{...}",
  "ResolverJson": "{...}",
  "MapCount": 45,
  "EdgeCount": 120
}
```

## Error Handling

### Error Scenarios and Responses

| Error Scenario | Detection | Response |
|----------------|-----------|----------|
| Unknown map name | MapNameResolver.Resolve() returns -1 | Send ERROR to Manager: "Unknown map: {name}" |
| No path exists | BFS returns null | Send ERROR to Manager: "Map unreachable: {name}" |
| Portal not found | NavigationController can't find edge | Abort navigation, send ERROR |
| Map transition timeout | 10s elapsed without map change | Abort navigation, retry up to 3 times |
| Wrong map arrived | Actual map ≠ expected map | Recompute path from current location |
| Cache load failure | Exception during deserialization | Log error, rebuild graph from scratch |
| Graph build failure | No MapGateways found | Log warning, operate in degraded mode |

### Retry Logic

```csharp
private IEnumerator TraversePortalWithRetry(PortalEdge portal, int maxRetries = 3)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        Plugin.Log.LogInfo($"[NavigationController] Portal traversal attempt {attempt}/{maxRetries}");
        
        yield return TraversePortal(portal);
        
        int arrivedMap = GameAPI.GetCurrentMapId();
        if (arrivedMap == portal.DestinationMapId)
        {
            yield break; // Success
        }
        
        Plugin.Log.LogWarning($"[NavigationController] Attempt {attempt} failed, retrying...");
        yield return new WaitForSeconds(Mathf.Pow(2, attempt)); // Exponential backoff
    }
    
    Plugin.Log.LogError($"[NavigationController] Portal traversal failed after {maxRetries} attempts");
}
```

## Testing Strategy

### Property-Based Testing Applicability Assessment

The BFS Pathfinder feature is **highly suitable for property-based testing** because:
- ✅ Core logic is pure functions (graph construction, BFS algorithm, path reconstruction)
- ✅ Universal properties hold across all valid inputs (path correctness, shortest path guarantee)
- ✅ Large input space (arbitrary graphs, any source/destination pairs)
- ✅ Algorithms with well-defined mathematical properties

**Not suitable for PBT:**
- ❌ Portal traversal (Unity API interactions, coroutines, timing-dependent)
- ❌ Cache I/O operations (file system side effects)

We will use **property-based tests for the core algorithms** and **integration tests for the Unity-specific components**.

### Unit Testing

**Core Algorithm Tests (Example-Based):**
- Test BFS on a small known graph (5 maps, verify exact path)
- Test single-node graph (source == destination)
- Test disconnected graph (no path exists)
- Test cycle handling (graph with loops)
- Test path reconstruction from parent dictionary

**Component Tests:**
- MapGraph: AddEdge, GetEdges, serialization roundtrip
- MapNameResolver: Register, Resolve (case-insensitive), GetName
- GraphCache: Save, TryLoad, Delete, TTL expiration

**Integration Tests (Mock-Based):**
- TELEPORT_TO_MAP command flow with mocked BFSPathfinder
- NavigationController with mocked GameAPI
- Cache load/save with temporary test directory

### Property-Based Testing Library

**Recommendation:** Use **FsCheck** for C#/.NET property-based testing.

**Each property test must:**
- Include a comment tag: `// Feature: bfs-pathfinder, Property N: [property text]`
- Reference the design property number
- Run at least 100 iterations
- Use FsCheck's `Prop.ForAll` for universal quantification

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Graph Construction Validity

*For any* list of MapGateway objects, constructing a MapGraph SHALL produce an adjacency list where every source map ID is a key and every destination map ID appears in at least one edge list.

**Validates: Requirements 1.2, 1.3**

### Property 2: Bidirectional Edge Independence

*For any* two distinct map IDs A and B, adding an edge A→B and then adding an edge B→A SHALL result in both edges existing independently in the graph (A's edge list contains B, and B's edge list contains A).

**Validates: Requirements 1.4**

### Property 3: Graph Serialization Round-Trip

*For any* valid MapGraph, serializing to JSON and then deserializing SHALL produce a graph that is structurally equivalent (same nodes, same edges, same portal positions).

**Validates: Requirements 3.1**

### Property 4: BFS Shortest Path Guarantee

*For any* connected source and destination in a MapGraph, the path returned by BFS SHALL have length less than or equal to any other valid path between the same source and destination.

**Validates: Requirements 2.2, 2.7**

### Property 5: Path Boundary Validity

*For any* non-null path returned by BFS from source S to destination D, the first element SHALL equal S and the last element SHALL equal D.

**Validates: Requirements 2.3**

### Property 6: Path Connectivity

*For any* non-null path returned by BFS, every consecutive pair of map IDs (path[i], path[i+1]) SHALL be connected by an edge in the MapGraph.

**Validates: Requirements 2.3**

### Property 7: Unreachable Destination Handling

*For any* MapGraph with disconnected components, calling BFS with source in component A and destination in component B SHALL return null.

**Validates: Requirements 2.5**

### Property 8: Cycle Termination

*For any* MapGraph containing cycles, calling BFS from any source to any destination SHALL terminate within a finite number of steps and SHALL NOT visit any node more than once.

**Validates: Requirements 2.4**

### Property 9: Identity Path

*For any* map ID M in the MapGraph, calling BFS with source=M and destination=M SHALL return a single-element path [M].

**Validates: Requirements 2.8**

### Property 10: Name Resolution Bidirectional Consistency

*For any* map ID registered with MapNameResolver, calling getName(id) to get the name N and then calling resolve(N) SHALL return the original ID.

**Validates: Requirements 11.1**

### Property 11: Case-Insensitive Name Resolution

*For any* map name registered in MapNameResolver, resolving that name with different casing (lowercase, uppercase, mixed case) SHALL always return the same map ID.

**Validates: Requirements 11.3**

