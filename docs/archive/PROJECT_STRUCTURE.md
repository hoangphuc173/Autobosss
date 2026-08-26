# AutoBoss System - Project Structure

## Overview

This document describes the project structure for the AutoBoss Manager Integration system, which transforms AutoBossGrabber from a standalone BepInEx plugin into a centralized multi-account bot management system.

## Solution Structure

**AutoBossSystem.sln** - Master solution file containing all three projects:

### 1. AutoBossShared (Class Library - .NET 6.0)
**Location:** `AutoBossShared/`  
**Purpose:** Shared data models and message protocol used by both Manager and Client  
**Dependencies:**
- Newtonsoft.Json 13.0.4

**Key Files:**
- `IpcMessage.cs` - Base message structure for IPC communication
  - `MessageTypes` - Constants for message type identifiers
  - `Commands` - Constants for remote control commands
- `Enums.cs` - Shared enumerations
  - `StrategyPreset` - Bot behavior presets (Aggressive, Balanced, Safe, Custom)
  - `ItemFilterMode` - Item pickup filter modes
  - `ConnectionStatus` - Bot connection states
  - `AutoBossState` - State machine states
- `BotProfile.cs` - Complete bot configuration model
  - Contains all settings: credentials, boss targets, behavior parameters, skill triggers, etc.
  - Serialized to JSON for persistent storage
- `BotInstanceState.cs` - Runtime state of a bot instance
  - Real-time metrics: HP, position, boss kills, errors, etc.
  - Sent from Client to Manager via status updates

### 2. AutoBossManager (WPF Application - .NET 6.0)
**Location:** `AutoBossManager/`  
**Purpose:** Centralized desktop application for managing multiple game instances  
**Dependencies:**
- Newtonsoft.Json 13.0.4
- AutoBossShared (project reference)

**Status:** ✅ Builds successfully  
**Next Steps (Phase 1):**
- Implement SocketServer for IPC communication
- Create Dashboard UI (WPF)
- Implement ProfileManager for bot configuration
- Add AnalyticsEngine for metrics tracking

### 3. AutoBossClient (BepInEx Plugin - .NET 6.0)
**Location:** `AutoBossGrabber/source/`  
**Purpose:** Enhanced BepInEx plugin with IPC capabilities for remote control  
**Dependencies:**
- BepInEx 6 IL2CPP (Unity mod framework)
- Unity Engine DLLs (game-specific)
- 0Harmony (for patching game code)
- Il2CppInterop (for C# ↔ IL2CPP interop)
- Newtonsoft.Json 13.0.4
- AutoBossShared (project reference)

**Status:** ⚠️ Requires BepInEx/Unity DLLs from game installation to compile  
**Existing Components (Preserved):**
- `AutoBossRunner` - Main state machine (8 states)
- `BossDetector` - 3-layer boss detection (notification, passive scan, cache)
- `BossSkillManager` - HP-based skill triggers
- `CaptchaManager` - AI-powered captcha solving
- `MapTransporter` - Portal navigation
- `ZoneSwitcher` - Zone scanning loop
- `AutoPickupLite` - Item looting
- `GameAPI.cs` - Reflection wrapper for game types

**New Components (Phase 1):**
- `SocketClient` - TCP client with thread-safe command execution
- `GameOptimizer` - Memory/CPU optimization for multi-instance support
- `BFSPathfinder` - Smart pathfinding for map navigation
- `FarmLoopStateMachine` - Town farming automation
- `ItemFilterManager` - Intelligent item pickup filtering

## Data Flow

```
┌────────────────┐                           ┌────────────────┐
│ AutoBossManager│ ◄──── TCP Socket ──────► │AutoBossClient  │
│    (Desktop)   │      127.0.0.1:28081      │  (In-Game)     │
└────────────────┘                           └────────────────┘
        │                                             │
        │ JSON Profiles                               │ Reflection
        ▼                                             ▼
   AppData/                                      Game Memory
   profiles/                                     (Unity/IL2CPP)
```

### IPC Message Protocol

**Manager → Client:**
- `COMMAND` - Remote control (start, stop, pause, teleport, etc.)
- `CONFIG_UPDATE` - Hot-reload configuration changes
- `SHUTDOWN` - Graceful shutdown request

**Client → Manager:**
- `HEARTBEAT` - Alive signal (every 3 seconds)
- `STATUS_UPDATE` - Real-time state (HP, position, boss kills, etc.)
- `LOG_EVENT` - Important log messages
- `BOSS_FOUND` - Boss detection notification
- `BOSS_KILLED` - Boss kill confirmation
- `CAPTCHA_DETECTED` - Captcha popup detected
- `ERROR` - Error condition

**Bidirectional:**
- `ACK` - Command acknowledgment

## Build Instructions

### Prerequisites
- .NET 6.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- BepInEx 6 IL2CPP (for AutoBossClient)
- Game installation with Unity interop DLLs (for AutoBossClient)

### Building AutoBossShared
```bash
cd AutoBossShared
dotnet build
```

### Building AutoBossManager
```bash
cd AutoBossManager
dotnet build
```

### Building AutoBossClient
```bash
cd AutoBossGrabber/source
# Ensure BepInEx DLL paths in .csproj are correct
dotnet build
```

### Building Entire Solution
```bash
dotnet build AutoBossSystem.sln
```

## Configuration

### BepInEx DLL References
The AutoBossClient project requires the following DLLs from a BepInEx 6 IL2CPP installation. Update the paths in `AutoBossGrabber.csproj` to match your game installation:

```xml
<HintPath>..\..\Tool_Om_Boss\BepInEx\core\BepInEx.Core.dll</HintPath>
```

Change to your actual game path, for example:
```xml
<HintPath>C:\Games\YourGame\BepInEx\core\BepInEx.Core.dll</HintPath>
```

## Next Steps (Task 2)

Implement IPC message protocol and serialization:
- Create message validation logic
- Add serialization/deserialization helpers
- Write unit tests for message round-trip
- Test JSON line-delimited protocol

## Version Information

- **Created:** 2026-08-22
- **Framework:** .NET 6.0
- **C# Version:** Latest (10.0)
- **JSON Library:** Newtonsoft.Json 13.0.4
- **UI Framework:** WPF (Windows Presentation Foundation)
