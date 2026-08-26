# API Documentation - AutoBossGrabber Phase 3

## IPC COMMANDS

### TELEPORT_TO_MAP
**Purpose:** Pathfind and navigate to target map

**Request:**
\\\json
{
  "MessageType": "COMMAND",
  "Command": "TELEPORT_TO_MAP",
  "Payload": {
    "targetMap": "G?ng"  // Vietnamese or English name
  }
}
\\\

**Response (Success):**
\\\json
{
  "MessageType": "ACK",
  "AcknowledgedType": "Navigating to G?ng"
}
\\\

**Response (Error):**
\\\json
{
  "MessageType": "ERROR",
  "ErrorMessage": "Map unreachable or unknown: InvalidName"
}
\\\

**Behavior:**
1. Resolves map name to ID
2. Computes shortest path using BFS
3. Executes portal traversal
4. Returns immediately (navigation async)

---

### INVALIDATE_CACHE  
**Purpose:** Force graph rebuild on next path computation

**Request:**
\\\json
{
  "MessageType": "COMMAND",
  "Command": "INVALIDATE_CACHE"
}
\\\

**Response:**
\\\json
{
  "MessageType": "ACK",
  "AcknowledgedType": "Cache invalidated"
}
\\\

**Use Cases:**
- After game update (new maps/portals)
- Cache corruption detected
- Testing fresh discovery

---

## C# API

### BFSPathfinder Class

#### ComputePath
\\\csharp
public List<int> ComputePath(string targetMapName)
\\\
- **Parameters:** Target map name (Vietnamese/English)
- **Returns:** List of map IDs forming path, or null if unreachable
- **Thread-safe:** Yes (uses _graphLock)
- **Side effects:** Lazy-loads graph on first call

**Example:**
\\\csharp
var pathfinder = new BFSPathfinder();
var path = pathfinder.ComputePath("G?ng");
if (path != null) {
    // path = [1, 3, 5]
}
\\\

#### InvalidateCache
\\\csharp
public void InvalidateCache()
\\\
- **Effect:** Clears in-memory graph and deletes cache file
- **Next call:** Will rebuild graph from scratch

---

### NavigationController Class

#### Constructor
\\\csharp
public NavigationController(MapGraph graph)
\\\
- **Parameters:** MapGraph instance (shared with BFSPathfinder)
- **Note:** Graph must be non-null and populated

#### ExecutePath
\\\csharp
public IEnumerator ExecutePath(List<int> path)
\\\
- **Parameters:** Path as list of map IDs
- **Returns:** IEnumerator for Unity coroutine
- **Usage:** \StartCoroutine("ExecuteNavigationPath")\

**Example:**
\\\csharp
var controller = new NavigationController(graph);
_currentNavigationPath = path;
this.StartCoroutine("ExecuteNavigationPath");

private IEnumerator ExecuteNavigationPath() {
    return controller.ExecutePath(_currentNavigationPath);
}
\\\

#### IsNavigating Property
\\\csharp
public bool IsNavigating { get; }
\\\
- **Returns:** True if navigation currently executing

---

### MapGraph Class

#### AddEdge
\\\csharp
public void AddEdge(int fromMapId, int toMapId, float portalX, float portalY)
\\\
- **Parameters:** Source map, destination map, portal coordinates
- **Effect:** Adds directed edge to graph

#### GetEdges
\\\csharp
public List<PortalEdge> GetEdges(int mapId)
\\\
- **Returns:** All outgoing portals from given map
- **Empty list:** If map has no portals

#### HasMap
\\\csharp
public bool HasMap(int mapId)
\\\
- **Returns:** True if map exists in graph

---

### MapNameResolver Class

#### Resolve
\\\csharp
public int? Resolve(string mapName)
\\\
- **Parameters:** Map name (Vietnamese/English, any case)
- **Returns:** Map ID if found, null otherwise
- **Features:** Accent-insensitive, fuzzy matching

**Examples:**
\\\csharp
resolver.Resolve("G?ng")      // ? 5
resolver.Resolve("gung")      // ? 5 (no accent)
resolver.Resolve("G?NG")      // ? 5 (case insensitive)
resolver.Resolve("Ginger")    // ? 5 (English)
\\\

---

### GraphCache Class

#### Load
\\\csharp
public MapGraph Load()
\\\
- **Returns:** Loaded MapGraph or null if no cache
- **File:** bfs_map_cache.json

#### Save
\\\csharp
public void Save(MapGraph graph)
\\\
- **Parameters:** Graph to serialize
- **Format:** JSON

#### Clear
\\\csharp
public void Clear()
\\\
- **Effect:** Deletes cache file

---

## CACHE FILE FORMAT

**File:** \fs_map_cache.json\

**Structure:**
\\\json
{
  "Edges": [
    {
      "SourceMapId": 1,
      "DestinationMapId": 3,
      "PortalX": 120.5,
      "PortalY": 80.2
    }
  ]
}
\\\

---

## ERROR CODES

### Path Computation
- **Null path:** Target unreachable or unknown
- **Empty graph:** Cache failed to load, discovery failed

### Navigation
- **Portal not found:** Graph incomplete or stale
- **Timeout:** Portal interaction took too long
- **Map mismatch:** Arrived at wrong map

---

## LOGGING

### Log Levels
- **Info:** Normal operation (path computed, navigation started)
- **Warning:** Recoverable issues (cache miss, retry)
- **Error:** Failures (no path, portal not found)

### Key Log Messages
\\\
[BFSPathfinder] Graph loaded from cache (X maps, Y edges)
[BFSPathfinder] Computing path to: <name>
[BFSPathfinder] Resolved '<name>' to ID: <id>
[BFSPathfinder] Path found: <hops> hops
[NavigationController] Executing path: <count>
[NavigationController] Step X/Y: Map A ? B
\\\

---

*API Documentation v1.0*
*Phase 3 Complete*
