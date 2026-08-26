# Requirements Document

## Introduction

This document specifies the requirements for implementing the BFS Pathfinder feature in AutoBossGrabber. The BFS Pathfinder enables intelligent map navigation by automatically computing the shortest path between any two maps in the game world using breadth-first search algorithm.

Currently, the TELEPORT_TO_MAP command exists in the IPC protocol but is not implemented. This feature will enable that command by providing automatic pathfinding capabilities, eliminating the need for operators to manually configure portal chains.

**Key Benefits:**
- Automatic map navigation without manual portal configuration
- Shortest path computation for efficient traversal
- Dynamic graph construction from game data
- Support for unlockable maps (graph updates as new maps become accessible)

**Context:**
- The IPC infrastructure (SocketClient/SocketServer) is complete
- TELEPORT_TO_MAP command is defined but returns a "not implemented" warning
- The system must be IL2CPP compatible and run within BepInEx plugin environment

## Glossary

- **BFS_Pathfinder**: Component that implements breadth-first search algorithm for map pathfinding
- **Map_Graph**: Data structure representing maps as nodes and portals as directed edges
- **MapGateway**: Game object representing a portal connection between two maps
- **Portal**: In-game object that transports player from one map to another when interacted with
- **Path**: Ordered sequence of map IDs from source map to destination map
- **Graph_Cache**: Persistent storage of the Map_Graph to avoid rebuilding on every game session
- **Fast_Travel_Anchor**: Special teleport points that provide shortcuts in the Map_Graph
- **Unreachable_Map**: A map that has no valid path from the current map
- **Navigation_State**: Bot state representing active map traversal (Navigating, Traversing_Portal, Arrived)
- **Portal_Interaction**: Simulated player action of clicking a portal and confirming the zone selection
- **Unity_Main_Thread**: The primary game thread where Unity API calls must be executed
- **Graph_Invalidation**: Process of clearing cached graph data when map unlocks or game data changes

## Requirements

### Requirement 1: Map Graph Construction

**User Story:** As a system component, I want to build a graph of all maps and portals from game data, so that I can compute paths between any two maps.

#### Acceptance Criteria

1. WHEN BFS_Pathfinder initializes, THE BFS_Pathfinder SHALL scan all MapGateway objects in the game world
2. THE BFS_Pathfinder SHALL extract from each MapGateway: source map ID, destination map ID, portal position
3. THE Map_Graph SHALL represent maps as integer node IDs and portals as directed edges
4. THE Map_Graph SHALL support bidirectional edges where portals allow travel in both directions
5. THE BFS_Pathfinder SHALL store edge data including: destination map ID, portal world position, portal zone
6. WHEN graph construction completes, THE BFS_Pathfinder SHALL log the total number of maps and edges discovered
7. IF no MapGateway objects are found, THEN THE BFS_Pathfinder SHALL log a warning and operate in degraded mode
8. THE BFS_Pathfinder SHALL complete graph construction within 500ms on startup

### Requirement 2: Breadth-First Search Algorithm

**User Story:** As a navigation system, I want to compute the shortest path between two maps using BFS, so that bots take the most efficient route.

#### Acceptance Criteria

1. WHEN a path is requested from source to destination, THE BFS_Pathfinder SHALL execute breadth-first search algorithm
2. THE BFS algorithm SHALL explore maps in level-order traversal ensuring shortest path discovery
3. THE BFS_Pathfinder SHALL return a path as a list of map IDs ordered from source to destination
4. THE BFS_Pathfinder SHALL track visited maps to avoid infinite loops in cyclic graphs
5. IF the destination is unreachable from source, THEN THE BFS_Pathfinder SHALL return null or empty path
6. THE BFS_Pathfinder SHALL complete pathfinding within 50ms for graphs with up to 200 maps
7. FOR ANY valid source and destination in the Map_Graph, the returned path SHALL be one of the shortest possible paths
8. THE BFS_Pathfinder SHALL handle the edge case where source equals destination by returning a single-element path

### Requirement 3: Graph Caching and Invalidation

**User Story:** As a performance-conscious system, I want to cache the map graph to disk, so that I don't rebuild it on every game session.

#### Acceptance Criteria

1. THE BFS_Pathfinder SHALL serialize the Map_Graph to JSON and save it to BepInEx\config\AutoBossGrabber\map_graph.json
2. WHEN BFS_Pathfinder initializes, THE BFS_Pathfinder SHALL attempt to load the cached graph from disk
3. IF cached graph file exists and is less than 7 days old, THEN THE BFS_Pathfinder SHALL use the cached graph
4. IF cached graph file does not exist or is older than 7 days, THEN THE BFS_Pathfinder SHALL rebuild from game data
5. WHEN INVALIDATE_CACHE command is received, THE BFS_Pathfinder SHALL delete the cached graph file and rebuild from game data
6. THE cached graph file SHALL include metadata: creation timestamp, game version, total maps, total edges
7. THE BFS_Pathfinder SHALL validate cached graph integrity by checking for corrupted data
8. IF cached graph load fails due to corruption, THEN THE BFS_Pathfinder SHALL log an error and rebuild from game data

### Requirement 4: TELEPORT_TO_MAP Command Implementation

**User Story:** As a bot operator, I want to send TELEPORT_TO_MAP commands from the Manager, so that bots automatically navigate to target maps without manual intervention.

#### Acceptance Criteria

1. WHEN SocketClient receives TELEPORT_TO_MAP command with targetMap parameter, THE SocketClient SHALL invoke BFS_Pathfinder to compute path
2. THE SocketClient SHALL validate that targetMap parameter exists and is a valid string
3. IF BFS_Pathfinder returns a valid path, THEN THE SocketClient SHALL initiate navigation along that path
4. IF BFS_Pathfinder returns no path, THEN THE SocketClient SHALL send ERROR message to Manager with reason "Map unreachable"
5. THE SocketClient SHALL transition bot state to Navigating when path execution begins
6. WHEN path execution completes, THE SocketClient SHALL send STATUS_UPDATE to Manager indicating arrival
7. THE SocketClient SHALL support aborting navigation if STOP_FARMING or PAUSE command is received during traversal
8. THE SocketClient SHALL log each portal traversal step for debugging and analytics

### Requirement 5: Portal Traversal Logic

**User Story:** As a navigation system, I want to automatically traverse portals along computed paths, so that bots physically move between maps.

#### Acceptance Criteria

1. WHEN navigating a path, THE BFS_Pathfinder SHALL process portals sequentially from source to destination
2. FOR EACH portal in the path, THE BFS_Pathfinder SHALL retrieve the portal's world position from graph data
3. THE BFS_Pathfinder SHALL move the player character to within interaction range of the portal (distance < 2.0 units)
4. THE BFS_Pathfinder SHALL simulate portal interaction by invoking the game's portal use function
5. WHEN portal interaction is triggered, THE BFS_Pathfinder SHALL wait for map transition confirmation (current map ID changes)
6. IF map transition does not complete within 10 seconds, THEN THE BFS_Pathfinder SHALL timeout and report navigation failure
7. THE BFS_Pathfinder SHALL verify that the player arrived at the expected destination map after each portal
8. IF the player arrives at an unexpected map, THEN THE BFS_Pathfinder SHALL recompute path from current location

### Requirement 6: Fast Travel Integration

**User Story:** As an advanced navigation feature, I want to use fast travel anchors when available, so that navigation is faster than portal-only paths.

#### Acceptance Criteria

1. THE Map_Graph SHALL include Fast_Travel_Anchor nodes representing teleport points
2. WHEN constructing the graph, THE BFS_Pathfinder SHALL detect unlocked fast travel anchors
3. THE BFS algorithm SHALL treat fast travel anchors as zero-cost edges in the graph
4. WHEN a path includes a fast travel anchor, THE BFS_Pathfinder SHALL prefer it over multi-portal chains
5. THE BFS_Pathfinder SHALL verify fast travel anchor accessibility before using it
6. IF a fast travel anchor is locked or unavailable, THEN THE BFS_Pathfinder SHALL recompute path excluding that anchor
7. THE Map_Graph SHALL support dynamic fast travel anchor unlock detection
8. THE BFS_Pathfinder SHALL log fast travel usage for analytics purposes

### Requirement 7: Thread Safety and Unity Main Thread Execution

**User Story:** As a system architect, I want BFS pathfinding to be thread-safe, so that it can be safely called from background threads without crashing Unity.

#### Acceptance Criteria

1. THE BFS_Pathfinder SHALL use thread-safe data structures for the Map_Graph (locks or concurrent collections)
2. THE pathfinding algorithm SHALL be safe to call from any thread without modifying shared state
3. WHEN portal traversal actions are needed, THE BFS_Pathfinder SHALL enqueue them to the Unity_Main_Thread_Queue
4. THE BFS_Pathfinder SHALL NOT directly invoke Unity API calls outside the main thread
5. THE BFS_Pathfinder SHALL provide async/await compatible methods for path computation
6. THE BFS_Pathfinder SHALL log thread ID when executing pathfinding for debugging purposes
7. THE BFS_Pathfinder SHALL handle race conditions during graph invalidation gracefully
8. THE BFS_Pathfinder SHALL use proper locking when reading/writing the cached graph file

### Requirement 8: Error Handling and Fallback Behavior

**User Story:** As a robust system, I want graceful error handling for pathfinding failures, so that navigation errors don't crash the bot.

#### Acceptance Criteria

1. WHEN pathfinding encounters an error, THE BFS_Pathfinder SHALL log detailed error information
2. IF Map_Graph is empty or corrupted, THEN THE BFS_Pathfinder SHALL attempt to rebuild from game data
3. IF a portal interaction fails, THEN THE BFS_Pathfinder SHALL retry up to 3 times with exponential backoff
4. IF navigation fails after all retries, THEN THE BFS_Pathfinder SHALL send ERROR event to Manager
5. THE BFS_Pathfinder SHALL provide fallback to manual hard-coded portal chains if graph construction fails
6. WHEN an unexpected map is reached, THE BFS_Pathfinder SHALL log the deviation and recompute path
7. THE BFS_Pathfinder SHALL validate all path computations before execution
8. THE BFS_Pathfinder SHALL include error codes in ERROR messages for programmatic handling

### Requirement 9: Performance and Memory Constraints

**User Story:** As a multi-instance system, I want BFS pathfinding to use minimal memory and CPU, so that it doesn't impact the ability to run 10+ bots.

#### Acceptance Criteria

1. THE Map_Graph SHALL consume less than 5MB of memory for typical game worlds (up to 200 maps)
2. THE BFS algorithm SHALL use memory-efficient queue implementation to minimize allocations
3. THE BFS_Pathfinder SHALL complete pathfinding within 50ms for 99% of path queries
4. THE BFS_Pathfinder SHALL reuse data structures across pathfinding calls to reduce GC pressure
5. THE cached graph file SHALL be less than 1MB in size
6. THE BFS_Pathfinder SHALL not cause Unity frame drops during path computation
7. THE BFS_Pathfinder SHALL use object pooling for frequently allocated objects
8. THE BFS_Pathfinder SHALL report performance metrics: pathfinding duration, graph size, cache hit rate

### Requirement 10: Integration with Existing Bot State Machine

**User Story:** As a bot control system, I want pathfinding to integrate seamlessly with the existing AutoBossState machine, so that navigation doesn't conflict with other bot behaviors.

#### Acceptance Criteria

1. THE BFS_Pathfinder SHALL define a new Navigation_State for the bot state machine
2. WHEN navigation begins, THE bot SHALL transition from current state to Navigation_State
3. THE Navigation_State SHALL have priority over other states except emergency stop
4. WHEN navigation completes, THE bot SHALL transition back to Idle or previous state
5. IF STOP_FARMING command is received during Navigation_State, THEN THE bot SHALL abort navigation immediately
6. THE Navigation_State SHALL publish progress updates every 5 seconds during traversal
7. THE BFS_Pathfinder SHALL integrate with existing teleport logic for RETURN_TO_TOWN command
8. THE BFS_Pathfinder SHALL respect bot pause state and halt navigation during pause

### Requirement 11: Map Name Resolution

**User Story:** As a user-friendly system, I want to accept human-readable map names, so that operators don't need to know internal map IDs.

#### Acceptance Criteria

1. THE BFS_Pathfinder SHALL maintain a bidirectional mapping between map names and map IDs
2. WHEN TELEPORT_TO_MAP command specifies a map name, THE BFS_Pathfinder SHALL resolve it to the corresponding map ID
3. THE BFS_Pathfinder SHALL support fuzzy matching for map names (case-insensitive, partial matches)
4. IF multiple maps match a partial name, THEN THE BFS_Pathfinder SHALL return the closest match or prompt user
5. THE BFS_Pathfinder SHALL log both map name and map ID when computing paths
6. THE Map_Graph SHALL store localized map names for display in Manager UI
7. IF a map name cannot be resolved, THEN THE BFS_Pathfinder SHALL return an error with suggested similar names
8. THE BFS_Pathfinder SHALL support map name aliases for common abbreviations

### Requirement 12: Analytics and Debugging Support

**User Story:** As a system operator, I want detailed analytics on pathfinding usage, so that I can optimize navigation and debug issues.

#### Acceptance Criteria

1. THE BFS_Pathfinder SHALL track statistics: total paths computed, average path length, cache hit rate, navigation success rate
2. THE BFS_Pathfinder SHALL log all pathfinding requests with source, destination, and computed path
3. THE BFS_Pathfinder SHALL report pathfinding duration and performance metrics to Manager
4. THE BFS_Pathfinder SHALL maintain a history of recent navigation attempts with outcomes
5. WHEN navigation fails, THE BFS_Pathfinder SHALL include diagnostic information: graph state, portal positions, player position
6. THE BFS_Pathfinder SHALL provide a debug mode that visualizes the Map_Graph structure
7. THE BFS_Pathfinder SHALL export graph data to DOT format for visualization in external tools
8. THE Manager SHALL display pathfinding analytics in a dedicated UI panel

---

## Technical Constraints

**IL2CPP Compatibility:**
- Must use IL2CPP-compatible serialization (Newtonsoft.Json)
- Cannot use reflection-heavy operations
- Must be compatible with Unity's IL2CPP scripting backend

**BepInEx Environment:**
- Plugin DLL loaded at game startup
- Must use BepInEx logging infrastructure
- Config files stored in BepInEx\config\AutoBossGrabber\

**Performance Requirements:**
- <50ms pathfinding for 200-map graphs
- <5MB memory for graph storage
- <500ms graph construction time
- Must not block Unity main thread

**Thread Safety:**
- Safe to call from background IPC receive thread
- Unity API calls must be queued to main thread
- Proper locking for shared graph data

## Notes

**Scope Decisions:**
- Phase 1 focuses on basic BFS pathfinding with portal traversal
- Fast travel anchor support is optional (can be added later)
- Fuzzy map name matching is a nice-to-have enhancement
- DOT format export for debugging is optional

**Integration Points:**
- SocketClient.HandleCommand() already has TELEPORT_TO_MAP case (currently placeholder)
- Commands.INVALIDATE_CACHE already defined in IPC protocol
- AutoBossState enum may need new Navigating state added

**Game Data Dependencies:**
- MapGateway object structure (needs reverse engineering)
- Portal interaction API (needs game API investigation)
- Map ID format and naming conventions
- Fast travel anchor detection (if implementing Requirement 6)

