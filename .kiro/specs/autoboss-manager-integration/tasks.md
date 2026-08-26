# Implementation Plan: AutoBoss Manager Integration

## Overview

This implementation plan transforms AutoBossGrabber from a standalone BepInEx plugin into a centralized multi-account bot management system. The plan is organized into phases with clear dependencies, prioritizing features that enable 10+ instances per machine.

**Key Objectives:**
- Enable 10+ game instances on one machine through memory/CPU optimization
- Provide centralized desktop application for managing all bot instances
- Implement robust IPC for thread-safe remote command execution
- Add smart BFS pathfinding to eliminate manual portal configuration
- Preserve existing strengths (boss detection, skill manager, captcha AI)

**Technology Stack:**
- Language: C#
- Game Integration: BepInEx + Harmony
- UI Framework: WPF with MVVM pattern
- IPC: TCP Sockets with JSON line-delimited protocol
- Serialization: Newtonsoft.Json

---

## Tasks

### Phase 1: Core Infrastructure (Critical Path)

- [x] 1. Set up project structure and core interfaces
  - Create AutoBossManager.sln (WPF application project)
  - Create AutoBossClient.csproj (BepInEx plugin project)
  - Define shared data models (IpcMessage, BotProfile, BotInstanceState)
  - Set up Newtonsoft.Json and Harmony dependencies
  - Create GameAPI.cs reflection wrapper for game types
  - _Requirements: 1.1, 1.6_
  - _Design Reference: Section "Data Models"_
  - _Estimated Effort: 1 day_

- [x] 2. Implement IPC message protocol and serialization
  - [x] 2.1 Create IpcMessage class with Type, Timestamp, Payload fields
    - Implement JSON serialization/deserialization
    - Define MessageTypes constants (COMMAND, STATUS_UPDATE, HEARTBEAT, etc.)
    - Define Commands constants (START_FARMING, STOP_FARMING, etc.)
    - Add message validation logic
    - _Requirements: 3.5_
    - _Design Reference: Section "IPC Message Protocol"_
  
  - [ ]* 2.2 Write unit tests for IpcMessage serialization
    - Test JSON round-trip serialization
    - Test message validation with invalid payloads
    - Test timestamp handling across timezones
    - _Requirements: 3.5_
  
  - [x] 2.3 Create BotProfile configuration model
    - Implement all properties (AccountName, TargetBossNames, Strategy, etc.)
    - Add validation rules (required fields, min/max values)
    - Implement StrategyPreset enum (Aggressive, Balanced, Safe, Custom)
    - Add SkillTrigger model with HpThreshold, SkillKey, SpamCount
    - _Requirements: 6.3_
    - _Design Reference: Section "BotProfile (Manager Storage)"_
  
  - [x] 2.4 Create BotInstanceState runtime model
    - Implement all status fields (CurrentState, CurrentMap, BossKills, etc.)
    - Add ConnectionStatus enum (Disconnected, Connected, Active, Paused, Error)
    - Add AutoBossState enum (Idle, DetectBoss, MoveToBoss, etc.)
    - Implement calculated properties (Uptime, BossKillsPerHour)
    - _Requirements: 5.4_
    - _Design Reference: Section "BotInstanceState (Runtime State)"_

- [x] 3. Implement GameOptimizer module (CRITICAL for 10+ instances)
  - [x] 3.1 Create GameOptimizer.cs with P/Invoke declarations
    - Add [DllImport] for EmptyWorkingSet from psapi.dll
    - Add [DllImport] for SetProcessWorkingSetSize from kernel32.dll
    - Implement Initialize() method with Harmony parameter
    - Add configuration fields (OptimizationIntervalSec = 60f)
    - _Requirements: 2.1, 2.2_
    - _Design Reference: Section "GameOptimizer Module"_
  
  - [x] 3.2 Implement GC optimization logic
    - Set GCSettings.LatencyMode to SustainedLowLatency
    - Set GCSettings.LargeObjectHeapCompactionMode to CompactOnce
    - Force full GC collection with blocking mode
    - Call GC.WaitForPendingFinalizers()
    - Log memory freed after optimization
    - _Requirements: 2.3, 2.4_
  
  - [x] 3.3 Implement memory optimization execution
    - Get current process handle via Process.GetCurrentProcess().Handle
    - Call EmptyWorkingSet to release unused working set memory
    - Call SetProcessWorkingSetSize with (-1, -1) to minimize footprint
    - Execute optimization every 60 seconds in Update()
    - Log memory metrics (before/after) when freed > 1MB
    - _Requirements: 2.1, 2.2, 2.8_
  
  - [x] 3.4 Apply Harmony patches for rendering optimization
    - Find ParallaxBackground type using GameAPI.FindTypeByName()
    - Patch FixedUpdate method with Postfix to disable component
    - Set QualitySettings.shadowDistance = 0f
    - Set QualitySettings.shadows = ShadowQuality.Disable
    - Handle patch failures gracefully with try-catch
    - _Requirements: 2.5, 2.6_
  
  - [ ]* 3.5 Write unit tests for GameOptimizer
    - Test P/Invoke declarations don't crash (mock calls)
    - Test GC settings are applied correctly
    - Test optimization interval timing
    - Verify SetEnabled() controls optimization execution
  
  - [ ]* 3.6 Validate memory optimization effectiveness
    - **Target: 30% RAM reduction (1200MB → 800MB per instance)**
    - Launch 2 instances: one with optimization, one without
    - Measure RAM usage after 5 minutes of farming
    - Verify optimized instance uses ≤800MB RAM
    - Log performance metrics to console
    - _Requirements: 2.7_

- [-] 4. Implement SocketClient with thread-safe command execution
  - [x] 4.1 Create SocketClient MonoBehaviour component
    - Add configuration constants (ServerHost = "127.0.0.1", ServerPort = 28081)
    - Create ConcurrentQueue&lt;Action&gt; for main thread command queue
    - Add connection state fields (TcpClient, StreamReader, StreamWriter, isConnected)
    - Add reconnection fields (reconnectAttempt, nextReconnectTime)
    - _Requirements: 3.1, 3.2_
    - _Design Reference: Section "SocketClient with Thread-Safe Command Execution"_
  
  - [ ] 4.2 Implement TCP connection and reconnection logic
    - Implement ConnectToManager() with TcpClient.Connect()
    - Initialize StreamReader/StreamWriter with UTF-8 encoding
    - Set writer.AutoFlush = true for immediate message sending
    - Start background receive thread after successful connection
    - Implement ScheduleReconnect() with exponential backoff (1s, 2s, 4s, 8s, max 30s)
    - _Requirements: 3.7_
  
  - [ ] 4.3 Implement background receive loop
    - Create ReceiveLoop() method running on background thread
    - Use StreamReader.ReadLine() for line-delimited JSON
    - Deserialize messages using JsonConvert.DeserializeObject&lt;IpcMessage&gt;()
    - Enqueue message handlers to mainThreadQueue using lambda closures
    - Handle IOException for connection loss, trigger reconnection
    - _Requirements: 3.3_
  
  - [ ] 4.4 Implement main thread command processing in Update()
    - Dequeue all pending actions from mainThreadQueue using TryDequeue()
    - Execute each action wrapped in try-catch for error isolation
    - Send ERROR message to Manager on command execution failure
    - Check reconnection timing and call ConnectToManager() if needed
    - _Requirements: 3.4_
  
  - [ ] 4.5 Implement command handlers
    - Create HandleMessage() router switching on message.Type
    - Implement HandleCommand() with switch on command string
    - Add handlers for START_FARMING, STOP_FARMING, PAUSE, RESUME
    - Add handlers for RETURN_TO_TOWN, TELEPORT_TO_MAP, SWITCH_ZONE
    - Add handlers for INVALIDATE_CACHE, RELOAD_CONFIG
    - Send ACK message after successful command execution
    - _Requirements: 13.1, 13.2, 13.3_
  
  - [ ] 4.6 Implement hot-reload configuration updates
    - Create HandleConfigUpdate() method
    - Parse payload and update Plugin.Instance.Config fields
    - Support maxZoneAttempts, retreatHpPct, attackRange updates
    - Deserialize bossSkillTriggers array from payload
    - Log successful config update to BepInEx log
    - _Requirements: 6.1, 6.2, 6.7_
  
  - [ ] 4.7 Implement message sending methods
    - Create SendMessage() wrapper with JSON serialization
    - Implement SendStatusUpdate() with game state fields
    - Implement SendBossFound() with boss/map/zone info
    - Implement SendBossKilled() with kill duration metric
    - Implement SendLogEvent() for log streaming
    - Implement SendError() for error reporting
    - Handle send failures by disconnecting and scheduling reconnection
    - _Requirements: 3.8, 8.2, 9.1_
  
  - [ ] 4.8 Implement heartbeat sender
    - Create StartHeartbeat() async Task method
    - Send HEARTBEAT message every 3 seconds using Task.Delay()
    - Use CancellationTokenSource for graceful shutdown
    - Handle TaskCanceledException on shutdown
    - _Requirements: 3.6_
  
  - [ ]* 4.9 Write unit tests for SocketClient
    - Mock TcpClient to test connection logic
    - Test mainThreadQueue enqueue/dequeue thread safety
    - Test reconnection exponential backoff timing
    - Test command execution error isolation
    - **Property 1: Commands enqueued are eventually executed**
    - **Validates: Requirements 3.3, 3.4**
  
  - [ ]* 4.10 Write integration tests for IPC
    - Start mock TCP server, connect SocketClient
    - Send COMMAND message, verify client executes and sends ACK
    - Simulate connection drop, verify reconnection succeeds
    - Measure command latency (target: <50ms send to ACK)
    - _Requirements: 3.8_

- [ ] 5. Checkpoint - Core IPC infrastructure complete
  - Ensure all tests pass, ask the user if questions arise.
  - Verify SocketClient can connect, receive commands, send status updates
  - Verify reconnection works after simulated network failure
  - Measure command execution latency (should be <50ms)

### Phase 2: Manager Application Foundation

- [ ] 6. Create AutoBossManager WPF application skeleton
  - [ ] 6.1 Create WPF project with MVVM structure
    - Create MainWindow.xaml with DashboardView placeholder
    - Create ViewModels folder (MainViewModel, BotInstanceViewModel)
    - Create Models folder (move BotProfile, BotInstanceState here)
    - Create Services folder (SocketServer, ProfileManager, AnalyticsEngine)
    - Set up dependency injection container (Microsoft.Extensions.DependencyInjection)
    - _Requirements: 1.1, 20.1_
  
  - [ ] 6.2 Implement MainViewModel with observable collections
    - Create ObservableCollection&lt;BotInstanceViewModel&gt; for bot instances
    - Add properties: TotalBossKills, TotalUptime, ConnectedClientCount
    - Implement INotifyPropertyChanged for reactive UI updates
    - Add RelayCommand implementations for StartAll, StopAll, EmergencyStop
    - _Requirements: 5.4, 17.1_
  
  - [ ] 6.3 Implement BotInstanceViewModel
    - Add observable properties matching BotInstanceState fields
    - Implement RelayCommands: StartCommand, StopCommand, PauseCommand, ResumeCommand
    - Add computed properties (StatusColor, UptimeFormatted)
    - Subscribe to SocketServer events for real-time updates
    - _Requirements: 5.4, 8.4_
  
  - [ ]* 6.4 Write unit tests for ViewModels
    - Test MainViewModel property change notifications
    - Test BotInstanceViewModel command execution
    - Test observable collection updates trigger UI refresh
    - Verify computed properties calculate correctly

- [ ] 7. Implement SocketServer for Manager
  - [ ] 7.1 Create SocketServer class with TcpListener
    - Initialize TcpListener on IPAddress.Loopback with configurable port
    - Create ConcurrentDictionary&lt;Guid, ClientConnection&gt; for client registry
    - Add events: OnStatusUpdate, OnBossFound, OnLogEvent, OnError
    - Implement Start() to begin accepting connections
    - _Requirements: 1.2, 1.3_
    - _Design Reference: Section "SocketServer Implementation (Manager Side)"_
  
  - [ ] 7.2 Implement client connection acceptance loop
    - Create AcceptLoop() async Task running on background thread
    - Accept connections using TcpListener.AcceptTcpClientAsync()
    - Create ClientConnection wrapper for each TcpClient
    - Assign unique Guid as InstanceId for each connection
    - Add to clients dictionary and subscribe to events
    - _Requirements: 1.3, 1.5_
  
  - [ ] 7.3 Implement ClientConnection class
    - Create per-client state (InstanceId, LastHeartbeat, TcpClient, reader, writer)
    - Implement background receive loop reading line-delimited JSON
    - Deserialize IpcMessage and raise OnMessage event
    - Track LastHeartbeat timestamp on HEARTBEAT messages
    - Implement SendMessage() for bidirectional communication
    - _Requirements: 1.3, 3.6_
  
  - [ ] 7.4 Implement heartbeat monitoring
    - Create HeartbeatMonitor() async Task checking clients every 5 seconds
    - Calculate elapsed time since LastHeartbeat for each client
    - Disconnect and remove clients with timeout > 10 seconds
    - Log timeout events to console
    - _Requirements: 1.8, 3.6_
  
  - [ ] 7.5 Implement message routing and event raising
    - Create HandleClientMessage() parsing message.Type
    - Route STATUS_UPDATE to OnStatusUpdate event
    - Route BOSS_FOUND to OnBossFound event with extracted payload
    - Route LOG_EVENT to OnLogEvent event
    - Route ERROR to OnError event
    - _Requirements: 8.2, 9.2_
  
  - [ ] 7.6 Implement command sending methods
    - Create SendCommand() taking Guid instanceId and command string
    - Implement BroadcastCommand() sending to all connected clients
    - Create SendConfigUpdate() serializing BotProfile to payload
    - Add command acknowledgment tracking (optional for Phase 1)
    - _Requirements: 13.2, 13.5_
  
  - [ ]* 7.7 Write unit tests for SocketServer
    - Mock TcpClient connections to test client registry
    - Test heartbeat timeout detection and disconnection
    - Test message routing to correct event handlers
    - Test command sending to specific clients vs broadcast
  
  - [ ]* 7.8 Write integration tests for SocketServer
    - Start SocketServer, connect multiple mock clients
    - Send STATUS_UPDATE from clients, verify events raised
    - Test heartbeat timeout disconnects stale clients
    - Send commands from server, verify clients receive them

- [ ] 8. Implement ProfileManager for configuration persistence
  - [ ] 8.1 Create ProfileManager service class
    - Define profile storage path (AppData/AutoBossManager/profiles/)
    - Implement LoadProfiles() reading all JSON files from directory
    - Implement SaveProfile() writing BotProfile to JSON file
    - Implement DeleteProfile() removing JSON file
    - Handle file I/O errors gracefully
    - _Requirements: 1.4, 5.2_
  
  - [ ] 8.2 Implement profile validation
    - Validate required fields (AccountName, GameExecutablePath, etc.)
    - Enforce minimum safe values (dwell time ≥ 0.5s, movement threshold ≥ 0.3f)
    - Enforce maximum safe values (maxZoneAttempts ≤ 50, session duration ≤ 12h)
    - Validate boss skill trigger HpThreshold and SkillKey ranges
    - Return validation error messages for UI display
    - _Requirements: 14.1, 14.2, 14.3, 14.6_
  
  - [ ] 8.3 Implement import/export functionality
    - Create ExportProfiles() serializing all profiles to single JSON file
    - Create ImportProfiles() deserializing JSON and validating each profile
    - Handle duplicate profile names (prompt overwrite/skip/rename)
    - Validate imported profiles against current schema version
    - _Requirements: 18.1, 18.2, 18.3, 18.7_
  
  - [ ] 8.4 Implement automatic backup
    - Create BackupProfiles() copying all profiles to timestamped backup file
    - Schedule daily backups in background thread
    - Retain last 7 backups, delete older files
    - Store backups in AppData/AutoBossManager/backups/
    - _Requirements: 18.5, 18.6_
  
  - [ ]* 8.5 Write unit tests for ProfileManager
    - Test profile save/load round-trip with all fields
    - Test validation rules reject invalid profiles
    - Test import handles duplicate names correctly
    - Test backup retention deletes old backups
    - **Property 2: Profile round-trip preserves all data**
    - **Validates: Requirements 1.4**

- [ ] 9. Implement dashboard UI with real-time updates
  - [ ] 9.1 Create DashboardView.xaml with bot grid
    - Add DataGrid bound to MainViewModel.BotInstances collection
    - Define columns: Account, Status, Map, Zone, HP, Kills, Uptime, LastUpdate
    - Add value converters for status color coding (green/yellow/red/gray)
    - Add toolbar with buttons: Start All, Stop All, Pause All, Emergency Stop
    - Enable sorting by clicking column headers
    - _Requirements: 5.4, 8.1, 8.7_
  
  - [ ] 9.2 Implement real-time status updates
    - Subscribe MainViewModel to SocketServer.OnStatusUpdate event
    - Update corresponding BotInstanceViewModel when status received
    - Throttle UI updates to max 1 per second per bot (avoid flooding)
    - Update dashboard columns automatically via data binding
    - _Requirements: 8.2_
  
  - [ ] 9.3 Add filtering and sorting controls
    - Implement filter dropdown (All, Active Only, Errors Only, Paused)
    - Apply filter using ICollectionView.Filter predicate
    - Implement sort by any column (status, uptime, boss kills, etc.)
    - Persist filter/sort preferences to user settings
    - _Requirements: 8.6, 8.7_
  
  - [ ] 9.4 Add bot control buttons per row
    - Add context menu with Start, Stop, Pause, Resume actions
    - Bind commands to BotInstanceViewModel.StartCommand, etc.
    - Show disabled state when action is not applicable
    - Display confirmation dialog for destructive actions (Stop All)
    - _Requirements: 5.5, 17.1, 17.2_
  
  - [ ]* 9.5 Test dashboard performance with 10+ clients
    - Connect 10+ mock clients sending status updates every second
    - Verify UI updates within 100ms (no lag or freezing)
    - Measure memory usage and CPU usage of Manager app
    - Verify throttling prevents UI update storms
    - _Requirements: 8.8, 20.8_

- [ ] 10. Checkpoint - Manager application core complete
  - Ensure all tests pass, ask the user if questions arise.
  - Verify SocketServer accepts connections and routes messages
  - Verify ProfileManager saves/loads profiles correctly
  - Verify Dashboard displays real-time status for multiple clients
  - Test control commands (Start, Stop) execute successfully

### Phase 3: Smart Navigation with BFS

- [ ] 11. Implement BFSPathfinder for dynamic map navigation
  - [ ] 11.1 Create BFSPathfinder class with graph storage
    - Add Dictionary&lt;int, List&lt;int&gt;&gt; for bidirectional graph edges
    - Add Dictionary&lt;int, string&gt; for mapId → mapName mapping
    - Add Dictionary&lt;string, int&gt; for mapName → mapId mapping
    - Add isGraphBuilt flag and graphBuildTime timestamp
    - _Requirements: 4.1, 4.2_
    - _Design Reference: Section "BFSPathfinder for Dynamic Map Navigation"_
  
  - [ ] 11.2 Implement map graph construction from game data
    - Create FindMapGateways() searching for MapGateway objects in game
    - Try GameManager.listMapGateways field (if exists)
    - Try FindObjectsOfType&lt;MapGateway&gt; using Il2CppAPI
    - Try scanning ChangeMap objects as fallback
    - Extract fromMapId, toMapId, fromMapName, toMapName from each gateway
    - _Requirements: 4.1_
  
  - [ ] 11.3 Build bidirectional graph from gateway data
    - Iterate through all MapGateway objects found
    - Add bidirectional edges (fromMapId ↔ toMapId) to graph dictionary
    - Populate mapIdToName and mapNameToId mappings
    - Set isGraphBuilt = true and update graphBuildTime
    - Log graph statistics (map count, gateway count)
    - _Requirements: 4.2, 4.7_
  
  - [ ] 11.4 Implement BFS pathfinding algorithm
    - Create FindPathBFS() taking startId and targetId parameters
    - Initialize Queue&lt;int&gt;, visited HashSet&lt;int&gt;, parent Dictionary&lt;int, int&gt;
    - Implement BFS loop: dequeue, check target, enqueue neighbors
    - Add safety check: abort if parent.Count > MaxPathLength (20)
    - Return null if no path found after exhausting queue
    - _Requirements: 4.3, 4.8_
  
  - [ ] 11.5 Implement path reconstruction and conversion
    - Create ReconstructPath() building path from parent dictionary
    - Traverse parent links from target back to start, then reverse
    - Convert path from mapId list to mapName list using mappings
    - Create FindPath() public method taking string map names
    - Log final path as "Map1 → Map2 → Map3" for debugging
    - _Requirements: 4.4_
  
  - [ ] 11.6 Implement fallback and cache invalidation
    - Add InvalidateCache() method clearing graph and resetting build flag
    - Add IsGraphReady() checking isGraphBuilt and graph.Count > 0
    - Rebuild graph every 300 seconds in Update() to catch game updates
    - Return null and log warning if graph not built when FindPath() called
    - _Requirements: 4.6, 4.7_
  
  - [ ]* 11.7 Write unit tests for BFSPathfinder
    - Create mock graph data (10 maps, 15 portals)
    - Test BFS finds shortest path between any two maps
    - Test BFS returns null for disconnected maps
    - Test path length limit prevents infinite loops
    - **Property 3: BFS always finds shortest path if one exists**
    - **Validates: Requirements 4.3, 4.4**
  
  - [ ]* 11.8 Validate BFS pathfinding performance
    - Build realistic graph (50 maps, 100 portals)
    - Measure worst-case pathfinding time (opposite corners of graph)
    - Verify pathfinding completes in <100ms
    - Test graph construction time from game data
    - _Requirements: 4.8_

- [ ] 12. Integrate BFS pathfinding with AutoBossRunner
  - [ ] 12.1 Add BFSPathfinder instance to AutoBossRunner
    - Create pathfinder field in AutoBossRunner class
    - Initialize pathfinder in Start() method
    - Call pathfinder.Update() in runner Update() for graph rebuilds
    - _Requirements: 4.3_
  
  - [ ] 12.2 Implement TeleportToMap command using BFS
    - Create TeleportToMap(string targetMapName) method in AutoBossRunner
    - Get current map name using GameAPI.GetCurrentMapName()
    - Call pathfinder.FindPath(currentMap, targetMap)
    - Execute path by sequentially using portals (iterate through path list)
    - Fall back to hard-coded PortalChainMaps if BFS returns null
    - _Requirements: 4.5, 4.6, 13.8_
  
  - [ ] 12.3 Add BFS pathfinding to MoveToBoss state
    - Replace hard-coded portal chains with dynamic BFS pathfinding
    - Compute path from current map to boss map on state entry
    - Execute portal sequence from computed path
    - Log each portal transition for debugging
    - _Requirements: 4.3, 4.4_
  
  - [ ]* 12.4 Test BFS integration end-to-end
    - Start bot in Map A with boss target in Map E
    - Verify BFS computes path A→B→C→D→E
    - Verify bot executes portal sequence correctly
    - Test fallback when BFS fails (disconnected graph)
    - Measure total navigation time vs hard-coded chains

### Phase 4: Enhanced Features

- [ ] 13. Implement FarmLoop state machine for town farming
  - [ ] 13.1 Create FarmLoopStateMachine MonoBehaviour component
    - Define FarmLoopState enum (FarmIdle, FarmFindMobs, FarmAttack, etc.)
    - Add state tracking fields (currentState, currentZoneIndex, zonesCleared)
    - Add configuration flags (enableAutoZoneSwitch, enableAutoReward, enableAutoSatellite)
    - Add timing fields (lastZoneSwitchTime)
    - _Requirements: 11.1, 11.5_
    - _Design Reference: Section "FarmLoop State Machine"_
  
  - [ ] 13.2 Implement farm loop Update() state dispatcher
    - Create switch statement routing to state-specific Update methods
    - Implement UpdateFarmFindMobs() scanning for alive mobs
    - Implement UpdateFarmAttack() delegating to main combat logic
    - Implement UpdateFarmZoneEmpty() checking auto-switch config
    - Add TransitionTo() method logging state changes
    - _Requirements: 11.5, 11.7_
  
  - [ ] 13.3 Implement auto zone switching logic
    - Create UpdateFarmSwitchZone() calling ZoneFunc.select()
    - Increment currentZoneIndex and zonesCleared counters
    - Update lastZoneSwitchTime timestamp
    - Log zone switch event for analytics
    - Transition to FarmRewardCheck after switching
    - _Requirements: 11.1, 11.2_
  
  - [ ] 13.4 Implement auto reward claiming logic
    - Create UpdateFarmRewardCheck() detecting reward popup UI
    - Create UpdateFarmClaimReward() clicking reward button
    - Use GameAPI reflection to find reward UI elements
    - Handle case where no reward is available (skip gracefully)
    - Transition to FarmSatelliteCheck after claiming
    - _Requirements: 11.3_
  
  - [ ] 13.5 Implement auto satellite activation logic
    - Create UpdateFarmSatelliteCheck() checking satellite item availability
    - Create UpdateFarmActivateSatellite() using satellite item for exp boost
    - Use GameAPI reflection to find and use satellite items
    - Transition back to FarmFindMobs to continue loop
    - _Requirements: 11.4_
  
  - [ ] 13.6 Integrate FarmLoop with main AutoBossRunner
    - Add FarmLoopStateMachine instance to AutoBossRunner
    - Enable farm loop when AutoBossRunner state is Idle
    - Pause farm loop when AutoBossRunner transitions to MoveToBoss
    - Resume farm loop after ReturnHome completes
    - _Requirements: 11.7_
  
  - [ ] 13.7 Add farm loop configuration to BotProfile
    - Add EnableAutoZoneSwitch boolean property
    - Add EnableAutoReward boolean property
    - Add EnableAutoSatellite boolean property
    - Support hot-reload of farm loop settings via CONFIG_UPDATE
    - _Requirements: 6.6, 11.6_
  
  - [ ]* 13.8 Write integration tests for FarmLoop
    - Mock game state with mobs in zone
    - Verify auto zone switch triggers when mob count = 0
    - Verify reward claiming executes when popup detected
    - Test farm loop cycles through all features correctly
    - _Requirements: 11.1, 11.2, 11.3, 11.4_
  
  - [ ]* 13.9 Log farm loop analytics
    - Track zones cleared count per session
    - Track rewards claimed count
    - Track satellites activated count
    - Send analytics to Manager via LOG_EVENT messages
    - _Requirements: 11.8_

- [ ] 14. Implement ItemFilter system
  - [ ] 14.1 Create ItemFilterManager class
    - Define ItemFilterMode enum (Disabled, Whitelist, Blacklist)
    - Add HashSet&lt;string&gt; for filter list (case-insensitive)
    - Add configuration flags (alwaysPickGems, alwaysPickQuestItems, minRarityToPickup)
    - Add statistics counters (itemsPickedUp, itemsFiltered)
    - _Requirements: 12.1, 12.2, 12.3_
    - _Design Reference: Section "ItemFilterManager"_
  
  - [ ] 14.2 Implement filter configuration
    - Create Configure() method accepting mode, item list, special rules
    - Clear and repopulate filterList HashSet
    - Set configuration flags from parameters
    - Log filter configuration for debugging
    - _Requirements: 12.3, 12.7_
  
  - [ ] 14.3 Implement ShouldPickup() decision logic
    - Extract item name using GameAPI.GetItemDisplayName()
    - Extract item rarity using GetItemRarity() (game-specific)
    - Check special rules first (gems, quest items) - always pick if enabled
    - Apply rarity filter (skip if rarity < minRarityToPickup)
    - Apply whitelist mode (pick only if in list)
    - Apply blacklist mode (pick unless in list)
    - Return true on error (safe default)
    - _Requirements: 12.1, 12.2, 12.4, 12.6_
  
  - [ ] 14.4 Integrate ItemFilter with loot pickup logic
    - Find AutoPickupLite or equivalent loot system in existing code
    - Call itemFilter.ShouldPickup() before picking each item
    - Skip item if ShouldPickup() returns false
    - Log filtered items count for analytics
    - _Requirements: 12.4_
  
  - [ ] 14.5 Add ItemFilter configuration to BotProfile
    - Add FilterMode property to BotProfile
    - Add ItemFilterList List&lt;string&gt; property
    - Add AlwaysPickGems, AlwaysPickQuestItems boolean properties
    - Add MinRarityToPickup int property (0=all, 1=uncommon+, etc.)
    - Support hot-reload via CONFIG_UPDATE message
    - _Requirements: 12.3, 12.7_
  
  - [ ] 14.6 Implement auto inventory sorting
    - Create SortInventory() method sorting by rarity or type
    - Use GameAPI reflection to access inventory system
    - Call game's native sort function if available
    - Fall back to manual sorting via item swaps
    - _Requirements: 12.5_
  
  - [ ]* 14.7 Write unit tests for ItemFilter
    - Create mock items with different names and rarities
    - Test whitelist mode only picks items in list
    - Test blacklist mode picks all except items in list
    - Test special rules override filters (gems, quest items)
    - Test rarity filter correctly filters low-rarity items
    - **Property 4: Whitelist mode never picks items not in list**
    - **Validates: Requirements 12.1**
  
  - [ ]* 14.8 Log item filter statistics
    - Track itemsPickedUp and itemsFiltered counters
    - Send statistics to Manager periodically
    - Display in analytics dashboard
    - _Requirements: 12.8_

- [ ] 15. Implement HUD overlay for visual debugging
  - [ ] 15.1 Create AutoBossHUD MonoBehaviour component
    - Add isEnabled flag (default false)
    - Add configuration for screen corner position
    - Add hotkey detection for Ctrl+F11 toggle
    - _Requirements: 22.1, 22.4_
  
  - [ ] 15.2 Implement OnGUI rendering
    - Create OnGUI() method rendering overlay using GUI.Box()
    - Display current state, map, zone, boss kills, uptime, connection status
    - Use semi-transparent background (GUI.backgroundColor with alpha)
    - Position in configurable corner (use Screen.width/height)
    - Use color coding (green/yellow/red) for status text
    - _Requirements: 22.2, 22.3, 22.5, 22.7_
  
  - [ ] 15.3 Implement hotkey toggle
    - Check for Ctrl+F11 key combination in Update()
    - Toggle isEnabled flag on keypress
    - Skip OnGUI rendering when isEnabled = false for performance
    - _Requirements: 22.4, 22.8_
  
  - [ ] 15.4 Add HUD configuration to BotProfile
    - Add EnableHUD boolean property
    - Add HUDPosition enum (TopLeft, TopRight, BottomLeft, BottomRight)
    - Support hot-reload of HUD settings
    - _Requirements: 22.5_
  
  - [ ]* 15.5 Test HUD overlay performance
    - Measure frame time with HUD enabled vs disabled
    - Verify no significant performance impact (<1ms per frame)
    - Test HUD updates every frame without lag
    - _Requirements: 22.6, 22.8_

### Phase 5: Analytics and Monitoring

- [ ] 16. Implement AnalyticsEngine for metrics tracking
  - [ ] 16.1 Create AnalyticsEngine service class
    - Create data models for Session, Metrics, BossSighting
    - Add storage path (AppData/AutoBossManager/analytics/)
    - Implement per-instance metrics tracking (boss kills, runtime, deaths, etc.)
    - Implement per-session metrics tracking (start/end time, maps traversed)
    - _Requirements: 10.1, 10.2_
  
  - [ ] 16.2 Implement metrics calculation
    - Calculate aggregate metrics (total boss kills, average kills/hour)
    - Calculate efficiency metrics (avg time to find boss, avg zone switches)
    - Calculate success rate percentage (boss kills / boss attempts)
    - Update metrics in real-time as events arrive from SocketServer
    - _Requirements: 10.3, 10.5_
  
  - [ ] 16.3 Implement analytics dashboard view
    - Create AnalyticsView.xaml with charts and summary panels
    - Add line chart for boss kills over time (use OxyPlot or LiveCharts)
    - Add bar chart for bot efficiency comparison (boss/hour per bot)
    - Display aggregate metrics (total kills, total uptime, average efficiency)
    - _Requirements: 10.4_
  
  - [ ] 16.4 Implement data export
    - Create ExportToCSV() method exporting metrics to CSV file
    - Include all tracked fields (timestamp, bot, boss, duration, success)
    - Implement daily/weekly summary report generation
    - Store reports in analytics/reports/ directory
    - _Requirements: 10.6, 10.8_
  
  - [ ] 16.5 Implement metrics persistence
    - Save metrics to JSON files daily (metrics_YYYY-MM-DD.json)
    - Save session data on session end
    - Implement historical data loading for trend analysis
    - _Requirements: 10.7_
  
  - [ ]* 16.6 Write unit tests for AnalyticsEngine
    - Test metrics calculation with mock data
    - Test aggregate calculations are correct
    - Test efficiency metrics are computed accurately
    - Test CSV export format is valid

- [ ] 17. Implement centralized logging system
  - [ ] 17.1 Create LogAggregator service class
    - Create log database storage (SQLite or JSON files)
    - Define LogEntry model (timestamp, bot, level, message)
    - Implement AddLogEntry() method
    - Subscribe to SocketServer.OnLogEvent for client logs
    - _Requirements: 9.1, 9.2_
  
  - [ ] 17.2 Implement log viewer UI
    - Create LogView.xaml with DataGrid for log entries
    - Add filtering controls (bot, log level, time range, keyword search)
    - Implement real-time log updates (new logs appear automatically)
    - Add export button to save filtered logs to text file
    - _Requirements: 9.3, 9.4_
  
  - [ ] 17.3 Implement log file rotation
    - Monitor log file size, rotate when exceeds 50MB
    - Create new log file with timestamp suffix
    - Retain logs for 7 days, delete older files automatically
    - Warn user when disk space is low
    - _Requirements: 9.5, 9.6, 9.7_
  
  - [ ]* 17.4 Test log aggregation
    - Connect multiple clients sending log events
    - Verify all logs appear in centralized viewer
    - Test filtering by bot, level, keyword
    - Test log rotation creates new files correctly
    - _Requirements: 9.1, 9.2, 9.3_

- [ ] 18. Implement notification system
  - [ ] 18.1 Create NotificationManager service class
    - Define notification types (BossFound, BossKilled, BotError, etc.)
    - Add notification channels (in-app toast, system tray)
    - Implement rate limiting (max 10 notifications per minute per bot)
    - _Requirements: 19.1, 19.2, 19.7_
  
  - [ ] 18.2 Implement in-app toast notifications
    - Create toast UI overlay in MainWindow
    - Display notification with icon, message, timestamp
    - Auto-dismiss after 5 seconds with fade animation
    - Click to view details (navigate to bot in dashboard)
    - _Requirements: 19.2_
  
  - [ ] 18.3 Implement system tray notifications
    - Use NotifyIcon for Windows system tray integration
    - Display balloon tip for high-priority notifications
    - Show Manager icon in system tray even when minimized
    - _Requirements: 19.2_
  
  - [ ] 18.4 Add notification configuration per bot
    - Add NotificationSettings to BotProfile
    - Allow enabling/disabling each notification type per bot
    - Support sound alerts for high-priority notifications (configurable)
    - _Requirements: 19.3, 19.8_
  
  - [ ] 18.5 Implement notification history
    - Maintain list of last 50 notifications with timestamps
    - Create NotificationHistoryView showing all recent notifications
    - Allow filtering by bot or notification type
    - _Requirements: 19.6_
  
  - [ ]* 18.6 Test notification system
    - Trigger various notification types (boss found, error, etc.)
    - Verify rate limiting prevents spam
    - Verify sound alerts play correctly
    - Test notification history retention

### Phase 6: Advanced Configuration and Safety

- [ ] 19. Implement strategy preset system
  - [ ] 19.1 Define preset configurations
    - Create StrategyPresetManager class
    - Define Aggressive preset (fast movement, low dwell times, 15 max attempts)
    - Define Balanced preset (medium settings, standard movement)
    - Define Safe preset (slow movement, high dwell times, 20 max attempts)
    - Store presets as JSON templates in application resources
    - _Requirements: 7.1, 7.2, 7.3, 7.4_
  
  - [ ] 19.2 Implement preset application to profile
    - Create ApplyPreset() method copying preset values to BotProfile
    - Allow user to modify individual parameters after applying preset
    - Support custom presets created by users
    - _Requirements: 7.5, 7.6, 7.7_
  
  - [ ] 19.3 Add behavior randomization settings to presets
    - Add randomization intensity (low, medium, high) to each preset
    - Include delay variance percentage
    - Include action timing variance settings
    - _Requirements: 7.8_
  
  - [ ]* 19.4 Test preset application
    - Apply Aggressive preset, verify fast settings applied
    - Apply Safe preset, verify conservative settings applied
    - Modify preset values, verify custom values preserved

- [ ] 20. Implement behavior randomization
  - [ ] 20.1 Create BehaviorRandomizer utility class
    - Add RandomizeDwellTime() returning value ±10% around base
    - Add RandomizeMovementDelay() returning value ±20% around base
    - Add RandomizeMicroPause() returning 50-200ms pause duration
    - Add RandomizeScanDirection() returning forward/reverse (50/50)
    - _Requirements: 16.1, 16.2, 16.3, 16.4_
  
  - [ ] 20.2 Integrate randomization into AutoBossRunner
    - Apply randomized dwell times in zone scanning loops
    - Apply randomized movement delays in navigation
    - Insert micro-pauses between action sequences
    - Randomize scan direction on each zone scan
    - _Requirements: 16.1, 16.2, 16.3, 16.4_
  
  - [ ] 20.3 Add randomization configuration
    - Add randomization intensity setting to BotProfile
    - Support enabling/disabling randomization per bot
    - Log randomization events in debug mode for analysis
    - _Requirements: 16.5, 16.6_
  
  - [ ] 20.4 Implement click position randomization
    - Randomize click positions within UI element bounding boxes
    - Avoid always clicking exact center of buttons
    - Add small random movement offsets during patrol
    - _Requirements: 16.7, 16.8_
  
  - [ ]* 20.5 Test randomization effectiveness
    - Record bot behavior over 100 cycles
    - Verify dwell times vary within ±10% range
    - Verify scan direction is roughly 50/50 split
    - Analyze for human-like variability patterns

- [ ] 21. Implement emergency stop and safety features
  - [ ] 21.1 Implement emergency stop functionality
    - Add EmergencyStopAll button to dashboard toolbar (prominent red)
    - Send STOP commands to all connected clients within 500ms
    - Force clients to transition to Idle state immediately
    - Log emergency stop event with timestamp and reason
    - _Requirements: 17.1, 17.2, 17.3_
  
  - [ ] 21.2 Implement client-side panic hotkey
    - Detect Ctrl+Alt+F12 key combination in client Update()
    - Immediately stop bot and transition to Idle state
    - Send panic notification to Manager with reason
    - Can be pressed from game window without Manager focus
    - _Requirements: 17.4_
  
  - [ ] 21.3 Implement automatic safety pauses
    - Detect repeated failures (5 consecutive boss engagement failures)
    - Automatically stop bot and send notification to Manager
    - Log reason for safety pause in detail
    - Require manual resume after safety pause
    - _Requirements: 17.5, 17.6_
  
  - [ ] 21.4 Implement global pause mode
    - Add Global Pause toggle in Manager preventing all bots from farming
    - Pause all active bots when global pause enabled
    - Resume all bots when global pause disabled
    - Display global pause status prominently in UI
    - _Requirements: 17.7_
  
  - [ ] 21.5 Implement graceful shutdown
    - Flush all logs to disk before shutdown
    - Save persisted state before closing
    - Send shutdown notification to Manager
    - Wait for pending operations to complete (max 5 seconds)
    - _Requirements: 17.8_
  
  - [ ]* 21.6 Test emergency stop
    - Trigger emergency stop with 5 active bots
    - Verify all bots stop within 500ms
    - Verify state is saved correctly
    - Test panic hotkey from game window

- [ ] 22. Implement bot state persistence and recovery
  - [ ] 22.1 Create PersistedState model
    - Define fields: SavedAt, CurrentState, CurrentMap, CurrentZone, BossKills, SessionStartTime
    - Implement JSON serialization
    - _Requirements: 15.2_
    - _Design Reference: Section "Persisted State (For Recovery)"_
  
  - [ ] 22.2 Implement state persistence
    - Save state to BepInEx/config/AutoBossGrabber/{account}_state.json
    - Execute save every 30 seconds during active operation
    - Include all critical state fields in save
    - Handle file I/O errors gracefully (log warning, continue)
    - _Requirements: 15.1, 15.7_
  
  - [ ] 22.3 Implement state loading on startup
    - Load persisted state from disk in client Start() method
    - Check SavedAt timestamp to determine if state is fresh (<5 min)
    - Resume from saved state if fresh (restore map, zone, boss kills)
    - Start fresh from Idle state if stale (>5 min old)
    - _Requirements: 15.3, 15.4, 15.5_
  
  - [ ] 22.4 Implement state cleanup
    - Delete persisted state files older than 24 hours
    - Run cleanup on Manager startup and daily
    - _Requirements: 15.6_
  
  - [ ] 22.5 Integrate with auto-restart
    - When Manager detects crash, restart game process
    - AutoBossClient loads persisted state on startup
    - Resume farming from last saved state automatically
    - _Requirements: 15.7_
  
  - [ ]* 22.6 Test state persistence and recovery
    - Save state during active farming
    - Kill game process (simulate crash)
    - Restart game, verify state loads correctly
    - Verify bot resumes farming from saved state
    - Test stale state handling (>5 min old)
    - **Property 5: State loaded after crash matches last saved state**
    - **Validates: Requirements 15.3, 15.4**

- [ ] 23. Implement captcha integration enhancement
  - [ ] 23.1 Integrate existing CaptchaManager with SocketClient
    - Hook CaptchaManager detection events
    - Send CAPTCHA_DETECTED message to Manager with screenshot
    - Include timestamp and bot instance ID in notification
    - _Requirements: 21.1, 21.2_
  
  - [ ] 23.2 Implement automatic captcha solving with CNN
    - Preserve existing CNN model and solving logic
    - Attempt automatic solve when captcha detected
    - Send CAPTCHA_SOLVED or CAPTCHA_FAILED message to Manager
    - Pause bot if automatic solving fails
    - _Requirements: 21.3, 21.4_
  
  - [ ] 23.3 Implement Manager-side captcha queue
    - Create CaptchaQueueView displaying pending captchas
    - Show captcha screenshot with bot instance name
    - Add Retry button to trigger another solve attempt
    - Prioritize captchas by bot priority setting
    - _Requirements: 21.5, 21.6, 21.8_
  
  - [ ] 23.4 Track captcha statistics
    - Count total captchas encountered per bot
    - Calculate auto-solve success rate percentage
    - Track average solve time
    - Display statistics in analytics dashboard
    - _Requirements: 21.7_
  
  - [ ]* 23.5 Test captcha integration
    - Simulate captcha popup in game
    - Verify CAPTCHA_DETECTED message sent to Manager
    - Verify automatic solving attempts with CNN
    - Test manual retry from Manager

### Phase 7: Integration and Testing

- [ ] 24. Implement multi-instance launch and management
  - [ ] 24.1 Implement game process launching
    - Create ProcessLauncher utility class
    - Implement LaunchGameProcess() with executable path and profile
    - Pass profile-specific arguments or config files to game process
    - Track launched process IDs for each bot instance
    - _Requirements: 5.3_
  
  - [ ] 24.2 Implement auto-restart on crash
    - Detect process exit using Process.Exited event
    - Check AutoRestartOnCrash flag in BotProfile
    - Track restart attempt count per instance
    - Stop restarting after MaxRestartAttempts exceeded
    - Notify user via dashboard when max attempts reached
    - _Requirements: 5.7, 15.7_
  
  - [ ] 24.3 Implement duplicate instance prevention
    - Track active instances by AccountName
    - Prevent launching duplicate instance for same account
    - Display warning message if duplicate launch attempted
    - _Requirements: 5.8_
  
  - [ ] 24.4 Add bulk operations support
    - Implement Start All button launching all configured profiles
    - Implement Stop All button stopping all active instances
    - Implement Pause All button pausing all active instances
    - Add confirmation dialog for bulk destructive operations
    - _Requirements: 5.6_
  
  - [ ]* 24.5 Test multi-instance launch
    - Configure 5 bot profiles with different accounts
    - Launch all 5 instances simultaneously
    - Verify all connect to Manager successfully
    - Verify each instance operates independently
    - Test bulk Stop All command

- [ ] 25. Implement configuration UI and editor
  - [ ] 25.1 Create ConfigEditorWindow.xaml
    - Add tabs for different config sections (Boss, Behavior, Skills, Farm, Filter)
    - Add form fields for all BotProfile properties with labels
    - Add validation indicators (green checkmark / red X)
    - Add tooltips with recommended values and descriptions
    - _Requirements: 6.4, 14.7, 20.3_
  
  - [ ] 25.2 Implement real-time validation in editor
    - Validate fields as user types (debounced)
    - Display validation errors below each field
    - Disable Save button when validation fails
    - Show recommended value ranges in tooltips
    - _Requirements: 14.6, 14.7_
  
  - [ ] 25.3 Add strategy preset selector
    - Add dropdown to select preset (Aggressive, Balanced, Safe, Custom)
    - Apply preset values when selected
    - Switch to Custom when user modifies any field
    - _Requirements: 7.6, 7.7_
  
  - [ ] 25.4 Implement boss skill configuration UI
    - Add DataGrid for skill triggers
    - Columns: HP Threshold, Skill Key (1-4), Spam Count
    - Add/Remove buttons for skill trigger entries
    - Validate HP threshold and skill key ranges
    - _Requirements: 24.3_
  
  - [ ] 25.5 Implement item filter configuration UI
    - Add filter mode selector (Disabled, Whitelist, Blacklist)
    - Add TextBox with multi-line input for item names
    - Add checkboxes for special rules (gems, quest items)
    - Add slider for minimum rarity (0-3)
    - _Requirements: 12.7_
  
  - [ ]* 25.6 Test configuration editor
    - Enter invalid values, verify validation errors
    - Apply strategy preset, verify values populate
    - Save and load profile, verify round-trip
    - Test hot-reload sends CONFIG_UPDATE message

- [ ] 26. Implement UI polish and user experience
  - [ ] 26.1 Implement keyboard shortcuts
    - Ctrl+N: Create new profile
    - Ctrl+S: Save current profile
    - F5: Refresh dashboard
    - Space: Start/Stop selected bot
    - Implement shortcut handling in MainWindow code-behind
    - _Requirements: 20.2_
  
  - [ ] 26.2 Implement dark/light theme support
    - Create theme resource dictionaries
    - Add theme selector in settings menu
    - Persist theme preference to user settings
    - Apply theme dynamically without restart
    - _Requirements: 20.5_
  
  - [ ] 26.3 Implement loading indicators
    - Show spinner when launching bots
    - Show progress bar when generating reports
    - Show indeterminate progress during long operations
    - _Requirements: 20.6_
  
  - [ ] 26.4 Create first-time user tutorial
    - Implement wizard for creating first bot profile
    - Guide through essential settings (account, boss, maps)
    - Offer to launch bot after wizard completion
    - Show "Getting Started" guide on first run
    - _Requirements: 20.4_
  
  - [ ] 26.5 Optimize UI performance
    - Enable virtualization for DataGrid with many rows
    - Throttle status updates to max 1 per second
    - Implement progressive loading for large log files
    - Test UI responsiveness with 20+ connected clients
    - _Requirements: 20.8_
  
  - [ ]* 26.6 Test UI responsiveness
    - Connect 20 clients sending updates every second
    - Verify UI update time stays <100ms
    - Verify no freezing or lag during updates
    - Measure memory usage over 1 hour period
    - _Requirements: 20.8_

- [ ] 27. Integration testing and validation
  - [ ]* 27.1 End-to-end boss hunting test
    - Configure bot profile with target boss
    - Start bot from Manager
    - Verify bot connects to Manager
    - Verify bot navigates to boss map using BFS
    - Verify boss detection and combat engagement
    - Verify loot pickup and return to town
    - Verify boss kill logged to analytics
    - **Property 6: Boss hunting cycle completes without manual intervention**
    - **Validates: Requirements 1-25 (integration)**
  
  - [ ]* 27.2 Multi-instance stress test
    - Launch 10+ instances simultaneously on 16GB RAM machine
    - Monitor RAM usage per instance (target: <800MB)
    - Monitor CPU usage per instance (target: <8% during combat)
    - Monitor total system resource usage
    - Run for 1 hour, verify stability (no crashes)
    - _Requirements: 2.7, 5.1_
  
  - [ ]* 27.3 IPC reliability test
    - Send 1000 commands to client, verify all executed
    - Simulate network interruption, verify reconnection
    - Measure command latency (target: <50ms)
    - Verify no command loss during reconnection
    - _Requirements: 3.7, 3.8_
  
  - [ ]* 27.4 Configuration hot-reload test
    - Change boss target in Manager
    - Verify client receives CONFIG_UPDATE within 1 second
    - Verify bot switches to new boss without restart
    - Test multiple hot-reload cycles
    - _Requirements: 6.1, 6.2_
  
  - [ ]* 27.5 Crash recovery test
    - Save state during active farming
    - Force-kill game process
    - Verify Manager detects disconnect
    - Verify auto-restart launches game
    - Verify bot resumes from saved state
    - _Requirements: 15.7, 5.7_
  
  - [ ]* 27.6 Performance benchmark validation
    - Measure RAM per instance: target <800MB
    - Measure CPU per instance (idle): target <3%
    - Measure CPU per instance (combat): target <8%
    - Measure max instances on 16GB RAM: target 12-14
    - Measure IPC command latency: target <50ms
    - Measure BFS pathfinding time: target <100ms
    - Measure dashboard refresh rate: 1 second
    - Measure dashboard UI lag: target <100ms
    - _Design Reference: Section "Performance Benchmarks"_

- [ ] 28. Final checkpoint - Full integration validation
  - Ensure all tests pass, ask the user if questions arise.
  - Verify 10+ instances can run simultaneously without issues
  - Verify all commands execute correctly via Manager
  - Verify hot-reload configuration updates work
  - Verify analytics and logging capture all events
  - Verify crash recovery and auto-restart function correctly
  - Measure and document final performance metrics

---

## Notes

**Task Execution Guidelines:**
- Tasks marked with `*` are optional and can be skipped for faster MVP delivery
- Test-related sub-tasks include: unit tests, property tests, integration tests, performance validation
- Core implementation tasks (without `*`) must be implemented
- Property-based tests validate universal correctness properties from design
- Each task references specific requirements for traceability

**Critical Path (Must Complete for Phase 1):**
1. GameOptimizer (Task 3) - Enables 10+ instances
2. Socket IPC (Task 4, 7) - Enables remote control
3. Manager Core (Task 6, 8, 9) - Provides control interface
4. BFS Pathfinding (Task 11, 12) - Eliminates manual config

**Dependencies:**
- Tasks 4 and 7 can be developed in parallel (client and server sides of IPC)
- Task 11 (BFS) depends on Task 1 (GameAPI wrapper)
- Tasks 13-15 (enhanced features) depend on Task 4 (SocketClient)
- Tasks 16-18 (analytics/monitoring) depend on Task 7 (SocketServer)
- Task 24 (multi-instance) depends on Tasks 6-9 (Manager core)
- Task 27 (integration tests) depends on all previous tasks

**Testing Strategy:**
- Unit tests verify individual component correctness
- Property-based tests validate universal properties (e.g., BFS shortest path, state persistence)
- Integration tests verify component interactions
- Performance tests validate resource usage targets
- End-to-end tests validate complete workflows

**Estimated Timeline:**
- Phase 1 (Core Infrastructure): Weeks 1-3
- Phase 2 (Manager Foundation): Weeks 3-4
- Phase 3 (Smart Navigation): Week 4
- Phase 4 (Enhanced Features): Weeks 5-6
- Phase 5 (Analytics/Monitoring): Week 6
- Phase 6 (Configuration/Safety): Week 7
- Phase 7 (Integration/Testing): Week 8

**Performance Targets:**
- RAM per instance: <800MB (30% reduction from 1200MB baseline)
- CPU per instance (idle): <3%
- CPU per instance (combat): <8%
- Max instances (16GB RAM): 12-14
- IPC command latency: <50ms
- BFS pathfinding time: <100ms
- Dashboard refresh rate: 1 second
- Dashboard UI lag: <100ms

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1"] },
    { "id": 1, "tasks": ["2.1", "2.3", "2.4"] },
    { "id": 2, "tasks": ["2.2", "3.1", "6.1"] },
    { "id": 3, "tasks": ["3.2", "3.3", "3.4", "6.2"] },
    { "id": 4, "tasks": ["3.5", "3.6", "4.1", "6.3", "7.1"] },
    { "id": 5, "tasks": ["4.2", "4.3", "6.4", "7.2", "7.3", "8.1"] },
    { "id": 6, "tasks": ["4.4", "4.5", "4.6", "4.7", "4.8", "7.4", "7.5", "7.6", "8.2"] },
    { "id": 7, "tasks": ["4.9", "4.10", "7.7", "7.8", "8.3", "8.4", "9.1", "11.1"] },
    { "id": 8, "tasks": ["8.5", "9.2", "9.3", "11.2", "11.3"] },
    { "id": 9, "tasks": ["9.4", "9.5", "11.4", "11.5", "11.6"] },
    { "id": 10, "tasks": ["11.7", "11.8", "12.1"] },
    { "id": 11, "tasks": ["12.2", "12.3", "13.1", "14.1", "15.1"] },
    { "id": 12, "tasks": ["12.4", "13.2", "13.3", "14.2", "15.2"] },
    { "id": 13, "tasks": ["13.4", "13.5", "13.6", "14.3", "14.4", "15.3"] },
    { "id": 14, "tasks": ["13.7", "13.8", "13.9", "14.5", "14.6", "15.4", "16.1"] },
    { "id": 15, "tasks": ["14.7", "14.8", "15.5", "16.2", "17.1"] },
    { "id": 16, "tasks": ["16.3", "16.4", "17.2", "17.3", "18.1", "19.1"] },
    { "id": 17, "tasks": ["16.5", "16.6", "17.4", "18.2", "18.3", "19.2"] },
    { "id": 18, "tasks": ["18.4", "18.5", "19.3", "19.4", "20.1"] },
    { "id": 19, "tasks": ["18.6", "20.2", "20.3", "21.1", "22.1"] },
    { "id": 20, "tasks": ["20.4", "20.5", "21.2", "21.3", "22.2"] },
    { "id": 21, "tasks": ["21.4", "21.5", "22.3", "22.4", "23.1"] },
    { "id": 22, "tasks": ["21.6", "22.5", "22.6", "23.2", "23.3"] },
    { "id": 23, "tasks": ["23.4", "23.5", "24.1", "25.1"] },
    { "id": 24, "tasks": ["24.2", "24.3", "25.2", "25.3"] },
    { "id": 25, "tasks": ["24.4", "24.5", "25.4", "25.5", "26.1"] },
    { "id": 26, "tasks": ["25.6", "26.2", "26.3", "26.4"] },
    { "id": 27, "tasks": ["26.5", "26.6"] },
    { "id": 28, "tasks": ["27.1", "27.2", "27.3"] },
    { "id": 29, "tasks": ["27.4", "27.5", "27.6"] }
  ]
}
```
