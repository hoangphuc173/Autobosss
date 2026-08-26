# Implementation Plan: BFS Pathfinder

## Overview

This implementation plan breaks down the BFS Pathfinder feature into discrete coding tasks. The feature enables automatic map-to-map navigation by computing shortest paths using breadth-first search algorithm. Tasks are ordered to build foundational components first, followed by integration, testing, and polish.

**Key Milestones:**
1. Core data structures (MapGraph, PortalEdge)
2. BFS algorithm implementation
3. Caching and persistence
4. Navigation controller for portal traversal
5. SocketClient integration
6. Property-based tests for correctness guarantees

## Tasks

- [x] 1. Create core data structures and models
  - [x] 1.1 Create PortalEdge class
    - Define DestinationMapId (int), PortalPosition (Vector3) properties
    - Add JSON serialization attributes
    - _Requirements: 1.5_
  
  - [x] 1.2 Create MapGraph class
    - Implement adjacency list using Dictionary<int, List<PortalEdge>>
    - Implement AddEdge(source, destination, position) method
    - Implement GetEdges(mapId) method returning IEnumerable<PortalEdge>
    - Implement ContainsMap(mapId) method
    - Add NodeCount and EdgeCount properties
    - _Requirements: 1.3, 1.4_
  
  - [ ]* 1.3 Write property test for MapGraph edge addition
    - **Property 1: Graph Construction Validity**
    - **Validates: Requirements 1.2, 1.3**
  
  - [ ]* 1.4 Write property test for bidirectional edges
    - **Property 2: Bidirectional Edge Independence**
    - **Validates: Requirements 1.4**

- [x] 2. Implement graph serialization and caching
  - [x] 2.1 Add MapGraph serialization methods
    - Implement ToJson() method using JsonConvert.SerializeObject
    - Implement static FromJson(json) method using JsonConvert.DeserializeObject
    - _Requirements: 3.1_
  
  - [x] 2.2 Create CacheData model class
    - Add properties: Timestamp, GraphJson, ResolverJson, MapCount, EdgeCount
    - _Requirements: 3.6_
  
  - [x] 2.3 Create GraphCache class
    - Implement TryLoad(out MapGraph, out MapNameResolver) with 7-day TTL check
    - Implement Save(MapGraph, MapNameResolver) to BepInEx\config\AutoBossGrabber\map_graph.json
    - Implement Delete() method for cache invalidation
    - Add error handling for corrupted cache files
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.7, 3.8_
  
  - [ ]* 2.4 Write property test for serialization round-trip
    - **Property 3: Graph Serialization Round-Trip**
    - **Validates: Requirements 3.1**

- [x] 3. Checkpoint - Verify core data structures work
  - Ensure all tests pass, verify MapGraph and caching work correctly. Ask the user if questions arise.

- [ ] 4. Implement MapNameResolver
  - [~] 4.1 Create MapNameResolver class
    - Implement bidirectional dictionaries: _idToName and _nameToId (case-insensitive)
    - Implement RegisterMap(mapId, mapName) method
    - Implement Resolve(mapName) returning int (returns -1 if not found)
    - Implement GetName(mapId) returning string (returns null if not found)
    - _Requirements: 11.1, 11.2_
  
  - [~] 4.2 Add MapNameResolver serialization methods
    - Implement ToJson() and static FromJson() methods
    - _Requirements: 11.1_
  
  - [ ]* 4.3 Write property test for name resolution consistency
    - **Property 10: Name Resolution Bidirectional Consistency**
    - **Validates: Requirements 11.1**
  
  - [ ]* 4.4 Write property test for case-insensitive resolution
    - **Property 11: Case-Insensitive Name Resolution**
    - **Validates: Requirements 11.3**

- [ ] 5. Implement BFS algorithm
  - [~] 5.1 Create BFSPathfinder class skeleton
    - Add private fields: _graph, _nameResolver, _cache, _graphLock
    - Add constructor initializing cache and nameResolver (lazy graph init)
    - _Requirements: 1.1_
  
  - [~] 5.2 Implement BFS core algorithm
    - Implement private BFS(source, destination) method
    - Use Queue<int> for BFS queue
    - Use HashSet<int> for visited tracking
    - Use Dictionary<int, int> for parent tracking
    - Implement path reconstruction in ReconstructPath() helper method
    - Handle source == destination edge case (return single-element list)
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.8_
  
  - [ ]* 5.3 Write property test for BFS shortest path guarantee
    - **Property 4: BFS Shortest Path Guarantee**
    - **Validates: Requirements 2.2, 2.7**
  
  - [ ]* 5.4 Write property test for path boundary validity
    - **Property 5: Path Boundary Validity**
    - **Validates: Requirements 2.3**
  
  - [ ]* 5.5 Write property test for path connectivity
    - **Property 6: Path Connectivity**
    - **Validates: Requirements 2.3**
  
  - [ ]* 5.6 Write property test for unreachable destination handling
    - **Property 7: Unreachable Destination Handling**
    - **Validates: Requirements 2.5**
  
  - [ ]* 5.7 Write property test for cycle termination
    - **Property 8: Cycle Termination**
    - **Validates: Requirements 2.4**
  
  - [ ]* 5.8 Write property test for identity path
    - **Property 9: Identity Path**
    - **Validates: Requirements 2.8**

- [ ] 6. Implement graph construction from game data
  - [~] 6.1 Add GameAPI.FindAllMapGateways() placeholder
    - Create GameAPI stub method returning List<MapGateway> (to be implemented later with actual game integration)
    - _Requirements: 1.1_
  
  - [~] 6.2 Implement BuildGraph() method in BFSPathfinder
    - Scan MapGateway objects via GameAPI.FindAllMapGateways()
    - Extract sourceMapId, destinationMapId, portalPosition from each gateway
    - Call _graph.AddEdge() for each gateway
    - Register map names via _nameResolver.RegisterMap()
    - Log graph statistics (map count, edge count, build time)
    - Handle empty MapGateway list gracefully
    - Save graph to cache after construction
    - _Requirements: 1.1, 1.2, 1.6, 1.7, 1.8_
  
  - [~] 6.3 Implement EnsureGraphLoaded() helper method
    - Check if _graph is null
    - Try loading from cache via _cache.TryLoad()
    - Fall back to BuildGraph() if cache miss
    - Add thread safety with _graphLock
    - _Requirements: 3.2, 3.3, 3.4_
  
  - [ ]* 6.4 Write unit tests for BuildGraph edge cases
    - Test empty MapGateway list (graceful degradation)
    - Test successful graph construction with sample data
    - Verify logging output
    - _Requirements: 1.7_

- [~] 7. Checkpoint - Verify BFS algorithm correctness
  - Run all property tests, ensure BFS handles all graph types correctly. Ask the user if questions arise.

- [ ] 8. Implement public API methods in BFSPathfinder
  - [~] 8.1 Implement ComputePath(targetMapName) public method
    - Call EnsureGraphLoaded()
    - Get current map ID via GameAPI.GetCurrentMapId()
    - Resolve target map name to ID via _nameResolver.Resolve()
    - Call BFS(currentMapId, targetMapId)
    - Add thread safety with _graphLock
    - Log path computation results
    - _Requirements: 2.1, 4.1, 11.2_
  
  - [~] 8.2 Implement InvalidateCache() public method
    - Delete cache file via _cache.Delete()
    - Set _graph = null to force rebuild
    - Log invalidation event
    - Add thread safety with _graphLock
    - _Requirements: 3.5_
  
  - [ ]* 8.3 Write integration tests for ComputePath
    - Test with valid map name (returns valid path)
    - Test with unknown map name (returns null)
    - Test with unreachable map (returns null)
    - _Requirements: 4.1, 4.3, 4.4_

- [ ] 9. Implement NavigationController for portal traversal
  - [~] 9.1 Create NavigationController class
    - Add private fields: _graph, _isNavigating
    - Add constructor accepting MapGraph parameter
    - _Requirements: 5.1_
  
  - [~] 9.2 Implement ExecutePath(path) coroutine
    - Validate path (non-null, length >= 2)
    - Iterate through path executing portal traversal for each step
    - Verify arrival at expected map after each portal
    - Handle navigation errors and abort
    - Send STATUS_UPDATE when navigation completes
    - _Requirements: 5.1, 5.2, 5.7_
  
  - [~] 9.3 Implement TraversePortal(portal) helper coroutine
    - Move player to portal position via GameAPI.MoveToPosition()
    - Wait for movement to complete (0.5s)
    - Interact with portal via GameAPI.InteractWithPortal()
    - Poll for map transition with 10s timeout
    - Implement exponential backoff retry (up to 3 attempts)
    - Log traversal success/failure
    - _Requirements: 5.2, 5.3, 5.4, 5.5, 5.6, 8.3_
  
  - [~] 9.4 Implement AbortNavigation() method
    - Set _isNavigating = false
    - Log abort event
    - _Requirements: 4.7_
  
  - [ ]* 9.5 Write integration tests for NavigationController
    - Test successful path execution with mocked GameAPI
    - Test portal timeout handling
    - Test wrong map arrival (recompute path)
    - Test abort during navigation
    - _Requirements: 5.5, 5.6, 5.7, 8.3_

- [ ] 10. Integrate BFSPathfinder with SocketClient
  - [~] 10.1 Add BFSPathfinder instance to SocketClient
    - Declare private _pathfinder and _navigationController fields
    - Initialize in SocketClient constructor or Awake()
    - _Requirements: 4.1_
  
  - [~] 10.2 Replace TELEPORT_TO_MAP placeholder in HandleCommand()
    - Extract targetMap from message.Payload
    - Call _pathfinder.ComputePath(targetMap)
    - If path is null, send ERROR message to Manager ("Map unreachable")
    - If path is valid, start navigation via runner.StartCoroutine(_navigationController.ExecutePath(path))
    - Send ACK message with navigation status
    - Log command execution
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.8_
  
  - [~] 10.3 Implement INVALIDATE_CACHE command handler
    - Replace existing placeholder in HandleCommand()
    - Call _pathfinder.InvalidateCache()
    - Send ACK message confirming cache invalidation
    - _Requirements: 3.5_
  
  - [ ]* 10.4 Write integration tests for TELEPORT_TO_MAP command flow
    - Test full command flow from IPC message to navigation start
    - Test error handling (unknown map, unreachable map)
    - Test cache invalidation command
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 3.5_

- [~] 11. Checkpoint - Verify end-to-end integration
  - Test TELEPORT_TO_MAP command from Manager UI, verify navigation executes. Ask the user if questions arise.

- [ ] 12. Add error handling and logging
  - [~] 12.1 Add comprehensive error handling to BFSPathfinder
    - Handle JsonException during cache load/save
    - Handle IOException during file operations
    - Handle ArgumentException for invalid map names
    - Log all errors with context (map names, graph state)
    - _Requirements: 8.1, 8.2, 8.3, 8.8_
  
  - [~] 12.2 Add detailed logging throughout navigation
    - Log path computation requests and results
    - Log each portal traversal step
    - Log cache hits/misses
    - Log graph construction statistics
    - Add performance timing logs (pathfinding duration)
    - _Requirements: 4.8, 9.9, 12.2_
  
  - [ ]* 12.3 Write unit tests for error scenarios
    - Test corrupted cache file handling
    - Test missing targetMap parameter
    - Test empty graph (no MapGateways found)
    - Test portal traversal timeout
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [ ] 13. Add performance optimizations
  - [~] 13.1 Implement object pooling for BFS data structures
    - Pool Queue<int>, HashSet<int>, Dictionary<int,int> to reduce GC pressure
    - _Requirements: 9.7_
  
  - [~] 13.2 Add performance metrics tracking
    - Track pathfinding duration per request
    - Track cache hit rate
    - Track average path length
    - Log performance metrics periodically
    - _Requirements: 9.8, 12.3_
  
  - [ ]* 13.3 Write performance benchmark tests
    - Test pathfinding on large graphs (200+ maps)
    - Verify <50ms pathfinding time for 99% of queries
    - Verify <5MB memory footprint for graph
    - Verify <500ms graph construction time
    - _Requirements: 2.6, 9.1, 9.2, 9.3, 9.6, 1.8_

- [ ] 14. Add analytics and debugging support
  - [~] 14.1 Implement pathfinding statistics tracking
    - Track total paths computed, average path length, success rate
    - Track navigation success rate, failure reasons
    - Persist statistics to JSON file
    - _Requirements: 12.1, 12.4_
  
  - [~] 14.2 Add debug visualization support
    - Implement ExportToDot() method generating DOT format graph
    - Add debug mode toggle for verbose logging
    - Log graph structure on demand
    - _Requirements: 12.6, 12.7_
  
  - [ ]* 14.3 Write integration tests for analytics
    - Test statistics persistence across sessions
    - Test DOT export with sample graph
    - Verify metrics accuracy
    - _Requirements: 12.1, 12.7_

- [ ] 15. Final checkpoint and documentation
  - [~] 15.1 Update SocketClient documentation
    - Document TELEPORT_TO_MAP command usage
    - Document INVALIDATE_CACHE command
    - Add code comments for pathfinding integration
    - _Requirements: All_
  
  - [~] 15.2 Create usage examples and test scenarios
    - Document common navigation scenarios
    - Create sample TELEPORT_TO_MAP IPC messages
    - Document cache invalidation workflow
    - _Requirements: All_
  
  - [~] 15.3 Run full test suite and verify all requirements
    - Ensure all property tests pass (100+ iterations each)
    - Ensure all unit tests pass
    - Ensure all integration tests pass
    - Verify performance benchmarks meet targets
    - _Requirements: All_

- [~] 16. Final checkpoint - Production readiness check
  - Ensure all tests pass, verify TELEPORT_TO_MAP command works end-to-end from Manager UI. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test-related sub-tasks and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests validate universal correctness properties with 100+ iterations
- Integration tests verify component interactions with mocked dependencies
- Unit tests cover specific examples and edge cases
- Checkpoints ensure incremental validation and provide opportunities for user feedback

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "1.4", "2.1", "2.2"] },
    { "id": 2, "tasks": ["2.3", "2.4"] },
    { "id": 3, "tasks": ["4.1", "4.2"] },
    { "id": 4, "tasks": ["4.3", "4.4", "5.1"] },
    { "id": 5, "tasks": ["5.2"] },
    { "id": 6, "tasks": ["5.3", "5.4", "5.5", "5.6", "5.7", "5.8"] },
    { "id": 7, "tasks": ["6.1", "6.2", "6.3"] },
    { "id": 8, "tasks": ["6.4", "8.1", "8.2"] },
    { "id": 9, "tasks": ["8.3", "9.1"] },
    { "id": 10, "tasks": ["9.2", "9.3", "9.4"] },
    { "id": 11, "tasks": ["9.5", "10.1"] },
    { "id": 12, "tasks": ["10.2", "10.3"] },
    { "id": 13, "tasks": ["10.4", "12.1", "12.2"] },
    { "id": 14, "tasks": ["12.3", "13.1", "13.2"] },
    { "id": 15, "tasks": ["13.3", "14.1", "14.2"] },
    { "id": 16, "tasks": ["14.3", "15.1", "15.2"] },
    { "id": 17, "tasks": ["15.3"] }
  ]
}
```
