# Requirements Document

## Introduction

This document specifies the requirements for transforming AutoBossGrabber from a standalone BepInEx plugin into a centralized multi-account bot management system. The enhancement draws inspiration from Tool_Up_Level_V111's client-server architecture and memory optimization techniques while maintaining AutoBossGrabber's superior performance optimizations and robust state machine design.

The system will consist of two main components:
1. **AutoBossManager** - A centralized desktop application for managing multiple bot instances
2. **AutoBossClient** - An enhanced BepInEx plugin with IPC capabilities for remote control

**Key Design Principles:**
- Prioritize features that enable running 10+ instances on one machine (memory/CPU optimization)
- Leverage AutoBossGrabber's existing strengths (boss detection, skill manager, captcha AI)
- Learn from V111's proven architecture (socket IPC, BFS pathfinding, auto farm loop)
- Keep scope focused on core multi-instance management for Phase 1

## Glossary

- **AutoBossManager**: The centralized desktop application (similar to QLTK.exe) that controls and monitors multiple game client instances
- **AutoBossClient**: The BepInEx plugin running inside each game instance (enhanced version of current AutoBossGrabber.dll)
- **Bot_Instance**: A single game client process with AutoBossClient plugin loaded
- **IPC_Channel**: Inter-Process Communication channel (TCP Socket or Named Pipes) connecting Client to Manager
- **Bot_Profile**: Configuration set for a specific game account including credentials, schedule, and strategy settings
- **Command_Protocol**: Structured message format for Manager-Client communication
- **Heartbeat**: Periodic status message sent from Client to Manager to indicate alive status
- **Hot_Reload**: The ability to update bot configuration without restarting the game client
- **Session**: A continuous period during which a Bot_Instance is actively running
- **Strategy_Preset**: Pre-configured bot behavior template (aggressive, safe, balanced, etc.)
- **Dashboard**: Real-time monitoring interface showing status of all Bot_Instances
- **Game_Optimizer**: Component that reduces memory and CPU usage per instance
- **Main_Thread_Queue**: ConcurrentQueue pattern for safely executing commands on Unity main thread
- **BFS_Pathfinder**: Breadth-first search algorithm for finding shortest map path
- **Map_Graph**: Representation of all maps and their portal connections
- **Farm_Loop**: State machine for town farming (auto zone switch, auto reward, auto satellite)
- **Item_Filter**: System for automatically picking up valuable items and ignoring junk

---

## Requirements

### Requirement 1: Manager Application Architecture

**User Story:** As a bot operator, I want a centralized desktop application to manage multiple game instances, so that I can control all my bots from one place instead of managing each game window separately.

#### Acceptance Criteria

1. THE AutoBossManager SHALL provide a desktop GUI application built with WPF or Windows Forms
2. WHEN the Manager starts, THE AutoBossManager SHALL create an IPC_Channel server listening for Client connections on configurable port (default 28081)
3. THE AutoBossManager SHALL maintain a registry of connected Bot_Instances with their connection status
4. THE AutoBossManager SHALL persist Bot_Profile configurations to local storage using JSON files
5. WHEN a Bot_Instance connects, THE AutoBossManager SHALL authenticate the connection using a shared secret key
6. THE AutoBossManager SHALL support running on Windows 10 and Windows 11 operating systems
7. THE AutoBossManager SHALL continue running even if all game clients are closed
8. WHEN the Manager exits, THE AutoBossManager SHALL send shutdown commands to all connected Bot_Instances

### Requirement 2: Game Optimizer for Multi-Instance Support (CRITICAL)

**User Story:** As a bot operator, I want each game instance to use minimal RAM and CPU, so that I can run 10+ instances simultaneously on a single machine.

#### Acceptance Criteria

1. THE Game_Optimizer SHALL invoke Windows API EmptyWorkingSet to release unused working set memory
2. THE Game_Optimizer SHALL invoke Windows API SetProcessWorkingSetSize with parameters (-1, -1) to minimize memory footprint
3. THE Game_Optimizer SHALL set GCSettings.LatencyMode to SustainedLowLatency for reduced GC pauses
4. THE Game_Optimizer SHALL set GCSettings.LargeObjectHeapCompactionMode to CompactOnce and trigger full GC collection
5. THE Game_Optimizer SHALL use Harmony patches to disable ParallaxBackground rendering by setting enabled = false
6. THE Game_Optimizer SHALL disable visual effects and animations that do not affect gameplay
7. WHEN Game_Optimizer is enabled, THE AutoBossClient SHALL reduce per-instance memory usage by at least 30% compared to unoptimized baseline
8. THE Game_Optimizer SHALL execute memory optimization every 60 seconds during active operation

### Requirement 3: Socket IPC with Thread-Safe Command Execution

**User Story:** As a system architect, I want a robust IPC mechanism that safely executes commands on Unity's main thread, so that remote commands do not cause Unity crashes or race conditions.

#### Acceptance Criteria

1. THE IPC_Channel SHALL use TCP socket connections for Manager-Client communication
2. THE AutoBossClient SHALL implement a Main_Thread_Queue using ConcurrentQueue&lt;Action&gt; for thread-safe command dispatching
3. WHEN Manager sends a command via socket, THE AutoBossClient SHALL enqueue the command action to Main_Thread_Queue
4. WHEN Unity Update() is called, THE AutoBossClient SHALL dequeue and execute all pending actions from Main_Thread_Queue
5. THE Command_Protocol SHALL use line-delimited JSON messages with fields: type, timestamp, payload
6. THE AutoBossClient SHALL send Heartbeat messages to Manager every 3 seconds
7. IF the socket connection drops, THEN THE AutoBossClient SHALL attempt reconnection with exponential backoff (1s, 2s, 4s, 8s, max 30s)
8. THE AutoBossClient SHALL acknowledge command receipt within 500ms by sending an ACK message

### Requirement 4: BFS Pathfinding for Dynamic Map Navigation

**User Story:** As a bot operator, I want the bot to automatically find the shortest path to any map, so that I don't have to manually configure portal chains when targeting new boss locations.

#### Acceptance Criteria

1. THE BFS_Pathfinder SHALL build a Map_Graph by reading all MapGateway objects from the game at startup
2. THE Map_Graph SHALL store bidirectional edges representing portal connections between maps
3. WHEN a teleport command specifies a target map, THE BFS_Pathfinder SHALL compute the shortest path using breadth-first search algorithm
4. THE BFS_Pathfinder SHALL return a path as a list of map IDs from current map to target map
5. THE AutoBossClient SHALL execute the path by sequentially using portals from the computed path
6. IF BFS_Pathfinder cannot find a path, THEN THE AutoBossClient SHALL fall back to hard-coded PortalChainMaps if available
7. THE BFS_Pathfinder SHALL cache the Map_Graph and invalidate cache when game data updates
8. THE BFS_Pathfinder SHALL complete pathfinding computation within 100ms for any map pair

### Requirement 5: Multi-Account Bot Instance Management

**User Story:** As a bot operator, I want to manage multiple game accounts simultaneously from the Manager, so that I can scale my farming operation efficiently.

#### Acceptance Criteria

1. THE AutoBossManager SHALL support managing at least 10 concurrent Bot_Instances
2. THE AutoBossManager SHALL provide UI to add, edit, and delete Bot_Profile configurations
3. WHEN a user clicks "Launch Bot", THE AutoBossManager SHALL start the game executable with the associated Bot_Profile
4. THE AutoBossManager SHALL display a list view showing all Bot_Instances with columns: Account Name, Status, Current Map, Boss Kills, Runtime, Last Update
5. THE AutoBossManager SHALL allow starting, stopping, pausing, and resuming individual Bot_Instances
6. THE AutoBossManager SHALL support bulk operations (start all, stop all, pause all)
7. WHERE a Bot_Instance crashes, THE AutoBossManager SHALL detect the disconnection and optionally restart it based on user preference
8. THE AutoBossManager SHALL prevent launching duplicate instances of the same Bot_Profile

### Requirement 6: Dynamic Configuration System

**User Story:** As a bot operator, I want to change bot settings in real-time without restarting the game, so that I can quickly adapt to game conditions or test different strategies.

#### Acceptance Criteria

1. THE AutoBossClient SHALL support Hot_Reload of configuration changes sent from Manager
2. WHEN Manager sends a CONFIG_UPDATE command, THE AutoBossClient SHALL apply the new settings within 1 second
3. THE Bot_Profile SHALL include configurable parameters: target boss names, home map name, boss map name, attack range, max zone attempts, dwell times, movement speeds
4. THE AutoBossManager SHALL provide a configuration editor UI with validation for all Bot_Profile parameters
5. THE AutoBossManager SHALL support exporting and importing Bot_Profile configurations as JSON files
6. THE AutoBossClient SHALL validate received configuration values and reject invalid configs with an error message
7. WHEN configuration validation fails, THE AutoBossClient SHALL continue using the previous valid configuration
8. THE AutoBossManager SHALL maintain a configuration history with rollback capability

### Requirement 7: Strategy Preset System

**User Story:** As a bot operator, I want pre-configured strategy presets for different scenarios, so that I can quickly switch between aggressive farming, safe farming, and testing modes.

#### Acceptance Criteria

1. THE AutoBossManager SHALL provide at least three Strategy_Preset templates: Aggressive, Balanced, Safe
2. THE Aggressive preset SHALL configure: minimum dwell times (1.0s empty, 2.5s with mobs), fast movement (1.5f threshold), 15 max zone attempts
3. THE Safe preset SHALL configure: maximum dwell times (2.0s empty, 5.0s with mobs), careful movement (0.8f threshold), 20 max zone attempts
4. THE Balanced preset SHALL configure: medium dwell times (1.5s empty, 3.5s with mobs), standard movement (1.2f threshold), 15 max zone attempts
5. THE AutoBossManager SHALL allow users to create custom Strategy_Preset configurations
6. WHEN a user applies a Strategy_Preset to a Bot_Profile, THE AutoBossManager SHALL populate all relevant configuration parameters
7. THE AutoBossManager SHALL allow users to modify individual parameters after applying a preset
8. THE Strategy_Preset SHALL include behavior randomization settings (delay variance, action timing variance)

### Requirement 8: Real-Time Monitoring Dashboard

**User Story:** As a bot operator, I want a real-time dashboard showing the status of all my bots, so that I can quickly identify issues and monitor farming efficiency.

#### Acceptance Criteria

1. THE Dashboard SHALL display real-time status for all Bot_Instances in a grid or list view
2. THE Dashboard SHALL update Bot_Instance status within 1 second of receiving status updates from Clients
3. THE Dashboard SHALL show for each Bot_Instance: current state, current map, current zone, target boss, player position, health/mana percentage, boss kills count, uptime, errors count
4. THE Dashboard SHALL use color coding: green for active farming, yellow for transitioning, red for errors, gray for stopped
5. WHEN a Bot_Instance encounters an error, THE Dashboard SHALL highlight it with a red indicator and show the error message
6. THE Dashboard SHALL provide filtering options: show only active bots, show only bots with errors, show only specific accounts
7. THE Dashboard SHALL support sorting by any column (status, uptime, boss kills, etc.)
8. THE Dashboard SHALL auto-refresh every 1 second without user intervention

### Requirement 9: Centralized Logging System

**User Story:** As a bot operator, I want all bot logs aggregated in the Manager, so that I can debug issues without opening multiple log files.

#### Acceptance Criteria

1. THE AutoBossClient SHALL stream important log events to Manager via IPC_Channel using LOG_EVENT messages
2. THE AutoBossManager SHALL maintain a centralized log database storing all LOG_EVENT messages from all Bot_Instances
3. THE AutoBossManager SHALL provide a log viewer UI with filtering by: Bot_Instance, log level (debug, info, warning, error), time range, keyword search
4. THE AutoBossManager SHALL support exporting filtered logs to text file or CSV format
5. THE AutoBossManager SHALL automatically rotate log files when they exceed 50MB in size
6. THE AutoBossManager SHALL retain logs for at least 7 days before automatic deletion
7. WHERE disk space is low, THE AutoBossManager SHALL warn the user and offer to delete old logs
8. THE AutoBossClient SHALL continue logging to local BepInEx log file even when disconnected from Manager

### Requirement 10: Analytics and Performance Metrics

**User Story:** As a bot operator, I want detailed analytics on bot performance, so that I can optimize my farming strategy and measure efficiency.

#### Acceptance Criteria

1. THE Analytics_Engine SHALL track per-Bot_Instance metrics: total boss kills, total runtime, average boss kill time, death count, captcha solve count, error count
2. THE Analytics_Engine SHALL track per-Session metrics: session start time, session end time, boss kills in session, maps traversed
3. THE Analytics_Engine SHALL calculate aggregate metrics: total boss kills across all bots, average boss kills per hour, total runtime across all bots
4. THE AutoBossManager SHALL provide an analytics dashboard showing graphs for: boss kills over time, bot efficiency (boss/hour) comparison
5. THE AutoBossManager SHALL display efficiency metrics: average time to find boss, average zone switches per boss, success rate percentage
6. THE AutoBossManager SHALL support exporting analytics data to CSV format
7. THE Analytics_Engine SHALL persist metrics to local JSON files for historical analysis
8. THE AutoBossManager SHALL provide daily and weekly summary reports

### Requirement 11: Advanced Farm Loop for Town Farming

**User Story:** As a bot operator, I want the bot to automatically handle town farming tasks like zone switching and reward claiming, so that it can farm efficiently without boss hunting.

#### Acceptance Criteria

1. THE Farm_Loop SHALL support auto zone switching when current zone has zero mobs alive
2. WHEN auto zone switch is enabled and mob count reaches zero, THE Farm_Loop SHALL increment current zone and call ZoneFunc.select()
3. THE Farm_Loop SHALL support auto reward claiming by detecting and clicking reward notification UI
4. THE Farm_Loop SHALL support auto satellite activation for exp boost items
5. THE Farm_Loop SHALL maintain a mini state machine with states: Farming, Switching_Zone, Claiming_Reward, Activating_Satellite
6. THE AutoBossClient SHALL allow enabling/disabling Farm_Loop features independently via configuration
7. THE Farm_Loop SHALL integrate with existing AutoBossState machine as a parallel sub-system
8. THE Farm_Loop SHALL log all automated actions (zone switches, rewards claimed, satellites activated) for analytics

### Requirement 12: Item Filter and Auto Sort

**User Story:** As a bot operator, I want the bot to only pick up valuable items and automatically sort inventory, so that I don't waste time on junk items and inventory management.

#### Acceptance Criteria

1. THE Item_Filter SHALL support whitelist mode: only pick up items in the whitelist
2. THE Item_Filter SHALL support blacklist mode: pick up all items except those in the blacklist
3. THE Bot_Profile SHALL allow configuring item filter rules with item names or name patterns
4. WHEN an item drops and filter is enabled, THE AutoBossClient SHALL check the filter before picking up
5. THE AutoBossClient SHALL support auto inventory sorting by rarity or item type
6. THE Item_Filter SHALL support special rules: always pick up gems, always pick up quest items, always pick up items above certain rarity
7. THE AutoBossManager SHALL provide a UI for configuring item filter rules per Bot_Profile
8. THE AutoBossClient SHALL log filtered items (items skipped) for analysis

### Requirement 13: Remote Control Commands

**User Story:** As a bot operator, I want to send commands to bots remotely from the Manager, so that I can control bot behavior without interacting with the game window.

#### Acceptance Criteria

1. THE AutoBossManager SHALL provide a command panel UI for sending commands to selected Bot_Instances
2. THE Command_Protocol SHALL support commands: START_FARMING, STOP_FARMING, PAUSE, RESUME, RETURN_TO_TOWN, TELEPORT_TO_MAP, SWITCH_ZONE, INVALIDATE_CACHE
3. WHEN Manager sends a command, THE AutoBossClient SHALL execute it and return a success or failure response
4. THE AutoBossClient SHALL queue commands if the bot is in a state that cannot immediately execute the command
5. THE AutoBossManager SHALL display command execution results with success/failure status and error messages
6. THE AutoBossManager SHALL maintain a command history showing last 50 commands sent to each Bot_Instance
7. WHERE a command fails, THE AutoBossClient SHALL provide a descriptive error message explaining why it failed
8. THE TELEPORT_TO_MAP command SHALL use BFS_Pathfinder to automatically navigate to the target map

### Requirement 14: Configuration Validation and Safety Checks

**User Story:** As a bot operator, I want the system to validate my configurations and prevent dangerous settings, so that I don't accidentally misconfigure bots in ways that could lead to poor performance.

#### Acceptance Criteria

1. THE AutoBossManager SHALL validate all Bot_Profile configurations before saving them
2. THE AutoBossManager SHALL enforce minimum safe values: dwell time minimum 0.5s, movement threshold minimum 0.3f, heartbeat interval minimum 1s
3. THE AutoBossManager SHALL enforce maximum safe values: max zone attempts maximum 50, session duration maximum 12 hours
4. THE AutoBossManager SHALL warn users when setting aggressive configurations that may increase detection risk
5. THE AutoBossManager SHALL prevent setting empty or whitespace-only values for required fields (account name, boss names, map names)
6. WHEN validation fails, THE AutoBossManager SHALL display specific error messages identifying which fields are invalid
7. THE AutoBossClient SHALL perform server-side validation of received configurations and reject invalid ones
8. THE AutoBossManager SHALL provide tooltips with recommended value ranges for all configuration fields

### Requirement 15: Bot State Persistence and Recovery

**User Story:** As a bot operator, I want bots to remember their state if they crash or restart, so that they can resume farming without manual intervention.

#### Acceptance Criteria

1. THE AutoBossClient SHALL persist current state to disk every 30 seconds during active operation
2. THE persisted state SHALL include: current AutoBossState, current map, current zone, boss kills count, session start time
3. WHEN AutoBossClient starts, THE AutoBossClient SHALL load persisted state from disk if available
4. IF persisted state is less than 5 minutes old, THEN THE AutoBossClient SHALL resume from the saved state
5. IF persisted state is older than 5 minutes, THEN THE AutoBossClient SHALL start fresh from Idle state
6. THE AutoBossClient SHALL clean up persisted state files older than 24 hours
7. WHEN a Bot_Instance crashes and Manager restarts it, THE AutoBossClient SHALL attempt to resume farming from the last saved state
8. THE persisted state file SHALL be stored in BepInEx\config\AutoBossGrabber\{account_name}_state.json

### Requirement 16: Behavior Randomization for Anti-Detection

**User Story:** As a bot operator, I want the bot to randomize its behavior patterns, so that it appears more human-like and reduces the risk of detection.

#### Acceptance Criteria

1. THE AutoBossClient SHALL randomize dwell times by ±10% around the configured base value
2. THE AutoBossClient SHALL randomize movement delays by ±20% around the configured base value
3. THE AutoBossClient SHALL introduce random micro-pauses (50-200ms) between action sequences
4. THE AutoBossClient SHALL randomly decide whether to scan in forward or reverse zone order (50/50 chance)
5. WHERE randomization is enabled, THE Bot_Profile SHALL allow configuring randomization intensity (low, medium, high)
6. THE AutoBossClient SHALL record randomization events in debug logs for analysis
7. THE AutoBossClient SHALL randomize click positions within UI element bounding boxes (not always center)
8. THE AutoBossClient SHALL vary patrol patterns by introducing small random movement offsets

### Requirement 17: Emergency Stop and Safety Features

**User Story:** As a bot operator, I want emergency stop controls and safety features, so that I can quickly intervene if something goes wrong.

#### Acceptance Criteria

1. THE AutoBossManager SHALL provide a prominent "Emergency Stop All" button that immediately stops all Bot_Instances
2. WHEN emergency stop is activated, THE AutoBossManager SHALL send STOP commands to all connected Bot_Instances within 500ms
3. THE AutoBossClient SHALL respond to STOP commands by immediately transitioning to Idle state and halting all actions
4. THE AutoBossClient SHALL support a panic hotkey (Ctrl+Alt+F12) that immediately stops the bot and can be pressed from the game window
5. THE AutoBossClient SHALL automatically stop if it detects repeated failures (5 consecutive boss engagement failures)
6. WHEN a safety pause is triggered, THE AutoBossClient SHALL send a notification to Manager explaining the reason
7. THE AutoBossManager SHALL support global pause mode that prevents all Bot_Instances from farming until resumed
8. THE AutoBossClient SHALL flush all logs and save state before shutting down on emergency stop

### Requirement 18: Profile Import/Export and Backup

**User Story:** As a bot operator, I want to backup and restore my bot configurations, so that I can recover my setup if I reinstall or migrate to a new machine.

#### Acceptance Criteria

1. THE AutoBossManager SHALL provide an "Export All Profiles" function that saves all Bot_Profile configurations to a single JSON file
2. THE AutoBossManager SHALL provide an "Import Profiles" function that loads Bot_Profile configurations from a JSON file
3. THE export format SHALL include: profile metadata, all configuration parameters, strategy presets, schedules
4. WHEN importing profiles, THE AutoBossManager SHALL validate the import file format and warn about any incompatible settings
5. THE AutoBossManager SHALL support automatic backup of profiles to a configurable directory daily
6. THE AutoBossManager SHALL retain the last 7 automatic backups and delete older backups
7. WHERE a profile with the same name exists during import, THE AutoBossManager SHALL prompt user to overwrite, skip, or rename
8. THE AutoBossManager SHALL validate imported profiles against current schema version and auto-migrate if possible

### Requirement 19: Notification System

**User Story:** As a bot operator, I want to receive notifications about important events, so that I can respond to issues even when not actively monitoring the dashboard.

#### Acceptance Criteria

1. THE AutoBossManager SHALL support notification types: boss found, boss killed, bot error, bot stopped, captcha required
2. THE AutoBossManager SHALL provide notification channels: in-app toast notifications, system tray notifications
3. THE AutoBossManager SHALL allow users to configure which notification types are enabled per Bot_Instance
4. WHEN a boss is found, THE AutoBossClient SHALL send a notification to Manager with boss name, map, and zone
5. WHEN a critical error occurs, THE AutoBossClient SHALL send a high-priority notification to Manager
6. THE AutoBossManager SHALL display a notification history panel showing last 50 notifications with timestamps
7. THE AutoBossManager SHALL support notification rate limiting to prevent spam (max 10 notifications per minute per Bot_Instance)
8. THE AutoBossManager SHALL support sound alerts for high-priority notifications (configurable)

### Requirement 20: UI Polish and User Experience

**User Story:** As a bot operator, I want an intuitive and polished user interface, so that I can efficiently operate the bot without confusion or frustration.

#### Acceptance Criteria

1. THE AutoBossManager SHALL use a modern UI framework with responsive layout supporting window resize
2. THE AutoBossManager SHALL provide keyboard shortcuts for common actions: Ctrl+N new profile, Ctrl+S save config, F5 refresh dashboard, Space start/stop selected bot
3. THE AutoBossManager SHALL display tooltips for all configuration options explaining their purpose and recommended values
4. THE AutoBossManager SHALL provide an interactive tutorial or wizard for first-time users to create their first Bot_Profile
5. THE AutoBossManager SHALL support dark mode and light mode themes with user preference persistence
6. THE AutoBossManager SHALL display loading indicators for long-running operations (launching bots, generating reports)
7. THE AutoBossManager SHALL show validation feedback in real-time as users edit configuration fields (green checkmark for valid, red X for invalid)
8. THE AutoBossManager SHALL maintain reasonable performance with up to 20 Bot_Instances connected (UI updates within 100ms, no freezing)

### Requirement 21: Captcha Integration Enhancement

**User Story:** As a bot operator, I want improved captcha handling integrated with the Manager, so that captcha solving doesn't interrupt bot operations and I can monitor captcha success rates.

#### Acceptance Criteria

1. THE AutoBossClient SHALL detect captcha popups using the existing CaptchaManager component
2. WHEN a captcha is detected, THE AutoBossClient SHALL send a notification to Manager with a screenshot
3. THE AutoBossClient SHALL attempt automatic solving using the existing CNN model
4. IF automatic solving fails, THEN THE AutoBossClient SHALL pause and wait for manual intervention
5. THE AutoBossManager SHALL display a captcha queue showing pending captchas from all Bot_Instances
6. THE AutoBossManager SHALL allow operators to view captcha screenshots and trigger retry
7. THE Analytics_Engine SHALL track captcha statistics: total captchas encountered, auto-solve success rate, average solve time
8. WHERE multiple captchas are pending, THE AutoBossManager SHALL prioritize them by Bot_Instance priority setting

### Requirement 22: HUD Overlay for Visual Debugging

**User Story:** As a bot operator, I want an in-game overlay showing bot status, so that I can monitor the bot without switching to the Manager application.

#### Acceptance Criteria

1. THE AutoBossClient SHALL render a HUD overlay using Unity OnGUI when enabled
2. THE HUD overlay SHALL display: current state, current map, current zone, boss kills count, session uptime, connection status
3. THE HUD overlay SHALL use a semi-transparent background to not obscure gameplay
4. THE HUD overlay SHALL be toggleable via a hotkey (Ctrl+F11)
5. THE HUD overlay SHALL position itself in a configurable screen corner (top-left, top-right, bottom-left, bottom-right)
6. THE HUD overlay SHALL update in real-time (every frame)
7. THE HUD overlay SHALL use color coding: green text for normal operation, yellow for warnings, red for errors
8. WHERE the HUD is disabled, THE AutoBossClient SHALL skip all OnGUI rendering to save performance

### Requirement 23: Boss Notification Integration (Leverage Existing Strength)

**User Story:** As a system developer, I want to ensure the existing boss notification hook continues to work with the Manager integration, so that we maintain fast boss detection.

#### Acceptance Criteria

1. THE AutoBossClient SHALL preserve the existing BossNotificationHook functionality
2. WHEN a boss notification is received from server, THE AutoBossClient SHALL immediately send a BOSS_FOUND event to Manager
3. THE AutoBossClient SHALL preserve the existing MessageHook for chat message parsing
4. THE Manager SHALL display boss notifications in real-time on the Dashboard
5. THE Manager SHALL maintain a boss sighting log showing: timestamp, boss name, map, zone, which Bot_Instance detected it
6. THE AutoBossClient SHALL use boss notifications as the primary detection method with passive scanning as fallback
7. THE Manager SHALL support filtering boss notifications by boss type or map
8. THE Manager SHALL calculate boss spawn statistics: average time between spawns per boss type, spawn frequency per map

### Requirement 24: Boss Skill Manager Integration (Leverage Existing Strength)

**User Story:** As a system developer, I want to ensure the existing Boss Skill Manager continues to work with the Manager integration, so that boss combat remains effective.

#### Acceptance Criteria

1. THE AutoBossClient SHALL preserve the existing BossSkillManager functionality
2. THE Boss skill configuration SHALL be part of the Bot_Profile and support Hot_Reload
3. THE Manager SHALL allow configuring boss skill triggers: HP thresholds, skill IDs, spam count
4. THE AutoBossClient SHALL report skill usage to Manager for analytics
5. THE Analytics_Engine SHALL track skill effectiveness: skills used per boss kill, average damage per skill
6. THE Manager SHALL support creating skill presets for different boss types
7. THE AutoBossClient SHALL validate skill configurations and warn if skills are not available
8. THE Manager SHALL display current boss HP and skill cooldowns in real-time on Dashboard (if data is available)

### Requirement 25: Reconnect Logic Integration (Leverage Existing Strength)

**User Story:** As a system developer, I want to ensure the existing reconnect logic works with the Manager integration, so that bots automatically recover from disconnections.

#### Acceptance Criteria

1. THE AutoBossClient SHALL preserve the existing AutoLoginController functionality with exponential backoff
2. WHEN the game disconnects, THE AutoBossClient SHALL notify Manager of disconnect event
3. THE AutoBossClient SHALL attempt automatic reconnection using existing logic
4. THE Manager SHALL track disconnect statistics: disconnect count, disconnect reasons, average reconnect time
5. THE Manager SHALL display disconnect events in the log viewer and Dashboard
6. THE Manager SHALL support configuring max reconnect attempts per Bot_Instance
7. WHERE max reconnect attempts are exceeded, THE Manager SHALL mark the Bot_Instance as failed and optionally restart the game process
8. THE AutoBossClient SHALL report successful reconnections to Manager with elapsed time

---

## Notes on Phase 1 Scope

This requirements document prioritizes features that enable multi-instance operation and centralized management. The following design decisions were made based on source code analysis:

**Critical Features (Must Have for Phase 1):**
- Game Optimizer for memory/CPU reduction → enables 10+ instances
- Socket IPC with thread-safe command execution → enables remote control
- BFS Pathfinding → eliminates manual portal chain configuration
- Multi-instance management → core value proposition
- Real-time monitoring dashboard → operational visibility

**High-Value Features (Should Have for Phase 1):**
- Advanced Farm Loop → automates town farming
- Item Filter → reduces inventory management overhead
- Captcha integration → maintains existing strength
- Boss notification/skill manager → maintains existing strengths

**Deferred Features (Phase 2):**
- Advanced coordination service (boss claim reservation)
- Scheduling system (time-based automation)
- Discord webhook notifications
- Advanced scripting language with custom grammar parser
- Network protocol parser (beyond existing message hooks)
- License system (not needed for personal tool)

**Removed Features:**
- Auto Potion system (per user request)

The parser requirements from the original document were over-engineered for Phase 1. The focus is on practical multi-instance management using proven patterns from V111 while preserving AutoBossGrabber's existing technical advantages.
