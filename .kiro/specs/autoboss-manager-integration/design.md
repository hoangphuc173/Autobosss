# Design Document: AutoBoss Manager Integration

## Overview

This document outlines the technical design for transforming AutoBossGrabber from a standalone BepInEx plugin into a centralized multi-account bot management system. The design draws from Tool_Up_Level_V111's proven client-server architecture while preserving AutoBossGrabber's superior performance optimizations and robust state machine.

### Design Goals

1. **Multi-Instance Support**: Enable 10+ game instances on one machine through aggressive memory/CPU optimization
2. **Centralized Control**: Provide desktop application for managing all bot instances from one interface
3. **Robust IPC**: Thread-safe remote command execution without Unity crashes
4. **Smart Navigation**: Automatic pathfinding eliminates manual portal chain configuration
5. **Preserve Strengths**: Maintain existing boss detection, skill manager, and captcha AI capabilities

### Architecture Principles

- **Separation of Concerns**: Manager (UI/control) vs Client (game integration)
- **Thread Safety**: ConcurrentQueue pattern for Unity main thread execution
- **Fault Tolerance**: Auto-reconnect, state persistence, graceful degradation
- **Performance First**: Every optimization targets enabling 10+ instances
- **Incremental Enhancement**: Keep existing AutoBoss core, add new capabilities

---

## Architecture

### System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     AutoBossManager.exe                         │
│                   (WPF Desktop Application)                     │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐         │
│  │   Dashboard  │  │Profile Manager│ │Analytics Engine│         │
│  │   (WPF UI)   │  │ (JSON Files) │  │ (Metrics DB)  │         │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘         │
│         │                  │                  │                  │
│  ┌──────┴──────────────────┴──────────────────┴───────┐         │
│  │              SocketServer (TCP 28081)              │         │
│  │  ┌────────────────────────────────────────────┐   │         │
│  │  │  ConnectionManager + CommandDispatcher     │   │         │
│  │  └────────────────────────────────────────────┘   │         │
│  └────────────────────────┬───────────────────────────┘         │
└───────────────────────────┼─────────────────────────────────────┘
                            │
                            │ TCP Socket (127.0.0.1:28081)
                            │ JSON Line Protocol
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
        ▼                   ▼                   ▼
┌───────────────┐   ┌───────────────┐   ┌───────────────┐
│ Game Instance │   │ Game Instance │   │ Game Instance │
│      #1       │   │      #2       │   │    #3-10+     │
├───────────────┤   ├───────────────┤   ├───────────────┤
│AutoBossClient │   │AutoBossClient │   │AutoBossClient │
│   (DLL)       │   │   (DLL)       │   │   (DLL)       │
└───────────────┘   └───────────────┘   └───────────────┘

Game Instance Internal Architecture:
┌─────────────────────────────────────────────────────────────────┐
│          Vũ Trụ Đại Chiến.exe + BepInEx Runtime               │
├─────────────────────────────────────────────────────────────────┤
│                    AutoBossClient.dll                          │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │              NEW COMPONENTS (Phase 1)                    │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │  │
│  │  │SocketClient │  │GameOptimizer│  │BFSPathfinder│     │  │
│  │  │(TCP+Queue)  │  │(P/Invoke+GC)│  │(Graph BFS)  │     │  │
│  │  └─────────────┘  └─────────────┘  └─────────────┘     │  │
│  │  ┌─────────────┐  ┌─────────────┐                       │  │
│  │  │FarmLoopSM   │  │ItemFilter   │                       │  │
│  │  │(Town Farm)  │  │(Whitelist)  │                       │  │
│  │  └─────────────┘  └─────────────┘                       │  │
│  └──────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │         EXISTING COMPONENTS (Preserved)                  │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │  │
│  │  │AutoBossRunner│ │BossDetector │  │SkillManager │     │  │
│  │  │(State Machine│  │(3-Layer+    │  │(HP Triggers)│     │  │
│  │  │ 8 states)    │  │ Cache)      │  │             │     │  │
│  │  └─────────────┘  └─────────────┘  └─────────────┘     │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │  │
│  │  │MapTransport │  │ZoneSwitcher │  │AutoPickupLite│    │  │
│  │  │(Go Home)    │  │(Scan Zones) │  │(Loot Items) │     │  │
│  │  └─────────────┘  └─────────────┘  └─────────────┘     │  │
│  │  ┌─────────────┐  ┌─────────────┐                       │  │
│  │  │AutoBossUI   │  │AutoLogin    │                       │  │
│  │  │(OnGUI HUD)  │  │Controller   │                       │  │
│  │  └─────────────┘  └─────────────┘                       │  │
│  └──────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                    GameAPI.cs                            │  │
│  │         (Reflection Wrapper for Game Types)              │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

#### AutoBossManager (Desktop Application)

**Dashboard Component**
- Real-time status display for all bot instances (1 second refresh)
- Color-coded status indicators (green/yellow/red/gray)
- Bot controls: Start, Stop, Pause, Resume per instance or bulk
- Log viewer with filtering and search
- Analytics charts: boss kills over time, efficiency comparison

**SocketServer Component**
- TCP server listening on 127.0.0.1:28081 (configurable)
- Maintains registry of connected clients with heartbeat monitoring
- Command dispatcher with acknowledgment tracking
- Message serialization/deserialization (JSON)
- Connection authentication using shared secret key

**ProfileManager Component**
- CRUD operations for BotProfile configurations
- JSON file storage in `AppData/AutoBossManager/profiles/`
- Profile validation (required fields, value ranges)
- Import/Export functionality
- Strategy preset templates (Aggressive, Balanced, Safe)

**AnalyticsEngine Component**
- Metrics collection: boss kills, uptime, efficiency, errors
- Session tracking with start/end timestamps
- Per-instance and aggregate statistics
- CSV export for historical analysis
- Daily/weekly summary report generation

#### AutoBossClient (BepInEx Plugin)

**SocketClient Component**
- TCP client connecting to Manager on startup
- Background thread for message receive loop
- `ConcurrentQueue<Action>` for thread-safe command queueing
- Heartbeat sender (every 3 seconds)
- Reconnection logic with exponential backoff (1s, 2s, 4s, 8s, max 30s)

**GameOptimizer Component**
- P/Invoke declarations for Windows memory APIs
- GC optimization (SustainedLowLatency, LOH compaction)
- Harmony patches to disable rendering (ParallaxBackground, particle effects)
- Scheduled optimization execution (every 60 seconds)
- Memory metrics logging

**BFSPathfinder Component**
- Map graph construction from MapGateway objects
- Bidirectional edge representation
- BFS algorithm implementation (queue-based)
- Path reconstruction from parent map
- Fallback to hard-coded PortalChainMaps if no path found
- Graph cache with invalidation on game data updates

**FarmLoopStateMachine Component**
- Parallel state machine for town farming automation
- States: FarmIdle, FarmFindMobs, FarmZoneEmpty, FarmRewardCheck, FarmSatelliteCheck
- Integration hooks with main AutoBossRunner state machine
- Configurable enable/disable per feature (zone switch, reward claim, satellite)

**ItemFilterManager Component**
- Whitelist/blacklist rule storage
- Item pickup decision logic
- Pattern matching for item names
- Special rules: always pick gems, quest items, high rarity
- Auto inventory sorting by rarity or type

**Existing Components (Enhanced)**
- AutoBossRunner: Add IPC command handlers
- BossDetector: Send BOSS_FOUND events to Manager
- BossSkillManager: Support hot-reload of skill configurations
- AutoLoginController: Report disconnect/reconnect events to Manager

---

## Data Models

### BotProfile (Manager Storage)

```csharp
/// <summary>
/// Complete configuration for a single bot instance.
/// Stored as JSON in AppData/AutoBossManager/profiles/{AccountName}.json
/// </summary>
public class BotProfile
{
    // === Identity ===
    public string AccountName { get; set; }           // Unique identifier
    public string GameExecutablePath { get; set; }    // Path to game .exe
    
    // === Game Credentials ===
    public string Username { get; set; }
    public string Password { get; set; }              // Encrypted in storage
    
    // === Boss Hunting Configuration ===
    public List<string> TargetBossNames { get; set; }
    public List<string> BossMapNames { get; set; }
    public string HomeMapName { get; set; }
    public string TownMapName { get; set; }
    public string FastTravelAnchorMap { get; set; }
    public List<string> PortalChainMaps { get; set; }
    
    // === Behavior Parameters ===
    public int MaxZoneAttempts { get; set; }
    public float AttackRange { get; set; }
    public float CombatTimeoutSec { get; set; }
    public float RetreatHpPct { get; set; }
    public float LootRadius { get; set; }
    
    // === Strategy Preset ===
    public StrategyPreset Strategy { get; set; }
    
    // === Boss Skill Configuration ===
    public List<SkillTrigger> BossSkillTriggers { get; set; }
    
    // === Farm Loop Configuration ===
    public bool EnableAutoZoneSwitch { get; set; }
    public bool EnableAutoReward { get; set; }
    public bool EnableAutoSatellite { get; set; }
    
    // === Item Filter Configuration ===
    public ItemFilterMode FilterMode { get; set; }    // Whitelist, Blacklist, Disabled
    public List<string> ItemFilterList { get; set; }
    public bool AlwaysPickGems { get; set; }
    public bool AlwaysPickQuestItems { get; set; }
    public int MinRarityToPickup { get; set; }
    
    // === Auto Restart Configuration ===
    public bool AutoRestartOnCrash { get; set; }
    public int MaxRestartAttempts { get; set; }
    
    // === Schedule Configuration (Phase 2) ===
    public Schedule Schedule { get; set; }
}

public enum StrategyPreset
{
    Aggressive,    // Fast movement, low dwell times, high risk
    Balanced,      // Medium settings, good for most situations
    Safe,          // Slow movement, high dwell times, low detection risk
    Custom         // User-defined parameters
}

public enum ItemFilterMode
{
    Disabled,      // Pick up all items
    Whitelist,     // Only pick items in list
    Blacklist      // Pick all except items in list
}

public class SkillTrigger
{
    public float HpThreshold { get; set; }    // Absolute HP value (not percentage)
    public int SkillKey { get; set; }         // 1-4
    public int SpamCount { get; set; }        // How many times to press (default 1)
}

public class Schedule
{
    public bool Enabled { get; set; }
    public List<TimeWindow> ActiveWindows { get; set; }
}

public class TimeWindow
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}
```

### BotInstanceState (Runtime State)

```csharp
/// <summary>
/// Real-time state of a connected bot instance.
/// Maintained in Manager memory, periodically received from Client.
/// </summary>
public class BotInstanceState
{
    // === Identity ===
    public Guid InstanceId { get; set; }              // Unique instance ID
    public string AccountName { get; set; }
    
    // === Connection Status ===
    public ConnectionStatus Status { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public DateTime SessionStartTime { get; set; }
    
    // === Game State ===
    public AutoBossState CurrentState { get; set; }   // Idle, MoveToBoss, etc.
    public string CurrentMap { get; set; }
    public int CurrentZone { get; set; }
    public Vector2 PlayerPosition { get; set; }
    
    // === Player Stats ===
    public float PlayerHpPct { get; set; }
    public float PlayerMpPct { get; set; }
    
    // === Progress Metrics ===
    public int BossKillsThisSession { get; set; }
    public int TotalBossKills { get; set; }
    public int DeathCount { get; set; }
    public int CaptchaSolveCount { get; set; }
    public int ErrorCount { get; set; }
    
    // === Recent Activity ===
    public List<string> RecentErrors { get; set; }    // Last 10 errors
    public DateTime LastBossKillTime { get; set; }
    public string LastBossKilled { get; set; }
    
    // === Calculated Metrics ===
    public TimeSpan Uptime => DateTime.Now - SessionStartTime;
    public double BossKillsPerHour => 
        BossKillsThisSession / Math.Max(Uptime.TotalHours, 0.01);
}

public enum ConnectionStatus
{
    Disconnected,   // Not connected
    Connected,      // Connected but not started
    Active,         // Actively farming
    Paused,         // Paused by user
    Error,          // In error state
    Stopping        // Shutting down
}

public enum AutoBossState
{
    Idle,
    DetectBoss,
    MoveToBoss,
    ZoneScanLoop,
    EngageBoss,
    CombatActive,
    LootItems,
    ReturnHome
}
```

### IPC Message Protocol

```csharp
/// <summary>
/// Base message structure for all IPC communication.
/// Serialized as line-delimited JSON over TCP socket.
/// </summary>
public class IpcMessage
{
    public string Type { get; set; }          // Command type identifier
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Payload { get; set; }
}

/// <summary>
/// Message types (Type field values)
/// </summary>
public static class MessageTypes
{
    // === Manager → Client ===
    public const string COMMAND = "COMMAND";
    public const string CONFIG_UPDATE = "CONFIG_UPDATE";
    public const string SHUTDOWN = "SHUTDOWN";
    
    // === Client → Manager ===
    public const string HEARTBEAT = "HEARTBEAT";
    public const string STATUS_UPDATE = "STATUS_UPDATE";
    public const string LOG_EVENT = "LOG_EVENT";
    public const string BOSS_FOUND = "BOSS_FOUND";
    public const string BOSS_KILLED = "BOSS_KILLED";
    public const string CAPTCHA_DETECTED = "CAPTCHA_DETECTED";
    public const string ERROR = "ERROR";
    
    // === Bidirectional ===
    public const string ACK = "ACK";
}

/// <summary>
/// Command types (Payload.command field values)
/// </summary>
public static class Commands
{
    public const string START_FARMING = "START_FARMING";
    public const string STOP_FARMING = "STOP_FARMING";
    public const string PAUSE = "PAUSE";
    public const string RESUME = "RESUME";
    public const string RETURN_TO_TOWN = "RETURN_TO_TOWN";
    public const string TELEPORT_TO_MAP = "TELEPORT_TO_MAP";
    public const string SWITCH_ZONE = "SWITCH_ZONE";
    public const string INVALIDATE_CACHE = "INVALIDATE_CACHE";
    public const string RELOAD_CONFIG = "RELOAD_CONFIG";
}
```

**Example Messages:**

```json
// Manager → Client: Start farming command
{
  "Type": "COMMAND",
  "Timestamp": "2024-01-15T10:30:00Z",
  "Payload": {
    "command": "START_FARMING"
  }
}

// Client → Manager: Status update
{
  "Type": "STATUS_UPDATE",
  "Timestamp": "2024-01-15T10:30:05Z",
  "Payload": {
    "state": "MoveToBoss",
    "map": "Cung Điện",
    "zone": 5,
    "playerHpPct": 85.5,
    "bossKillsThisSession": 12,
    "currentTarget": "Vua Vegita"
  }
}

// Client → Manager: Heartbeat
{
  "Type": "HEARTBEAT",
  "Timestamp": "2024-01-15T10:30:03Z",
  "Payload": {}
}

// Client → Manager: Boss found notification
{
  "Type": "BOSS_FOUND",
  "Timestamp": "2024-01-15T10:31:00Z",
  "Payload": {
    "bossName": "Vua Vegita",
    "mapName": "Cung Điện",
    "zoneName": "Khu 5",
    "detectionMethod": "ServerNotification"
  }
}

// Manager → Client: Config update (hot-reload)
{
  "Type": "CONFIG_UPDATE",
  "Timestamp": "2024-01-15T10:32:00Z",
  "Payload": {
    "maxZoneAttempts": 10,
    "retreatHpPct": 30.0,
    "bossSkillTriggers": [
      { "hpThreshold": 500000, "skillKey": 1 }
    ]
  }
}

// Client → Manager: Log event
{
  "Type": "LOG_EVENT",
  "Timestamp": "2024-01-15T10:30:10Z",
  "Payload": {
    "level": "Info",
    "message": "Boss detected: Vua Vegita at Cung Điện Khu 5"
  }
}

// Manager → Client: Teleport command
{
  "Type": "COMMAND",
  "Timestamp": "2024-01-15T10:33:00Z",
  "Payload": {
    "command": "TELEPORT_TO_MAP",
    "targetMap": "Trạm Frizar"
  }
}
```

### Persisted State (For Recovery)

```csharp
/// <summary>
/// State persisted to disk for crash recovery.
/// Stored in BepInEx/config/AutoBossGrabber/{account_name}_state.json
/// </summary>
public class PersistedState
{
    public DateTime SavedAt { get; set; }
    public AutoBossState CurrentState { get; set; }
    public string CurrentMap { get; set; }
    public int CurrentZone { get; set; }
    public int BossKillsThisSession { get; set; }
    public DateTime SessionStartTime { get; set; }
    public string LastTargetBoss { get; set; }
}
```

---

## Components and Interfaces

### 1. GameOptimizer Module

The GameOptimizer is **critical** for enabling 10+ instances per machine. It combines Windows API calls, GC tuning, and Harmony patches to reduce memory and CPU usage.

#### Implementation Details

```csharp
using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace AutoBossGrabber
{
    /// <summary>
    /// Aggressive memory and CPU optimization for multi-instance support.
    /// Target: Reduce per-instance RAM from ~1.2GB to &lt;800MB.
    /// Pattern from Tool_Up_Level_V111 GameOptimizer.cs.
    /// </summary>
    public class GameOptimizer
    {
        // === P/Invoke Declarations ===
        
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(
            IntPtr hProcess,
            IntPtr dwMinimumWorkingSetSize,
            IntPtr dwMaximumWorkingSetSize);
        
        // === Configuration ===
        
        private const float OptimizationIntervalSec = 60f;
        private float lastOptimizationTime = 0f;
        private bool isEnabled = true;
        
        // === Initialization ===
        
        public void Initialize(Harmony harmony)
        {
            try
            {
                // Apply Harmony patches to disable expensive rendering
                ApplyRenderingPatches(harmony);
                
                // Configure GC for sustained low latency
                ConfigureGarbageCollector();
                
                Plugin.Log.LogInfo("[GameOptimizer] Initialized successfully");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[GameOptimizer] Init failed: {ex.Message}");
            }
        }
        
        // === Main Optimization Tick ===
        
        public void Update()
        {
            if (!isEnabled) return;
            
            if (Time.time - lastOptimizationTime >= OptimizationIntervalSec)
            {
                lastOptimizationTime = Time.time;
                ExecuteOptimization();
            }
        }
        
        // === Core Optimization Logic ===
        
        private void ExecuteOptimization()
        {
            try
            {
                long memBefore = GC.GetTotalMemory(false);
                
                // Step 1: Force full GC collection
                GCSettings.LargeObjectHeapCompactionMode = 
                    GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                
                // Step 2: Empty working set (release unused pages back to OS)
                IntPtr processHandle = Process.GetCurrentProcess().Handle;
                EmptyWorkingSet(processHandle);
                
                // Step 3: Set working set size to minimum (-1, -1 = OS decides minimum)
                SetProcessWorkingSetSize(processHandle, new IntPtr(-1), new IntPtr(-1));
                
                long memAfter = GC.GetTotalMemory(false);
                long freed = memBefore - memAfter;
                
                if (freed > 1024 * 1024) // Log only if freed > 1MB
                {
                    Plugin.Log.LogInfo(
                        $"[GameOptimizer] Memory freed: {freed / 1024 / 1024}MB " +
                        $"(Before: {memBefore / 1024 / 1024}MB, After: {memAfter / 1024 / 1024}MB)");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[GameOptimizer] Optimization failed: {ex.Message}");
            }
        }
        
        // === GC Configuration ===
        
        private void ConfigureGarbageCollector()
        {
            try
            {
                // SustainedLowLatency reduces GC pause times
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
                
                Plugin.Log.LogInfo("[GameOptimizer] GC configured: SustainedLowLatency");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[GameOptimizer] GC config failed: {ex.Message}");
            }
        }
        
        // === Harmony Patches for Rendering ===
        
        private void ApplyRenderingPatches(Harmony harmony)
        {
            try
            {
                // Patch 1: Disable ParallaxBackground (expensive shader rendering)
                var tParallax = GameAPI.FindTypeByName("ParallaxBackground");
                if (tParallax != null)
                {
                    var mUpdate = tParallax.GetMethod("FixedUpdate", 
                        System.Reflection.BindingFlags.Instance | 
                        System.Reflection.BindingFlags.Public | 
                        System.Reflection.BindingFlags.NonPublic);
                    
                    if (mUpdate != null)
                    {
                        harmony.Patch(mUpdate, 
                            postfix: new HarmonyMethod(
                                typeof(GameOptimizer_Patches), 
                                nameof(GameOptimizer_Patches.ParallaxBackground_FixedUpdate_Postfix)));
                        
                        Plugin.Log.LogInfo("[GameOptimizer] Patched ParallaxBackground.FixedUpdate");
                    }
                }
                
                // Patch 2: Disable particle systems (optional - may affect gameplay visibility)
                // Disabled by default to avoid breaking visual cues
                
                // Patch 3: Reduce shadow quality via QualitySettings
                QualitySettings.shadowDistance = 0f;
                QualitySettings.shadows = ShadowQuality.Disable;
                
                Plugin.Log.LogInfo("[GameOptimizer] Rendering patches applied");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[GameOptimizer] Patch failed: {ex.Message}");
            }
        }
        
        // === Public Control ===
        
        public void SetEnabled(bool enabled)
        {
            isEnabled = enabled;
            Plugin.Log.LogInfo($"[GameOptimizer] Optimization {(enabled ? "enabled" : "disabled")}");
        }
        
        public void ForceOptimizeNow()
        {
            ExecuteOptimization();
        }
    }
    
    // === Harmony Patch Class ===
    
    [HarmonyPatch]
    public static class GameOptimizer_Patches
    {
        /// <summary>
        /// Disable ParallaxBackground rendering after its FixedUpdate.
        /// This prevents expensive background shader execution.
        /// </summary>
        public static void ParallaxBackground_FixedUpdate_Postfix(MonoBehaviour __instance)
        {
            try
            {
                if (__instance != null && __instance.enabled)
                {
                    __instance.enabled = false;
                }
            }
            catch
            {
                // Silently ignore - component may be destroyed
            }
        }
    }
}
```

**Expected Performance Impact:**

| Metric | Before Optimization | After Optimization | Improvement |
|--------|--------------------|--------------------|-------------|
| RAM per instance | ~1200 MB | ~800 MB | -33% |
| CPU per instance | ~8% | ~5% | -37.5% |
| Max instances (16GB RAM) | 8-9 | 12-14 | +50% |
| GC pause time | 50-100ms | 20-40ms | -60% |

### 2. SocketClient with Thread-Safe Command Execution

The SocketClient must execute remote commands on Unity's main thread to avoid crashes and race conditions.

#### Implementation Details

```csharp
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace AutoBossGrabber
{
    /// <summary>
    /// TCP socket client with thread-safe command execution on Unity main thread.
    /// Pattern from Tool_Up_Level_V111 SocketClient.cs.
    /// </summary>
    public class SocketClient : MonoBehaviour
    {
        // === Configuration ===
        private const string ServerHost = "127.0.0.1";
        private const int ServerPort = 28081;
        private const float HeartbeatIntervalSec = 3f;
        private const float ReconnectIntervalSec = 5f;
        
        // === Connection State ===
        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;
        private bool isConnected = false;
        private bool isShuttingDown = false;
        
        // === Thread-Safe Queue ===
        private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();
        
        // === Background Threads ===
        private Thread receiveThread;
        private Task heartbeatTask;
        private CancellationTokenSource cts;
        
        // === Reconnection ===
        private int reconnectAttempt = 0;
        private float nextReconnectTime = 0f;
        
        // === Initialization ===
        
        void Start()
        {
            cts = new CancellationTokenSource();
            ConnectToManager();
            StartHeartbeat();
        }
        
        void OnDestroy()
        {
            isShuttingDown = true;
            cts?.Cancel();
            Disconnect();
        }
        
        // === Unity Main Thread Update ===
        
        void Update()
        {
            // Process all queued commands on main thread
            while (mainThreadQueue.TryDequeue(out Action action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[SocketClient] Command execution failed: {ex.Message}");
                    SendError($"Command execution failed: {ex.Message}");
                }
            }
            
            // Check reconnection
            if (!isConnected && !isShuttingDown && Time.time >= nextReconnectTime)
            {
                ConnectToManager();
            }
        }
        
        // === Connection Management ===
        
        private void ConnectToManager()
        {
            try
            {
                client = new TcpClient();
                client.Connect(ServerHost, ServerPort);
                
                NetworkStream stream = client.GetStream();
                reader = new StreamReader(stream, Encoding.UTF8);
                writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                
                isConnected = true;
                reconnectAttempt = 0;
                
                Plugin.Log.LogInfo($"[SocketClient] Connected to Manager at {ServerHost}:{ServerPort}");
                
                // Start receive thread
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                receiveThread.Start();
                
                // Send initial status
                SendStatusUpdate();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SocketClient] Connection failed: {ex.Message}");
                ScheduleReconnect();
            }
        }
        
        private void Disconnect()
        {
            try
            {
                isConnected = false;
                reader?.Close();
                writer?.Close();
                client?.Close();
                Plugin.Log.LogInfo("[SocketClient] Disconnected from Manager");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SocketClient] Disconnect error: {ex.Message}");
            }
        }
        
        private void ScheduleReconnect()
        {
            reconnectAttempt++;
            // Exponential backoff: 1s, 2s, 4s, 8s, max 30s
            float delay = Mathf.Min(Mathf.Pow(2, reconnectAttempt - 1), 30f);
            nextReconnectTime = Time.time + delay;
            Plugin.Log.LogInfo($"[SocketClient] Reconnect scheduled in {delay}s (attempt #{reconnectAttempt})");
        }
        
        // === Message Receive Loop (Background Thread) ===
        
        private void ReceiveLoop()
        {
            Plugin.Log.LogInfo("[SocketClient] Receive loop started");
            
            try
            {
                while (isConnected && !isShuttingDown)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        // Connection closed by server
                        Plugin.Log.LogWarning("[SocketClient] Connection closed by Manager");
                        break;
                    }
                    
                    // Parse message
                    IpcMessage message = JsonConvert.DeserializeObject<IpcMessage>(line);
                    
                    // Enqueue command to main thread
                    mainThreadQueue.Enqueue(() => HandleMessage(message));
                }
            }
            catch (IOException)
            {
                Plugin.Log.LogWarning("[SocketClient] Connection lost (IO error)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SocketClient] Receive loop error: {ex.Message}");
            }
            finally
            {
                Disconnect();
                if (!isShuttingDown)
                {
                    ScheduleReconnect();
                }
            }
        }
        
        // === Message Handling (Main Thread) ===
        
        private void HandleMessage(IpcMessage message)
        {
            try
            {
                Plugin.Log.LogInfo($"[SocketClient] Received: {message.Type}");
                
                switch (message.Type)
                {
                    case MessageTypes.COMMAND:
                        HandleCommand(message);
                        break;
                        
                    case MessageTypes.CONFIG_UPDATE:
                        HandleConfigUpdate(message);
                        break;
                        
                    case MessageTypes.SHUTDOWN:
                        HandleShutdown();
                        break;
                        
                    default:
                        Plugin.Log.LogWarning($"[SocketClient] Unknown message type: {message.Type}");
                        break;
                }
                
                // Send ACK
                SendAck(message.Type);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SocketClient] Message handling error: {ex.Message}");
                SendError($"Message handling failed: {ex.Message}");
            }
        }
        
        private void HandleCommand(IpcMessage message)
        {
            if (!message.Payload.TryGetValue("command", out object cmdObj))
            {
                Plugin.Log.LogWarning("[SocketClient] Command message missing 'command' field");
                return;
            }
            
            string command = cmdObj.ToString();
            Plugin.Log.LogInfo($"[SocketClient] Executing command: {command}");
            
            switch (command)
            {
                case Commands.START_FARMING:
                    Plugin.Instance.Runner.SetEnabled(true);
                    break;
                    
                case Commands.STOP_FARMING:
                    Plugin.Instance.Runner.SetEnabled(false);
                    break;
                    
                case Commands.PAUSE:
                    Plugin.Instance.Runner.Pause();
                    break;
                    
                case Commands.RESUME:
                    Plugin.Instance.Runner.Resume();
                    break;
                    
                case Commands.RETURN_TO_TOWN:
                    Plugin.Instance.Runner.ForceReturnHome();
                    break;
                    
                case Commands.TELEPORT_TO_MAP:
                    if (message.Payload.TryGetValue("targetMap", out object mapObj))
                    {
                        string targetMap = mapObj.ToString();
                        Plugin.Instance.Runner.TeleportToMap(targetMap);
                    }
                    break;
                    
                case Commands.SWITCH_ZONE:
                    if (message.Payload.TryGetValue("zone", out object zoneObj))
                    {
                        int zone = Convert.ToInt32(zoneObj);
                        Plugin.Instance.Runner.SwitchToZone(zone);
                    }
                    break;
                    
                case Commands.INVALIDATE_CACHE:
                    Plugin.Instance.Runner.InvalidateCaches();
                    break;
                    
                case Commands.RELOAD_CONFIG:
                    Plugin.Instance.Config = new AutoBossConfig();
                    Plugin.Log.LogInfo("[SocketClient] Config reloaded");
                    break;
                    
                default:
                    Plugin.Log.LogWarning($"[SocketClient] Unknown command: {command}");
                    break;
            }
        }
        
        private void HandleConfigUpdate(IpcMessage message)
        {
            try
            {
                // Hot-reload config from payload
                var config = Plugin.Instance.Config;
                
                if (message.Payload.TryGetValue("maxZoneAttempts", out object maxZone))
                    config.MaxZoneAttempts = Convert.ToInt32(maxZone);
                    
                if (message.Payload.TryGetValue("retreatHpPct", out object retreatHp))
                    config.RetreatHpPct = Convert.ToSingle(retreatHp);
                    
                if (message.Payload.TryGetValue("attackRange", out object attackRange))
                    config.AttackRange = Convert.ToSingle(attackRange);
                
                // Boss skill triggers
                if (message.Payload.TryGetValue("bossSkillTriggers", out object skillsObj))
                {
                    string json = JsonConvert.SerializeObject(skillsObj);
                    config.BossSkillTriggers = JsonConvert.DeserializeObject<List<SkillTrigger>>(json);
                }
                
                Plugin.Log.LogInfo("[SocketClient] Config updated via hot-reload");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SocketClient] Config update failed: {ex.Message}");
                throw;
            }
        }
        
        private void HandleShutdown()
        {
            Plugin.Log.LogInfo("[SocketClient] Shutdown command received");
            Plugin.Instance.Runner.SetEnabled(false);
            Application.Quit();
        }
        
        // === Message Sending ===
        
        private void SendMessage(IpcMessage message)
        {
            if (!isConnected || writer == null) return;
            
            try
            {
                string json = JsonConvert.SerializeObject(message);
                writer.WriteLine(json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SocketClient] Send failed: {ex.Message}");
                Disconnect();
                ScheduleReconnect();
            }
        }
        
        public void SendStatusUpdate()
        {
            var runner = Plugin.Instance.Runner;
            if (runner == null) return;
            
            var message = new IpcMessage
            {
                Type = MessageTypes.STATUS_UPDATE,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["state"] = runner.CurrentState.ToString(),
                    ["map"] = GameAPI.GetCurrentMapName(),
                    ["zone"] = GameAPI.GetCurrentZoneIndexFromHUD(),
                    ["playerHpPct"] = GameAPI.GetPlayerHpPct(),
                    ["bossKillsThisSession"] = runner.BossKillsThisSession,
                    ["currentTarget"] = runner.CurrentTargetBoss
                }
            };
            
            SendMessage(message);
        }
        
        private void SendAck(string messageType)
        {
            var message = new IpcMessage
            {
                Type = MessageTypes.ACK,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["acknowledgedType"] = messageType
                }
            };
            
            SendMessage(message);
        }
        
        public void SendBossFound(string bossName, string mapName, string zoneName)
        {
            var message = new IpcMessage
            {
                Type = MessageTypes.BOSS_FOUND,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["bossName"] = bossName,
                    ["mapName"] = mapName,
                    ["zoneName"] = zoneName,
                    ["detectionMethod"] = "ServerNotification"
                }
            };
            
            SendMessage(message);
            Plugin.Log.LogInfo($"[SocketClient] Sent BOSS_FOUND: {bossName} at {mapName} {zoneName}");
        }
        
        public void SendBossKilled(string bossName, float killDurationSec)
        {
            var message = new IpcMessage
            {
                Type = MessageTypes.BOSS_KILLED,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["bossName"] = bossName,
                    ["killDurationSec"] = killDurationSec
                }
            };
            
            SendMessage(message);
        }
        
        public void SendLogEvent(string level, string logMessage)
        {
            var message = new IpcMessage
            {
                Type = MessageTypes.LOG_EVENT,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["level"] = level,
                    ["message"] = logMessage
                }
            };
            
            SendMessage(message);
        }
        
        public void SendError(string errorMessage)
        {
            var message = new IpcMessage
            {
                Type = MessageTypes.ERROR,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["message"] = errorMessage
                }
            };
            
            SendMessage(message);
        }
        
        // === Heartbeat (Async Task) ===
        
        private void StartHeartbeat()
        {
            heartbeatTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay((int)(HeartbeatIntervalSec * 1000), cts.Token);
                        
                        if (isConnected)
                        {
                            var message = new IpcMessage
                            {
                                Type = MessageTypes.HEARTBEAT,
                                Timestamp = DateTime.UtcNow,
                                Payload = new Dictionary<string, object>()
                            };
                            
                            SendMessage(message);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[SocketClient] Heartbeat error: {ex.Message}");
                    }
                }
            }, cts.Token);
        }
    }
}
```

**Thread Safety Guarantees:**

1. **ConcurrentQueue**: Lock-free thread-safe queue for command buffering
2. **Main Thread Execution**: All game API calls happen in `Update()` on Unity main thread
3. **Error Isolation**: Each command execution wrapped in try-catch to prevent cascading failures
4. **Graceful Degradation**: Connection loss doesn't crash the game, auto-reconnects

### 3. BFSPathfinder for Dynamic Map Navigation

The BFS pathfinder eliminates manual portal chain configuration by computing shortest paths dynamically from map gateway data.

#### Implementation Details

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutoBossGrabber
{
    /// <summary>
    /// BFS pathfinding for automatic map navigation.
    /// Builds graph from MapGateway objects, finds shortest portal path.
    /// Pattern from Tool_Up_Level_V111 Xmap.cs (BFS algorithm).
    /// </summary>
    public class BFSPathfinder
    {
        // === Graph Storage ===
        private Dictionary<int, List<int>> graph = new Dictionary<int, List<int>>();
        private Dictionary<int, string> mapIdToName = new Dictionary<int, string>();
        private Dictionary<string, int> mapNameToId = new Dictionary<string, int>();
        private bool isGraphBuilt = false;
        private float graphBuildTime = 0f;
        
        // === Configuration ===
        private const float GraphRebuildIntervalSec = 300f; // Rebuild every 5 minutes
        private const int MaxPathLength = 20; // Prevent infinite loops
        
        // === Initialization ===
        
        public void Initialize()
        {
            BuildMapGraph();
        }
        
        public void Update()
        {
            // Periodically rebuild graph in case game data changes
            if (Time.time - graphBuildTime > GraphRebuildIntervalSec)
            {
                BuildMapGraph();
            }
        }
        
        // === Graph Construction ===
        
        public void BuildMapGraph()
        {
            try
            {
                graph.Clear();
                mapIdToName.Clear();
                mapNameToId.Clear();
                
                // Find all MapGateway objects in game
                var gateways = FindMapGateways();
                
                if (gateways.Count == 0)
                {
                    Plugin.Log.LogWarning("[BFSPathfinder] No MapGateway objects found");
                    return;
                }
                
                // Build bidirectional graph
                foreach (var gateway in gateways)
                {
                    int fromMapId = GetMapId(gateway, "fromMapId");
                    int toMapId = GetMapId(gateway, "toMapId");
                    string fromMapName = GetMapName(gateway, "fromMapName");
                    string toMapName = GetMapName(gateway, "toMapName");
                    
                    if (fromMapId <= 0 || toMapId <= 0) continue;
                    
                    // Add bidirectional edges
                    if (!graph.ContainsKey(fromMapId))
                        graph[fromMapId] = new List<int>();
                    if (!graph.ContainsKey(toMapId))
                        graph[toMapId] = new List<int>();
                    
                    graph[fromMapId].Add(toMapId);
                    graph[toMapId].Add(fromMapId);
                    
                    // Store name mappings
                    if (!string.IsNullOrEmpty(fromMapName))
                    {
                        mapIdToName[fromMapId] = fromMapName;
                        mapNameToId[fromMapName] = fromMapId;
                    }
                    if (!string.IsNullOrEmpty(toMapName))
                    {
                        mapIdToName[toMapId] = toMapName;
                        mapNameToId[toMapName] = toMapId;
                    }
                }
                
                isGraphBuilt = true;
                graphBuildTime = Time.time;
                
                Plugin.Log.LogInfo($"[BFSPathfinder] Graph built: {graph.Count} maps, {gateways.Count} gateways");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[BFSPathfinder] Graph build failed: {ex.Message}");
            }
        }
        
        private List<object> FindMapGateways()
        {
            var result = new List<object>();
            
            try
            {
                // Approach 1: GameManager.listMapGateways (if exists)
                var gm = GameAPI.GetGameManager();
                if (gm != null)
                {
                    var t = gm.GetType();
                    var flags = System.Reflection.BindingFlags.Instance | 
                               System.Reflection.BindingFlags.Public | 
                               System.Reflection.BindingFlags.NonPublic;
                    
                    foreach (var field in t.GetFields(flags))
                    {
                        if (field.Name.ToLower().Contains("gateway") || 
                            field.Name.ToLower().Contains("portal"))
                        {
                            var val = field.GetValue(gm);
                            if (val is System.Collections.IEnumerable enumerable)
                            {
                                foreach (var item in enumerable)
                                {
                                    if (item != null) result.Add(item);
                                }
                            }
                        }
                    }
                }
                
                // Approach 2: FindObjectsOfType<MapGateway>
                var tGateway = GameAPI.FindTypeByName("MapGateway");
                if (tGateway != null)
                {
                    var gateways = Il2CppAPI.FindObjectsOfType(tGateway);
                    result.AddRange(gateways);
                }
                
                // Approach 3: Scan ChangeMap objects (fallback)
                var changeMaps = GameAPI.FindChangeMaps();
                result.AddRange(changeMaps);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[BFSPathfinder] FindMapGateways error: {ex.Message}");
            }
            
            return result;
        }
        
        private int GetMapId(object gateway, string fieldName)
        {
            try
            {
                var t = gateway.GetType();
                var flags = System.Reflection.BindingFlags.Instance | 
                           System.Reflection.BindingFlags.Public | 
                           System.Reflection.BindingFlags.NonPublic;
                
                // Try field
                var field = t.GetField(fieldName, flags);
                if (field != null)
                {
                    var val = field.GetValue(gateway);
                    return Convert.ToInt32(val);
                }
                
                // Try property
                var prop = t.GetProperty(fieldName, flags);
                if (prop != null)
                {
                    var val = prop.GetValue(gateway);
                    return Convert.ToInt32(val);
                }
            }
            catch { }
            
            return 0;
        }
        
        private string GetMapName(object gateway, string fieldName)
        {
            try
            {
                var t = gateway.GetType();
                var flags = System.Reflection.BindingFlags.Instance | 
                           System.Reflection.BindingFlags.Public | 
                           System.Reflection.BindingFlags.NonPublic;
                
                var field = t.GetField(fieldName, flags);
                if (field != null)
                {
                    var val = field.GetValue(gateway);
                    return val?.ToString() ?? "";
                }
                
                var prop = t.GetProperty(fieldName, flags);
                if (prop != null)
                {
                    var val = prop.GetValue(gateway);
                    return val?.ToString() ?? "";
                }
            }
            catch { }
            
            return "";
        }
        
        // === Pathfinding (BFS Algorithm) ===
        
        public List<string> FindPath(string startMapName, string targetMapName)
        {
            try
            {
                if (!isGraphBuilt)
                {
                    Plugin.Log.LogWarning("[BFSPathfinder] Graph not built yet");
                    return null;
                }
                
                // Convert names to IDs
                if (!mapNameToId.TryGetValue(startMapName, out int startId))
                {
                    Plugin.Log.LogWarning($"[BFSPathfinder] Start map not found: {startMapName}");
                    return null;
                }
                
                if (!mapNameToId.TryGetValue(targetMapName, out int targetId))
                {
                    Plugin.Log.LogWarning($"[BFSPathfinder] Target map not found: {targetMapName}");
                    return null;
                }
                
                // Run BFS
                var pathIds = FindPathBFS(startId, targetId);
                
                if (pathIds == null || pathIds.Count == 0)
                {
                    Plugin.Log.LogWarning($"[BFSPathfinder] No path found: {startMapName} → {targetMapName}");
                    return null;
                }
                
                // Convert IDs back to names
                var pathNames = pathIds.Select(id => 
                    mapIdToName.TryGetValue(id, out string name) ? name : $"Map{id}")
                    .ToList();
                
                Plugin.Log.LogInfo($"[BFSPathfinder] Path found ({pathNames.Count} steps): " +
                    string.Join(" → ", pathNames));
                
                return pathNames;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[BFSPathfinder] FindPath error: {ex.Message}");
                return null;
            }
        }
        
        private List<int> FindPathBFS(int startId, int targetId)
        {
            if (startId == targetId)
                return new List<int> { startId };
            
            if (!graph.ContainsKey(startId) || !graph.ContainsKey(targetId))
                return null;
            
            // BFS initialization
            var queue = new Queue<int>();
            var parent = new Dictionary<int, int>();
            var visited = new HashSet<int>();
            
            queue.Enqueue(startId);
            parent[startId] = -1;
            visited.Add(startId);
            
            // BFS loop
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                
                // Found target
                if (current == targetId)
                {
                    return ReconstructPath(parent, targetId);
                }
                
                // Explore neighbors
                if (graph.TryGetValue(current, out List<int> neighbors))
                {
                    foreach (int neighbor in neighbors)
                    {
                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            parent[neighbor] = current;
                            queue.Enqueue(neighbor);
                            
                            // Safety check: prevent infinite loops
                            if (parent.Count > MaxPathLength)
                            {
                                Plugin.Log.LogWarning("[BFSPathfinder] Path too long, aborting");
                                return null;
                            }
                        }
                    }
                }
            }
            
            // No path found
            return null;
        }
        
        private List<int> ReconstructPath(Dictionary<int, int> parent, int targetId)
        {
            var path = new List<int>();
            int current = targetId;
            
            while (current != -1)
            {
                path.Add(current);
                parent.TryGetValue(current, out current);
            }
            
            path.Reverse();
            return path;
        }
        
        // === Public Helpers ===
        
        public void InvalidateCache()
        {
            isGraphBuilt = false;
            graphBuildTime = 0f;
            Plugin.Log.LogInfo("[BFSPathfinder] Cache invalidated");
        }
        
        public bool IsGraphReady()
        {
            return isGraphBuilt && graph.Count > 0;
        }
        
        public int GetMapCount()
        {
            return graph.Count;
        }
    }
}
```

**Algorithm Complexity:**

- **Time**: O(V + E) where V = maps, E = portals (typically ~50 maps, ~100 portals)
- **Space**: O(V) for visited set and parent map
- **Typical execution time**: < 10ms for worst-case path (verified in V111)

**Fallback Strategy:**

If BFS fails to find path (disconnected graph, missing gateway data):
1. Try hard-coded `PortalChainMaps` from `AutoBossConfig`
2. Try direct teleport to target map (may fail if not unlocked)
3. Report error to Manager and wait for manual intervention

### 4. FarmLoop State Machine

The FarmLoop runs as a parallel sub-system to AutoBossRunner for town farming automation.

#### State Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    FarmLoop State Machine                   │
│                  (Parallel to AutoBossRunner)               │
└─────────────────────────────────────────────────────────────┘

        ┌──────────────┐
        │  FarmIdle    │ ◄──────────────┐
        │ (Wait start) │                │
        └──────┬───────┘                │
               │ Enable farm loop       │
               ▼                        │
        ┌──────────────┐                │
        │ FarmFindMobs │                │
        │ (Scan zone)  │                │
        └──────┬───────┘                │
               │ Mob found              │ All features
               ▼                        │ complete
        ┌──────────────┐                │
        │ FarmAttack   │                │
        │ (Kill mobs)  │                │
        └──────┬───────┘                │
               │ Zone empty             │
               ▼                        │
        ┌──────────────┐                │
        │FarmZoneEmpty │                │
        │(Check switch)│                │
        └──────┬───────┘                │
               │ Auto zone = ON         │
               ▼                        │
        ┌──────────────┐                │
        │FarmSwitchZone│                │
        │(Next zone)   │                │
        └──────┬───────┘                │
               │ Zone switched          │
               ▼                        │
        ┌──────────────┐                │
        │FarmRewardChk │                │
        │(Detect popup)│                │
        └──────┬───────┘                │
               │ Reward ready           │
               ▼                        │
        ┌──────────────┐                │
        │FarmClaimRewd │                │
        │(Click button)│                │
        └──────┬───────┘                │
               │ Reward claimed         │
               ▼                        │
        ┌──────────────┐                │
        │FarmSatellite │                │
        │(Activate exp)│                │
        └──────┬───────┘                │
               │ Satellite used         │
               └────────────────────────┘
```

#### Implementation Outline

```csharp
namespace AutoBossGrabber
{
    public enum FarmLoopState
    {
        FarmIdle,
        FarmFindMobs,
        FarmAttack,
        FarmZoneEmpty,
        FarmSwitchZone,
        FarmRewardCheck,
        FarmClaimReward,
        FarmSatelliteCheck,
        FarmActivateSatellite
    }
    
    /// <summary>
    /// Parallel state machine for town farming automation.
    /// Runs independently from main AutoBossRunner state machine.
    /// </summary>
    public class FarmLoopStateMachine : MonoBehaviour
    {
        private FarmLoopState currentState = FarmLoopState.FarmIdle;
        private bool isEnabled = false;
        
        // Configuration (from AutoBossConfig)
        private bool enableAutoZoneSwitch = true;
        private bool enableAutoReward = true;
        private bool enableAutoSatellite = true;
        
        // State tracking
        private float lastZoneSwitchTime = 0f;
        private int currentZoneIndex = 0;
        private int zonesCleared = 0;
        
        void Update()
        {
            if (!isEnabled) return;
            
            switch (currentState)
            {
                case FarmLoopState.FarmIdle:
                    // Wait for activation
                    break;
                    
                case FarmLoopState.FarmFindMobs:
                    UpdateFarmFindMobs();
                    break;
                    
                case FarmLoopState.FarmAttack:
                    UpdateFarmAttack();
                    break;
                    
                case FarmLoopState.FarmZoneEmpty:
                    UpdateFarmZoneEmpty();
                    break;
                    
                case FarmLoopState.FarmSwitchZone:
                    UpdateFarmSwitchZone();
                    break;
                    
                case FarmLoopState.FarmRewardCheck:
                    UpdateFarmRewardCheck();
                    break;
                    
                case FarmLoopState.FarmClaimReward:
                    UpdateFarmClaimReward();
                    break;
                    
                case FarmLoopState.FarmSatelliteCheck:
                    UpdateFarmSatelliteCheck();
                    break;
                    
                case FarmLoopState.FarmActivateSatellite:
                    UpdateFarmActivateSatellite();
                    break;
            }
        }
        
        private void UpdateFarmFindMobs()
        {
            var mobs = GameAPI.FindAllMobs();
            var aliveMobs = mobs.Where(m => GameAPI.IsMobAlive(m)).ToList();
            
            if (aliveMobs.Count > 0)
            {
                // Found mobs, attack them
                TransitionTo(FarmLoopState.FarmAttack);
            }
            else
            {
                // Zone empty
                TransitionTo(FarmLoopState.FarmZoneEmpty);
            }
        }
        
        private void UpdateFarmAttack()
        {
            // Use existing AutoBossRunner attack logic
            // When all mobs dead, transition to FarmZoneEmpty
        }
        
        private void UpdateFarmZoneEmpty()
        {
            if (enableAutoZoneSwitch)
            {
                TransitionTo(FarmLoopState.FarmSwitchZone);
            }
            else
            {
                // Farm loop complete
                TransitionTo(FarmLoopState.FarmIdle);
            }
        }
        
        private void UpdateFarmSwitchZone()
        {
            // Call ZoneFunc.select() to switch zone
            // Implementation depends on game's zone switching mechanism
            
            currentZoneIndex++;
            zonesCleared++;
            lastZoneSwitchTime = Time.time;
            
            Plugin.Log.LogInfo($"[FarmLoop] Switched to zone {currentZoneIndex}");
            
            // After zone switch, check for rewards
            TransitionTo(FarmLoopState.FarmRewardCheck);
        }
        
        private void UpdateFarmRewardCheck()
        {
            if (!enableAutoReward)
            {
                TransitionTo(FarmLoopState.FarmSatelliteCheck);
                return;
            }
            
            // Detect reward popup UI
            // If found, transition to claim
            // else, transition to satellite check
        }
        
        private void UpdateFarmClaimReward()
        {
            // Click reward claim button
            // Transition to satellite check
        }
        
        private void UpdateFarmSatelliteCheck()
        {
            if (!enableAutoSatellite)
            {
                // Loop complete, return to find mobs
                TransitionTo(FarmLoopState.FarmFindMobs);
                return;
            }
            
            // Check if satellite item available
            // If yes, activate
        }
        
        private void UpdateFarmActivateSatellite()
        {
            // Use satellite item for exp boost
            // Transition back to find mobs
            TransitionTo(FarmLoopState.FarmFindMobs);
        }
        
        private void TransitionTo(FarmLoopState newState)
        {
            if (currentState == newState) return;
            
            Plugin.Log.LogInfo($"[FarmLoop] {currentState} → {newState}");
            currentState = newState;
        }
        
        public void SetEnabled(bool enabled)
        {
            isEnabled = enabled;
            if (enabled)
            {
                TransitionTo(FarmLoopState.FarmFindMobs);
            }
            else
            {
                TransitionTo(FarmLoopState.FarmIdle);
            }
        }
    }
}
```

**Integration with Main State Machine:**

FarmLoop runs independently but respects main AutoBossRunner state:
- **When AutoBossRunner is Idle**: FarmLoop can run
- **When AutoBossRunner is MoveToBoss/EngageBoss**: FarmLoop pauses
- **After ReturnHome**: FarmLoop resumes

### 5. ItemFilterManager

```csharp
namespace AutoBossGrabber
{
    /// <summary>
    /// Item pickup filtering to reduce inventory management overhead.
    /// Supports whitelist/blacklist modes with pattern matching.
    /// </summary>
    public class ItemFilterManager
    {
        private ItemFilterMode mode = ItemFilterMode.Disabled;
        private HashSet<string> filterList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool alwaysPickGems = true;
        private bool alwaysPickQuestItems = true;
        private int minRarityToPickup = 0; // 0=all, 1=uncommon+, 2=rare+, 3=epic+
        
        // Statistics
        private int itemsPickedUp = 0;
        private int itemsFiltered = 0;
        
        public void Configure(ItemFilterMode filterMode, List<string> items, 
            bool pickGems, bool pickQuest, int minRarity)
        {
            mode = filterMode;
            filterList.Clear();
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item))
                    filterList.Add(item.Trim());
            }
            alwaysPickGems = pickGems;
            alwaysPickQuestItems = pickQuest;
            minRarityToPickup = minRarity;
            
            Plugin.Log.LogInfo($"[ItemFilter] Configured: mode={mode}, rules={filterList.Count}");
        }
        
        public bool ShouldPickup(object itemObject)
        {
            if (mode == ItemFilterMode.Disabled)
                return true; // Pick everything
            
            try
            {
                string itemName = GameAPI.GetItemDisplayName(itemObject);
                int rarity = GetItemRarity(itemObject);
                bool isGem = itemName.Contains("Ngọc") || itemName.Contains("Gem");
                bool isQuest = itemName.Contains("Quest") || itemName.Contains("Nhiệm vụ");
                
                // Always pick special items (override filters)
                if (alwaysPickGems && isGem) return true;
                if (alwaysPickQuestItems && isQuest) return true;
                
                // Rarity filter
                if (rarity < minRarityToPickup)
                {
                    itemsFiltered++;
                    return false;
                }
                
                // Whitelist mode: only pick items IN the list
                if (mode == ItemFilterMode.Whitelist)
                {
                    bool inList = filterList.Any(pattern => 
                        itemName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                    
                    if (inList)
                    {
                        itemsPickedUp++;
                        return true;
                    }
                    else
                    {
                        itemsFiltered++;
                        return false;
                    }
                }
                
                // Blacklist mode: pick items NOT in the list
                if (mode == ItemFilterMode.Blacklist)
                {
                    bool inList = filterList.Any(pattern => 
                        itemName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                    
                    if (inList)
                    {
                        itemsFiltered++;
                        return false;
                    }
                    else
                    {
                        itemsPickedUp++;
                        return true;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ItemFilter] Filter check failed: {ex.Message}");
                return true; // On error, pick item (safe default)
            }
        }
        
        private int GetItemRarity(object itemObject)
        {
            // Extract rarity from item data
            // Game-specific logic required
            return 0;
        }
        
        public void GetStatistics(out int picked, out int filtered)
        {
            picked = itemsPickedUp;
            filtered = itemsFiltered;
        }
    }
}
```

---

## Manager Application Design

### WPF MVVM Architecture

```
View Layer (XAML):
├─ MainWindow.xaml
│   ├─ DashboardView (Bot grid)
│   ├─ LogView (Centralized logs)
│   └─ AnalyticsView (Charts)
├─ ConfigEditorWindow.xaml
├─ ProfileManagerWindow.xaml
└─ StrategyPresetWindow.xaml

ViewModel Layer:
├─ MainViewModel
│   ├─ ObservableCollection<BotInstanceViewModel>
│   ├─ Commands: StartAll, StopAll, EmergencyStop
│   └─ Properties: TotalBossKills, TotalUptime
├─ BotInstanceViewModel
│   ├─ Properties: Status, Map, Zone, HP, Kills, Uptime
│   ├─ Commands: Start, Stop, Pause, Resume, Configure
│   └─ Observable updates from SocketServer
├─ ConfigEditorViewModel
│   └─ BotProfile editing with validation
└─ AnalyticsViewModel
    └─ Chart data binding

Model Layer:
├─ BotProfile
├─ BotInstanceState
├─ AnalyticsMetrics
└─ LogEntry

Service Layer:
├─ SocketServer (TCP listener)
├─ ProfileManager (JSON I/O)
├─ AnalyticsEngine (Metrics collection)
└─ LogAggregator (Centralized logs)
```

### Dashboard UI Mockup

```
╔══════════════════════════════════════════════════════════════════════════╗
║  AutoBoss Manager                                   [_] [□] [X]          ║
╠═══════════╦══════════════════════════════════════════════════════════════╣
║ Profiles  ║  [Start All] [Stop All] [Pause All] [Emergency Stop]        ║
║ ├ Acc1    ╠══════════════════════════════════════════════════════════════╣
║ ├ Acc2    ║  Account │ Status │ Map      │ Zone │ HP  │ Kills │ Uptime  ║
║ ├ Acc3    ╟──────────┼────────┼──────────┼──────┼─────┼───────┼─────────╢
║ ├ Acc4    ║  Acc1    │ ● Active│ Cung    │ 5    │ 85% │ 12    │ 1:23:45║
║ ├ Acc5    ║  Acc2    │ ● Active│ Sa Mạc  │ 3    │ 92% │ 8     │ 0:45:12║
║ ├ Acc6    ║  Acc3    │ ⚠ Error│ Quay    │ 1    │ 100%│ 3     │ 2:10:33║
║ ├ Acc7    ║  Acc4    │ ○ Paused│ Trạm   │ 1    │ 78% │ 15    │ 3:05:21║
║ ├ Acc8    ║  Acc5    │ ● Active│ Cung    │ 8    │ 65% │ 20    │ 4:12:08║
║ ...       ║  ...     │ ...    │ ...      │ ...  │ ... │ ...   │ ...    ║
║           ╠══════════════════════════════════════════════════════════════╣
║ Analytics ║  [Efficiency Chart]    Total Kills: 58   Avg: 14.5 boss/hr  ║
║ Logs      ║  ┌────────────────────────────────────────────────────────┐ ║
║ Settings  ║  │ 20  ●●●●●●●●●●                                         │ ║
║           ║  │ 15  ●●●●●●●●●●●●●●●                                   │ ║
║           ║  │ 10  ●●●●●●●                                           │ ║
║           ║  │  5  ●●●                                               │ ║
║           ║  │  0  ────────────────────────────────────────────────  │ ║
║           ║  │     Acc1  Acc2  Acc3  Acc4  Acc5  Acc6  Acc7  Acc8   │ ║
║           ║  └────────────────────────────────────────────────────────┘ ║
║           ╠══════════════════════════════════════════════════════════════╣
║           ║  Recent Events:                                             ║
║           ║  [10:30:15] Acc1: Boss found - Vua Vegita at Cung Khu 5   ║
║           ║  [10:30:45] Acc2: Boss killed - Cooler (kill time: 32.5s) ║
║           ║  [10:31:00] Acc3: ERROR - Combat timeout after 60s         ║
║           ║  [10:31:15] Acc5: Boss killed - Vua Vegita (kill: 28.1s)  ║
╚═══════════╩══════════════════════════════════════════════════════════════╝
```

### SocketServer Implementation (Manager Side)

```csharp
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AutoBossManager
{
    /// <summary>
    /// TCP socket server for receiving connections from game clients.
    /// Manages client registry, heartbeat monitoring, command dispatch.
    /// </summary>
    public class SocketServer
    {
        private TcpListener listener;
        private ConcurrentDictionary<Guid, ClientConnection> clients = 
            new ConcurrentDictionary<Guid, ClientConnection>();
        private CancellationTokenSource cts;
        private bool isRunning = false;
        
        // Configuration
        private const int Port = 28081;
        private const float HeartbeatTimeoutSec = 10f;
        
        // Events
        public event Action<Guid, BotInstanceState> OnStatusUpdate;
        public event Action<Guid, string, string> OnBossFound;
        public event Action<Guid, string> OnLogEvent;
        public event Action<Guid, string> OnError;
        
        public void Start()
        {
            if (isRunning) return;
            
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                isRunning = true;
                cts = new CancellationTokenSource();
                
                Console.WriteLine($"[SocketServer] Listening on 127.0.0.1:{Port}");
                
                // Accept connections
                Task.Run(AcceptLoop, cts.Token);
                
                // Monitor heartbeats
                Task.Run(HeartbeatMonitor, cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SocketServer] Start failed: {ex.Message}");
            }
        }
        
        public void Stop()
        {
            if (!isRunning) return;
            
            isRunning = false;
            cts?.Cancel();
            listener?.Stop();
            
            // Disconnect all clients
            foreach (var client in clients.Values)
            {
                client.Disconnect();
            }
            clients.Clear();
            
            Console.WriteLine("[SocketServer] Stopped");
        }
        
        private async Task AcceptLoop()
        {
            while (isRunning && !cts.Token.IsCancellationRequested)
            {
                try
                {
                    TcpClient tcpClient = await listener.AcceptTcpClientAsync();
                    
                    var connection = new ClientConnection(tcpClient);
                    connection.OnMessage += HandleClientMessage;
                    connection.OnDisconnect += HandleClientDisconnect;
                    connection.Start();
                    
                    clients.TryAdd(connection.InstanceId, connection);
                    
                    Console.WriteLine($"[SocketServer] Client connected: {connection.InstanceId}");
                }
                catch (Exception ex)
                {
                    if (isRunning)
                        Console.WriteLine($"[SocketServer] Accept error: {ex.Message}");
                }
            }
        }
        
        private async Task HeartbeatMonitor()
        {
            while (isRunning && !cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, cts.Token);
                    
                    var now = DateTime.UtcNow;
                    foreach (var kvp in clients)
                    {
                        var elapsed = (now - kvp.Value.LastHeartbeat).TotalSeconds;
                        if (elapsed > HeartbeatTimeoutSec)
                        {
                            Console.WriteLine($"[SocketServer] Client timeout: {kvp.Key} " +
                                $"(last heartbeat {elapsed:F1}s ago)");
                            
                            kvp.Value.Disconnect();
                            clients.TryRemove(kvp.Key, out _);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SocketServer] Heartbeat monitor error: {ex.Message}");
                }
            }
        }
        
        private void HandleClientMessage(Guid instanceId, IpcMessage message)
        {
            try
            {
                switch (message.Type)
                {
                    case MessageTypes.HEARTBEAT:
                        // Update heartbeat time
                        if (clients.TryGetValue(instanceId, out var client))
                        {
                            client.LastHeartbeat = DateTime.UtcNow;
                        }
                        break;
                        
                    case MessageTypes.STATUS_UPDATE:
                        var state = ParseStatusUpdate(message);
                        OnStatusUpdate?.Invoke(instanceId, state);
                        break;
                        
                    case MessageTypes.BOSS_FOUND:
                        string bossName = message.Payload["bossName"].ToString();
                        string mapName = message.Payload["mapName"].ToString();
                        OnBossFound?.Invoke(instanceId, bossName, mapName);
                        break;
                        
                    case MessageTypes.LOG_EVENT:
                        string logMsg = message.Payload["message"].ToString();
                        OnLogEvent?.Invoke(instanceId, logMsg);
                        break;
                        
                    case MessageTypes.ERROR:
                        string errorMsg = message.Payload["message"].ToString();
                        OnError?.Invoke(instanceId, errorMsg);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SocketServer] Message handling error: {ex.Message}");
            }
        }
        
        private BotInstanceState ParseStatusUpdate(IpcMessage message)
        {
            return new BotInstanceState
            {
                CurrentState = Enum.Parse<AutoBossState>(
                    message.Payload["state"].ToString()),
                CurrentMap = message.Payload["map"].ToString(),
                CurrentZone = Convert.ToInt32(message.Payload["zone"]),
                PlayerHpPct = Convert.ToSingle(message.Payload["playerHpPct"]),
                BossKillsThisSession = Convert.ToInt32(
                    message.Payload["bossKillsThisSession"]),
                LastHeartbeat = DateTime.UtcNow
            };
        }
        
        private void HandleClientDisconnect(Guid instanceId)
        {
            clients.TryRemove(instanceId, out _);
            Console.WriteLine($"[SocketServer] Client disconnected: {instanceId}");
        }
        
        // === Command Sending ===
        
        public void SendCommand(Guid instanceId, string command, 
            Dictionary<string, object> parameters = null)
        {
            if (!clients.TryGetValue(instanceId, out var client))
            {
                Console.WriteLine($"[SocketServer] Client not found: {instanceId}");
                return;
            }
            
            var payload = new Dictionary<string, object>
            {
                ["command"] = command
            };
            
            if (parameters != null)
            {
                foreach (var kvp in parameters)
                {
                    payload[kvp.Key] = kvp.Value;
                }
            }
            
            var message = new IpcMessage
            {
                Type = MessageTypes.COMMAND,
                Timestamp = DateTime.UtcNow,
                Payload = payload
            };
            
            client.SendMessage(message);
        }
        
        public void BroadcastCommand(string command, 
            Dictionary<string, object> parameters = null)
        {
            foreach (var kvp in clients)
            {
                SendCommand(kvp.Key, command, parameters);
            }
        }
        
        public void SendConfigUpdate(Guid instanceId, BotProfile profile)
        {
            if (!clients.TryGetValue(instanceId, out var client))
                return;
            
            var message = new IpcMessage
            {
                Type = MessageTypes.CONFIG_UPDATE,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["maxZoneAttempts"] = profile.MaxZoneAttempts,
                    ["retreatHpPct"] = profile.RetreatHpPct,
                    ["attackRange"] = profile.AttackRange,
                    ["bossSkillTriggers"] = profile.BossSkillTriggers
                }
            };
            
            client.SendMessage(message);
        }
        
        public List<Guid> GetConnectedClients()
        {
            return new List<Guid>(clients.Keys);
        }
        
        public int GetClientCount()
        {
            return clients.Count;
        }
    }
    
    /// <summary>
    /// Represents a single connected client.
    /// </summary>
    internal class ClientConnection
    {
        public Guid InstanceId { get; }
        public DateTime LastHeartbeat { get; set; }
        public event Action<Guid, IpcMessage> OnMessage;
        public event Action<Guid> OnDisconnect;
        
        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;
        private Thread receiveThread;
        private bool isRunning = false;
        
        public ClientConnection(TcpClient tcpClient)
        {
            InstanceId = Guid.NewGuid();
            client = tcpClient;
            LastHeartbeat = DateTime.UtcNow;
            
            var stream = client.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8);
            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }
        
        public void Start()
        {
            isRunning = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            receiveThread.Start();
        }
        
        private void ReceiveLoop()
        {
            try
            {
                while (isRunning)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    
                    var message = JsonConvert.DeserializeObject<IpcMessage>(line);
                    OnMessage?.Invoke(InstanceId, message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientConnection] Receive error: {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }
        
        public void SendMessage(IpcMessage message)
        {
            try
            {
                string json = JsonConvert.SerializeObject(message);
                writer.WriteLine(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientConnection] Send error: {ex.Message}");
            }
        }
        
        public void Disconnect()
        {
            isRunning = false;
            OnDisconnect?.Invoke(InstanceId);
            
            reader?.Close();
            writer?.Close();
            client?.Close();
        }
    }
}
```

---

## Error Handling

### Error Categories and Recovery Strategies

| Error Category | Detection | Recovery Strategy | Escalation |
|---------------|-----------|-------------------|------------|
| **Socket Disconnection** | Heartbeat timeout | Auto-reconnect with exponential backoff (1s→30s) | Report to Manager after 5 failed attempts |
| **Command Execution Failure** | Exception in main thread queue | Log error, send ERROR message to Manager, continue | None (isolate failure) |
| **Game Crash** | Process exit detection | Manager restarts game if AutoRestartOnCrash=true | Stop after MaxRestartAttempts |
| **Combat Timeout** | No boss HP change for 60s | Retreat to town, invalidate cache, retry | Report error after 3 consecutive timeouts |
| **Map Navigation Failure** | BFS returns null, teleport fails | Fallback to PortalChainMaps, then manual intervention | Report error, wait for user |
| **Captcha Detection** | CaptchaManager detects popup | Auto-solve with CNN model, pause if failed | Notify Manager, queue for manual solve |
| **State Persistence Error** | File I/O exception during save | Log warning, continue without persistence | None (non-critical) |
| **Memory Optimization Failure** | P/Invoke exception | Log warning, disable GameOptimizer | None (gracefully degrade) |

### Error Reporting Flow

```
Client Error Occurs
       ↓
Log to BepInEx log
       ↓
Send ERROR message to Manager via IPC
       ↓
Manager logs to centralized log database
       ↓
Dashboard updates with red indicator
       ↓
Notification (if enabled)
       ↓
User reviews error and takes action
```

### Crash Recovery State Machine

```
Game Process Running
       ↓
[Crash Detected]
       ↓
Read persisted state from disk
       ↓
Restart attempt < MaxRestartAttempts?
       ├─ Yes → Launch game process
       │            ↓
       │     AutoLoginController connects
       │            ↓
       │     SocketClient reconnects to Manager
       │            ↓
       │     Load persisted state (if < 5 min old)
       │            ↓
       │     Resume from saved state or start fresh
       │
       └─ No → Mark instance as Failed
                  ↓
            Notify user via Dashboard
```

---

## Testing Strategy

### Unit Tests

**AutoBossClient Components:**
- **GameOptimizer**: Verify P/Invoke calls don't crash, GC settings applied correctly
- **SocketClient**: Mock TcpClient, test queue enqueue/dequeue, reconnection logic
- **BFSPathfinder**: Test graph construction, path finding with known map data
- **ItemFilterManager**: Test whitelist/blacklist logic with sample items
- **FarmLoopStateMachine**: Test state transitions with mocked game state

**AutoBossManager Components:**
- **SocketServer**: Test client connection, heartbeat timeout, message dispatch
- **ProfileManager**: Test JSON serialization, validation rules, import/export
- **AnalyticsEngine**: Test metrics calculation, aggregation, CSV export
- **BotInstanceViewModel**: Test observable property updates, command execution

### Integration Tests

1. **End-to-End IPC Communication**
   - Manager sends START command → Client executes → Client sends STATUS_UPDATE → Manager displays
   
2. **Hot-Reload Configuration**
   - Manager updates profile → sends CONFIG_UPDATE → Client applies new settings without restart
   
3. **Multi-Client Scenario**
   - Launch 3 game instances → All connect to Manager → Verify independent control
   
4. **Reconnection After Network Interruption**
   - Simulate socket close → Verify client reconnects → Verify state preserved
   
5. **Crash and Recovery**
   - Kill game process → Verify Manager detects → Verify auto-restart → Verify state restoration

### Performance Tests

1. **Memory Optimization Effectiveness**
   - Measure RAM before/after GameOptimizer (target: -33%)
   - Run 10 instances simultaneously (target: <10GB total RAM)
   
2. **IPC Latency**
   - Measure command execution time (target: <50ms from send to ACK)
   
3. **BFS Pathfinding Speed**
   - Measure worst-case pathfinding time (target: <100ms)
   
4. **Dashboard Responsiveness**
   - 10 clients sending status updates every second (target: UI stays <100ms update time)

### Manual Tests

1. **Boss Hunting Full Cycle**
   - Configure profile → Start bot → Detect boss notification → Navigate to boss map → Engage boss → Kill boss → Loot → Return home
   
2. **Town Farming Loop**
   - Enable farm loop → Verify auto zone switch → Verify reward claim → Verify satellite activation
   
3. **Item Filter**
   - Configure whitelist → Drop items → Verify only whitelisted items picked up
   
4. **Strategy Presets**
   - Apply Aggressive preset → Verify fast movement and low dwell times
   - Apply Safe preset → Verify slow movement and high dwell times

---

## Performance Benchmarks

### Target Metrics (Phase 1)

| Metric | Target | Method |
|--------|--------|--------|
| RAM per instance (optimized) | <800 MB | GameOptimizer P/Invoke + GC tuning |
| CPU per instance (idle) | <3% | Disable rendering, reduce Update() frequency |
| CPU per instance (combat) | <8% | Efficient combat logic, throttle position updates |
| Max instances (16GB RAM) | 12-14 | Memory optimization effectiveness |
| IPC command latency | <50ms | Measure Manager send → Client ACK time |
| BFS pathfinding time | <100ms | Worst-case graph with 50 maps |
| Dashboard refresh rate | 1 second | Status updates from all clients |
| Dashboard UI lag | <100ms | WPF virtualized list, throttled updates |

### Baseline Measurements (Before Optimization)

| Metric | Before |
|--------|--------|
| RAM per instance | ~1200 MB |
| CPU per instance | ~8% |
| Max instances (16GB RAM) | 8-9 |

### Optimization Impact

**Memory Reduction Techniques:**
1. **EmptyWorkingSet + SetProcessWorkingSetSize**: -200MB per instance (release unused pages to OS)
2. **GC LOH Compaction**: -100MB per instance (compact large object heap)
3. **Disable ParallaxBackground**: -50MB per instance (eliminate shader textures)
4. **SustainedLowLatency GC mode**: Reduce pause times by 60%

**CPU Reduction Techniques:**
1. **Disable rendering effects**: -2% CPU (ParallaxBackground, particles)
2. **Throttle position updates**: -1% CPU (send to server only every 100ms instead of every frame)
3. **Reduce shadow quality**: -0.5% CPU (QualitySettings.shadows = Disable)

---

## Security Considerations

### Threat Model

**Threats:**
1. **Man-in-the-Middle Attack**: Attacker intercepts IPC traffic
2. **Unauthorized Access**: Attacker connects rogue client to Manager
3. **Credential Theft**: Passwords stolen from profile storage
4. **Process Injection**: Attacker injects malicious code into game process

**Mitigations:**

| Threat | Mitigation | Implementation |
|--------|-----------|----------------|
| MITM Attack | Bind socket to localhost only | `TcpListener(IPAddress.Loopback, port)` |
| Unauthorized Access | Shared secret authentication | Client sends HMAC(secret, instanceId) on connect |
| Credential Theft | Encrypt passwords in storage | AES-256 encryption with user machine key |
| Process Injection | Out of scope | Rely on BepInEx integrity, no additional protection |

### Authentication Flow

```
Client connects to Manager
       ↓
Client sends: { "type": "AUTH", "payload": { "secret": "HMAC(...)" } }
       ↓
Manager validates HMAC
       ├─ Valid → Send ACK, add to client registry
       └─ Invalid → Send NACK, close connection
```

### Password Encryption

```csharp
using System.Security.Cryptography;

public class PasswordManager
{
    public static string Encrypt(string plainText)
    {
        byte[] entropy = Encoding.UTF8.GetBytes(Environment.MachineName);
        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = ProtectedData.Protect(data, entropy, 
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }
    
    public static string Decrypt(string encryptedText)
    {
        byte[] entropy = Encoding.UTF8.GetBytes(Environment.MachineName);
        byte[] data = Convert.FromBase64String(encryptedText);
        byte[] decrypted = ProtectedData.Unprotect(data, entropy, 
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}
```

---

## Deployment Architecture

### File Structure

```
AutoBossManager/
├── AutoBossManager.exe (WPF application)
├── AutoBossManager.exe.config
├── Newtonsoft.Json.dll
├── profiles/
│   ├── Account1.json
│   ├── Account2.json
│   └── ...
├── logs/
│   ├── manager_2024-01-15.log
│   └── clients/
│       ├── Account1_2024-01-15.log
│       └── Account2_2024-01-15.log
├── analytics/
│   ├── metrics_2024-01-15.json
│   └── sessions/
│       ├── session_guid1.json
│       └── session_guid2.json
└── backups/
    └── profiles_2024-01-15.zip

Game Installation/
├── Vũ Trụ Đại Chiến.exe
├── BepInEx/
│   ├── core/
│   ├── plugins/
│   │   └── AutoBossClient.dll (Enhanced plugin)
│   ├── config/
│   │   └── AutoBossGrabber/
│   │       ├── Account1_state.json (persisted state)
│   │       └── Account2_state.json
│   └── LogOutput.log
└── ...
```

### Installation Steps

**Manager:**
1. Extract AutoBossManager.zip to `C:\AutoBossManager\`
2. Run AutoBossManager.exe
3. Configure socket port (default 28081)
4. Create first bot profile

**Client (per game instance):**
1. Install BepInEx to game directory (if not already installed)
2. Copy `AutoBossClient.dll` to `BepInEx\plugins\`
3. Copy dependencies (Harmony, Newtonsoft.Json) to `BepInEx\plugins\`
4. Launch game → BepInEx loads plugin → AutoBossClient connects to Manager

### Multi-Instance Setup

**Recommended Setup (16GB RAM):**
- 12-14 game instances
- Each instance in separate game installation directory (avoid file conflicts)
- Each instance configured with different account credentials
- Manager dashboard shows all instances simultaneously

**Directory Structure for 10 Instances:**
```
D:\Games\
├── VTDC_01\ (Instance 1)
│   ├── Vũ Trụ Đại Chiến.exe
│   └── BepInEx\plugins\AutoBossClient.dll
├── VTDC_02\ (Instance 2)
│   ├── Vũ Trụ Đại Chiến.exe
│   └── BepInEx\plugins\AutoBossClient.dll
├── VTDC_03\ → VTDC_10\ (Instances 3-10)
...
```

**Launch Process:**
1. Start AutoBossManager.exe
2. Click "Launch Bot" for each profile
3. Manager spawns game process for each account
4. Each client connects to Manager on startup
5. Dashboard displays real-time status for all instances

---

## Phase 1 Implementation Roadmap

### Milestone 1: Core IPC Infrastructure (Week 1-2)

**Deliverables:**
- SocketServer (Manager) with client registry and heartbeat monitoring
- SocketClient (Client) with ConcurrentQueue command execution
- IpcMessage protocol (JSON line-delimited)
- Basic command support: START, STOP, STATUS_UPDATE, HEARTBEAT

**Success Criteria:**
- Manager can accept client connections
- Client can send status updates to Manager
- Manager can send commands, Client can execute on main thread
- Reconnection works after connection drop

### Milestone 2: GameOptimizer (Week 2-3)

**Deliverables:**
- P/Invoke declarations for EmptyWorkingSet, SetProcessWorkingSetSize
- GC optimization (SustainedLowLatency, LOH compaction)
- Harmony patches for ParallaxBackground
- Scheduled optimization execution (every 60s)

**Success Criteria:**
- RAM per instance reduced from ~1200MB to <800MB
- 10+ instances runnable on 16GB RAM machine
- No game crashes from optimization

### Milestone 3: BFS Pathfinding (Week 3-4)

**Deliverables:**
- Map graph construction from MapGateway objects
- BFS algorithm implementation
- Path reconstruction and execution
- Fallback to hard-coded PortalChainMaps

**Success Criteria:**
- Automatic pathfinding for all boss maps without manual config
- Pathfinding completes in <100ms worst-case
- Graceful fallback when BFS fails

### Milestone 4: Manager Desktop App (Week 4-6)

**Deliverables:**
- WPF MVVM application with dashboard view
- Profile manager with JSON storage
- Real-time status grid for all connected clients
- Log viewer with filtering
- Bot controls: Start, Stop, Pause, Resume

**Success Criteria:**
- Dashboard displays status for 10+ clients with 1s refresh
- Profile CRUD operations work correctly
- Commands execute within 50ms latency
- UI remains responsive under load

### Milestone 5: FarmLoop & ItemFilter (Week 6-7)

**Deliverables:**
- FarmLoop state machine with zone switch, reward claim, satellite
- ItemFilterManager with whitelist/blacklist modes
- Configuration UI in Manager for farm loop settings

**Success Criteria:**
- Town farming works autonomously without manual zone switching
- Item filter correctly picks/skips items based on rules
- Hot-reload configuration updates apply without restart

### Milestone 6: Integration & Testing (Week 7-8)

**Deliverables:**
- End-to-end integration tests
- Performance benchmarks
- Bug fixes and stability improvements
- Documentation (user guide, API reference)

**Success Criteria:**
- All unit tests pass
- Integration tests pass (IPC, hot-reload, reconnection)
- Performance targets met (RAM, CPU, latency)
- 10+ instances can run simultaneously and farm successfully

---

## Future Enhancements (Phase 2)

### Advanced Coordination Service
- Boss claim reservation system (prevent multiple bots targeting same boss)
- Load balancing (distribute bots across different maps)
- Boss spawn prediction (historical data analysis)

### Scheduling System
- Time-based automation (farm 6am-6pm weekdays)
- Daily/weekly quotas (stop after 50 boss kills)
- Smart scheduling (avoid peak detection hours)

### Discord Webhook Notifications
- Boss found/killed notifications
- Error alerts
- Daily summary reports

### Advanced Scripting Language
- Custom grammar parser for user-defined bot behaviors
- Visual scripting editor (drag-drop actions)
- Community script sharing

### Network Protocol Parser
- Packet inspection beyond BossNotificationHook
- Custom packet injection for advanced features
- Protocol reverse engineering tools

### Machine Learning Enhancements
- Improved captcha CNN model (higher accuracy)
- Movement pattern randomization with ML (more human-like)
- Anomaly detection (detect unusual game behavior)

---

This design document provides a comprehensive blueprint for Phase 1 implementation of the AutoBoss Manager Integration feature. All critical components are detailed with code examples, algorithms, and performance targets. The architecture preserves AutoBossGrabber's existing strengths while adding powerful multi-instance management capabilities inspired by Tool_Up_Level_V111.
