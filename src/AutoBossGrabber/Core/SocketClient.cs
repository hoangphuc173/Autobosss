using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using AutoBossShared;
using System.Linq;
using System.Collections;

namespace AutoBossGrabber;

/// <summary>
/// TCP socket client with thread-safe command execution on Unity main thread.
/// Connects to AutoBossManager at 127.0.0.1:28081 for remote control and monitoring.
/// 
/// Architecture:
/// - Background thread: ReceiveLoop reads line-delimited JSON messages
/// - ConcurrentQueue: Thread-safe command buffering
/// - Main thread (Update): Dequeues and executes commands safely
/// - Auto-reconnect: Exponential backoff (1s, 2s, 4s, 8s, max 30s)
/// - Heartbeat: Sent every 3 seconds to Manager
/// 
/// Pattern from Tool_Up_Level_V111 SocketClient.cs
/// Requirements: REQ 3.1-3.8, 6.1-6.7, 13.1-13.8
/// </summary>
public class SocketClient : MonoBehaviour
{
    // === Configuration (Subtask 4.1) ===
    
    // === Connection State (Subtask 4.1) ===
    private TcpClient client;
    private StreamReader reader;
    private StreamWriter writer;
    private bool isConnected = false;
    private bool isShuttingDown = false;
    
    // === Thread-Safe Command Queue (Subtask 4.1) ===
    private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();
    
    // === Background Threads (Subtask 4.1) ===
    private Thread receiveThread;
    private Task heartbeatTask;
    private CancellationTokenSource cts;
    
    // === Reconnection Logic (Subtask 4.1) ===
    private int reconnectAttempt = 0;
    private float nextReconnectTime = 0f;
    private float lastReconnectAttemptTime = 0f;
    
    // === BFS Pathfinder (Phase 3) ===
    private BFSPathfinder _pathfinder;
    private NavigationController _navigationController;
    private System.Collections.Generic.List<int> _currentNavigationPath;
    
        // === Statistics ===
    private int commandsExecuted = 0;
    private int errorsCount = 0;
    private DateTime sessionStartTime = DateTime.UtcNow;
    private string accountName = "Unknown";
    
    // === Unity Lifecycle ===
    
    void Start()
    {
        try
        {
            Plugin.Log.LogInfo("[SocketClient] Initializing...");
            
            // Read account name from command line arguments
            string[] args = System.Environment.GetCommandLineArgs();
            int idx = Array.IndexOf(args, "--account");
            if (idx >= 0 && idx + 1 < args.Length)
            {
                accountName = args[idx + 1];
            }
            Plugin.Log.LogInfo($"[SocketClient] AccountName set to: {accountName}");
            
            cts = new CancellationTokenSource();
            
            // Attempt initial connection
            ConnectToManager();
            
            // Initialize BFS Pathfinder (Phase 3)
            _pathfinder = new BFSPathfinder();
            _navigationController = null; // Lazy init after first path computation
            Plugin.Log.LogInfo("[SocketClient] BFS Pathfinder initialized");
            
                        // Start heartbeat sender
            StartHeartbeat();
            
            Plugin.Log.LogInfo("[SocketClient] Initialization complete");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[SocketClient] Start failed: {ex.Message}");
        }
    }
    
    void OnDestroy()
    {
        try
        {
            Plugin.Log.LogInfo("[SocketClient] Shutting down...");
            
            isShuttingDown = true;
            cts?.Cancel();
            Disconnect();
            
            // Cho heartbeat task thoat roi moi dispose CTS (tranh ObjectDisposedException).
            try { heartbeatTask?.Wait(500); } catch { }
            try { cts?.Dispose(); } catch { }
            
            Plugin.Log.LogInfo($"[SocketClient] Shutdown complete. Stats: Commands={commandsExecuted}, Errors={errorsCount}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] OnDestroy error: {ex.Message}");
        }
    }
    
    // === Main Thread Command Processing (Subtask 4.4) ===
    
    private float _nextStatusUpdateTime = 0f;
    
    void Update()
    {
        if (isShuttingDown) return;
        
        // Process all queued commands on Unity main thread
        while (mainThreadQueue.TryDequeue(out Action action))
        {
            try
            {
                action.Invoke();
                commandsExecuted++;
            }
            catch (Exception ex)
            {
                errorsCount++;
                Plugin.Log.LogError($"[SocketClient] Command execution failed: {ex.Message}\n{ex.StackTrace}");
                SendError($"Command execution failed: {ex.Message}");
            }
        }

        // STATUS_UPDATE dinh ky moi 5s de Manager cap nhat dashboard realtime
        // (truoc day chi gui 1 lan luc connect -> UI lac hoi).
        if (isConnected && Time.time >= _nextStatusUpdateTime)
        {
            _nextStatusUpdateTime = Time.time + 5f;
            SendStatusUpdate();
        }
        
        // Check if reconnection is needed
        if (!isConnected && !isShuttingDown && Time.time >= nextReconnectTime)
        {
            ConnectToManager();
        }
    }
    
    // === Connection Management (Subtask 4.2) ===
    
    private void ConnectToManager()
    {
        try
        {
            lastReconnectAttemptTime = Time.time;
            
            Plugin.Log.LogInfo($"[SocketClient] Connecting to Manager at {IpcConfig.ServerHost}:{IpcConfig.ServerPort}...");
            
            client = new TcpClient();
            client.Connect(IpcConfig.ServerHost, IpcConfig.ServerPort);
            
            NetworkStream stream = client.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8);
            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            
            isConnected = true;
            reconnectAttempt = 0;
            
            Plugin.Log.LogInfo($"[SocketClient] Connected to Manager successfully");
            
            // Start background receive thread
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            receiveThread.Start();
            
            // Send initial status update
            SendStatusUpdate();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] Connection failed: {ex.Message}");
            Disconnect();
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
            
            if (!isShuttingDown)
            {
                Plugin.Log.LogInfo("[SocketClient] Disconnected from Manager");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] Disconnect error: {ex.Message}");
        }
    }
    
    private void ScheduleReconnect()
    {
        reconnectAttempt++;
        
        // Exponential backoff: 1s, 2s, 4s, 8s, 16s, max 30s
        float delay = Mathf.Min(Mathf.Pow(2, reconnectAttempt - 1), 30f);
        nextReconnectTime = Time.time + delay;
        
        Plugin.Log.LogInfo($"[SocketClient] Reconnect scheduled in {delay:F1}s (attempt #{reconnectAttempt})");
    }
    
    // === Background Receive Loop (Subtask 4.3) ===
    
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
                
                // Parse JSON message
                IpcMessage message = JsonConvert.DeserializeObject<IpcMessage>(line);
                
                if (message == null)
                {
                    Plugin.Log.LogWarning("[SocketClient] Received invalid message (null after deserialization)");
                    continue;
                }
                
                // Enqueue message handler to main thread
                mainThreadQueue.Enqueue(() => HandleMessage(message));
            }
        }
        catch (IOException ioEx)
        {
            if (!isShuttingDown)
            {
                Plugin.Log.LogWarning($"[SocketClient] Connection lost (IO error): {ioEx.Message}");
            }
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
    
    // === Message Handling (Subtask 4.4, 4.5) ===
    
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
            
            // Send ACK for all messages
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
        if (message.Payload == null || !message.Payload.TryGetValue("command", out object cmdObj))
        {
            Plugin.Log.LogWarning("[SocketClient] COMMAND message missing 'command' field");
            return;
        }
        
        string command = cmdObj.ToString();
        Plugin.Log.LogInfo($"[SocketClient] Executing command: {command}");
        
        var runner = Plugin.Instance.Runner;
        if (runner == null)
        {
            Plugin.Log.LogWarning("[SocketClient] AutoBossRunner not available");
            SendError("AutoBossRunner not initialized");
            return;
        }
        
        switch (command)
        {
            case Commands.START_FARMING:
                runner.Config.Enabled = true;
                Plugin.Log.LogInfo("[SocketClient] Farming started");
                break;
                
            case Commands.STOP_FARMING:
                runner.Config.Enabled = false;
                runner.State = AutoBossState.Idle;
                Plugin.Log.LogInfo("[SocketClient] Farming stopped");
                break;
                
            case Commands.PAUSE:
                runner.Config.Enabled = false;
                Plugin.Log.LogInfo("[SocketClient] Bot paused");
                break;
                
            case Commands.RESUME:
                runner.Config.Enabled = true;
                Plugin.Log.LogInfo("[SocketClient] Bot resumed");
                break;
                
            case Commands.RETURN_TO_TOWN:
                if (runner.State != AutoBossState.Idle)
                {
                    runner.State = AutoBossState.TeleportHome;
                    Plugin.Log.LogInfo("[SocketClient] Returning to town");
                }
                break;
                
            case Commands.TELEPORT_TO_MAP:
                if (message.Payload.TryGetValue("targetMap", out object mapObj))
                {
                    string targetMap = mapObj.ToString();
                    Plugin.Log.LogInfo($"[SocketClient] TELEPORT_TO_MAP command received: {targetMap}");

                    if (!RequestNavigationTo(targetMap))
                    {
                        SendError($"Map unreachable or unknown: {targetMap}");
                    }
                }
                else
                {
                    Plugin.Log.LogWarning("[SocketClient] TELEPORT_TO_MAP missing 'targetMap' parameter");
                    SendError("Missing required parameter: targetMap");
                }
                break;
                
            case Commands.SWITCH_ZONE:
                if (message.Payload.TryGetValue("zone", out object zoneObj))
                {
                    int zone = Convert.ToInt32(zoneObj);
                    Plugin.Log.LogInfo($"[SocketClient] SWITCH_ZONE -> zone {zone}");

                    // Dung ZoneSwitcher cua plugin: dat muc tieu khu, lan quet ke tiep
                    // cua runner (ZoneScanLoop) se mo panel va click dung Khu N do.
                    var runner2 = Plugin.Instance.Runner;
                    if (runner2 == null)
                    {
                        SendError("SWITCH_ZONE failed: AutoBossRunner not initialized");
                        break;
                    }

                    if (!runner2.Config.Enabled)
                    {
                        SendError("SWITCH_ZONE ignored: farming is not enabled");
                        break;
                    }

                    ZoneSwitcher.SetTargetZone(zone);
                    SendAck($"Switching to zone {zone}");
                }
                else
                {
                    Plugin.Log.LogWarning("[SocketClient] SWITCH_ZONE missing 'zone' parameter");
                    SendError("Missing required parameter: zone");
                }
                break;
                
            case Commands.INVALIDATE_CACHE:
                Plugin.Log.LogInfo("[SocketClient] Invalidating caches");
                _pathfinder?.InvalidateCache();
                SendAck("Cache invalidated successfully");
                break;
                
            case Commands.RELOAD_CONFIG:
                Plugin.Log.LogInfo("[SocketClient] Reloading config (not modifying Plugin.Config - read-only)");
                // Note: Plugin.Config property is read-only, so we can't directly reload it
                // Config changes should come through CONFIG_UPDATE messages
                Plugin.Log.LogWarning("[SocketClient] RELOAD_CONFIG: Use CONFIG_UPDATE message instead");
                break;
                
            default:
                Plugin.Log.LogWarning($"[SocketClient] Unknown command: {command}");
                SendError($"Unknown command: {command}");
                break;
        }
    }
    
    // === Hot-Reload Configuration (Subtask 4.6) ===
    
    private void HandleConfigUpdate(IpcMessage message)
    {
        try
        {
            Plugin.Log.LogInfo("[SocketClient] Processing config update...");
            
            if (message.Payload == null)
            {
                Plugin.Log.LogWarning("[SocketClient] CONFIG_UPDATE has null payload");
                return;
            }
            
            var config = Plugin.Instance.Config;
            int updatesApplied = 0;
            
            // Update numeric parameters
            if (message.Payload.TryGetValue("maxZoneAttempts", out object maxZone))
            {
                config.MaxZoneAttempts = Convert.ToInt32(maxZone);
                updatesApplied++;
            }
            
            if (message.Payload.TryGetValue("retreatHpPct", out object retreatHp))
            {
                config.RetreatHpPct = Convert.ToSingle(retreatHp);
                updatesApplied++;
            }
            
            if (message.Payload.TryGetValue("attackRange", out object attackRange))
            {
                config.AttackRange = Convert.ToSingle(attackRange);
                updatesApplied++;
            }
            
            if (message.Payload.TryGetValue("combatTimeoutSec", out object combatTimeout))
            {
                config.CombatTimeoutSec = Convert.ToSingle(combatTimeout);
                updatesApplied++;
            }
            
            if (message.Payload.TryGetValue("lootRadius", out object lootRadius))
            {
                config.LootRadius = Convert.ToSingle(lootRadius);
                updatesApplied++;
            }
            
            // Update skill triggers (complex object)
            if (message.Payload.TryGetValue("bossSkillTriggers", out object skillsObj))
            {
                try
                {
                    string json = JsonConvert.SerializeObject(skillsObj);
                    var triggers = JsonConvert.DeserializeObject<System.Collections.Generic.List<SkillTrigger>>(json);
                    
                    if (triggers != null && config.BossSkillTriggers != null)
                    {
                        config.BossSkillTriggers.Clear();
                        config.BossSkillTriggers.AddRange(triggers);
                        updatesApplied++;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[SocketClient] Failed to parse bossSkillTriggers: {ex.Message}");
                }
            }
            
            // Update boss names
            if (message.Payload.TryGetValue("bossNames", out object bossNamesObj))
            {
                try
                {
                    string json = JsonConvert.SerializeObject(bossNamesObj);
                    var names = JsonConvert.DeserializeObject<System.Collections.Generic.List<string>>(json);
                    
                    if (names != null && config.BossNames != null)
                    {
                        config.BossNames.Clear();
                        config.BossNames.AddRange(names);
                        updatesApplied++;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[SocketClient] Failed to parse bossNames: {ex.Message}");
                }
            }

            // === Item filter hot-reload (task 14) - Manager gui tu ProfileManager ===
            ApplyItemFilterConfig(message, ref updatesApplied);

            // === Farm loop flags hot-reload (task 13.7) ===
            try
            {
                if (message.Payload.TryGetValue("enableAutoZoneSwitch", out object azs))
                {
                    config.EnableAutoZoneSwitch = Convert.ToBoolean(azs);
                    updatesApplied++;
                }
                if (message.Payload.TryGetValue("enableAutoReward", out object ar))
                {
                    config.EnableAutoReward = Convert.ToBoolean(ar);
                    updatesApplied++;
                }
                if (message.Payload.TryGetValue("enableAutoSatellite", out object sat))
                {
                    config.EnableAutoSatellite = Convert.ToBoolean(sat);
                    updatesApplied++;
                }

                // === Behavior randomization sync (task 19.3/20.3) ===
                if (message.Payload.TryGetValue("enableRandomization", out object er))
                {
                    config.EnableRandomization = Convert.ToBoolean(er);
                    updatesApplied++;
                }
                if (message.Payload.TryGetValue("randomizationIntensity", out object ri))
                {
                    config.RandomizationIntensity = Math.Clamp(Convert.ToInt32(ri), 0, 2);
                    updatesApplied++;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SocketClient] Failed to apply farm/randomization flags: {ex.Message}");
            }

            Plugin.Log.LogInfo($"[SocketClient] Config updated successfully ({updatesApplied} parameters changed)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[SocketClient] Config update failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Ap dung cau hinh bo loc vat pham tu CONFIG_UPDATE payload:
    /// itemFilterMode (string), itemFilterList (list), alwaysPickGems,
    /// alwaysPickQuestItems, minRarityToPickup (int).
    /// </summary>
    private static void ApplyItemFilterConfig(IpcMessage message, ref int updatesApplied)
    {
        try
        {
            bool hasAny =
                message.Payload.ContainsKey("itemFilterMode") ||
                message.Payload.ContainsKey("itemFilterList") ||
                message.Payload.ContainsKey("alwaysPickGems") ||
                message.Payload.ContainsKey("alwaysPickQuestItems") ||
                message.Payload.ContainsKey("minRarityToPickup");

            if (!hasAny) return;

            var mode = ItemFilterMode.Disabled;
            if (message.Payload.TryGetValue("itemFilterMode", out object modeObj))
            {
                Enum.TryParse(modeObj?.ToString(), true, out mode);
            }

            System.Collections.Generic.List<string> itemList = null;
            if (message.Payload.TryGetValue("itemFilterList", out object listObj))
            {
                string json = JsonConvert.SerializeObject(listObj);
                itemList = JsonConvert.DeserializeObject<System.Collections.Generic.List<string>>(json);
            }

            bool gems = message.Payload.TryGetValue("alwaysPickGems", out object g)
                ? Convert.ToBoolean(g) : true;
            bool quest = message.Payload.TryGetValue("alwaysPickQuestItems", out object q)
                ? Convert.ToBoolean(q) : true;
            int minRarity = message.Payload.TryGetValue("minRarityToPickup", out object r)
                ? Convert.ToInt32(r) : 0;

            ItemFilterManager.Instance.Configure(mode, itemList, gems, quest, minRarity);
            updatesApplied++;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] Failed to apply itemFilter config: {ex.Message}");
        }
    }
    
    private void HandleShutdown()
    {
        Plugin.Log.LogInfo("[SocketClient] Shutdown command received from Manager");
        
        // Stop the bot
        if (Plugin.Instance.Runner != null)
        {
            Plugin.Instance.Runner.Config.Enabled = false;
            Plugin.Instance.Runner.State = AutoBossState.Idle;
        }
        
        // Optionally quit the game (commented out for safety)
        // Application.Quit();
    }
    
    // === Message Sending (Subtask 4.7) ===
    
    private void WriteMessage(IpcMessage message)
    {
        if (!isConnected || writer == null)
        {
            return; // Silently skip if not connected
        }
        
        try
        {
            // Use Formatting.None overload to avoid IL2CPP stripping of single-arg overload
            string json = JsonConvert.SerializeObject(message, Formatting.None);
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
        try
        {
            var runner = Plugin.Instance.Runner;
            string stateStr = runner != null ? runner.State.ToString() : AutoBossState.Idle.ToString();
            string mapName = "";
            try { mapName = GameAPI.GetCurrentMapFromMiniMap(); } catch { }
            if (string.IsNullOrWhiteSpace(mapName))
                try { mapName = GameAPI.GetCurrentMapName() ?? "Unknown"; } catch { }
            if (string.IsNullOrWhiteSpace(mapName)) mapName = "Unknown";
            int zone = 0;
            try { zone = GameAPI.GetCurrentZoneIndexFromHUD(); } catch { }
            float hp = 100f;
            try { hp = GameAPI.GetPlayerHpPct(); } catch { }

            var message = new IpcMessage(MessageTypes.STATUS_UPDATE);
            message.Payload["state"] = stateStr;
            message.Payload["map"] = mapName;
            message.Payload["zone"] = zone;
            message.Payload["playerHpPct"] = hp;
            message.Payload["playerMpPct"] = 100.0f;
            message.Payload["bossKillsThisSession"] = 0;
            try
            {
                message.Payload["currentTarget"] = (runner != null && runner.Config.BossNames.Count > 0) ? runner.Config.BossNames[0] : "None";
            }
            catch { message.Payload["currentTarget"] = "None"; }
            message.Payload["sessionStartTime"] = sessionStartTime;
            message.Payload["accountName"] = accountName;
            
            WriteMessage(message);
            Plugin.Log.LogInfo($"[SocketClient] Sent STATUS_UPDATE map='{mapName}' zone={zone} state={stateStr}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] SendStatusUpdate failed: {ex.Message}");
        }
    }
    
    public void SendBossFound(string bossName, string mapName, string zoneName)
    {
        try
        {
            var message = new IpcMessage(MessageTypes.BOSS_FOUND);
            message.Payload["bossName"] = bossName;
            message.Payload["mapName"] = mapName;
            message.Payload["zoneName"] = zoneName;
            message.Payload["detectionMethod"] = "ServerNotification";
            
            WriteMessage(message);
            Plugin.Log.LogInfo($"[SocketClient] Sent BOSS_FOUND: {bossName} at {mapName} {zoneName}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] SendBossFound failed: {ex.Message}");
        }
    }
    
    /// <summary>Bao Manager biet bot gap captcha (task 23.1).</summary>
    public void SendCaptchaDetected()
    {
        try
        {
            var message = new IpcMessage(MessageTypes.CAPTCHA_DETECTED);
            message.Payload["timestamp"] = DateTime.UtcNow.ToString("o");
            WriteMessage(message);
            Plugin.Log.LogInfo("[SocketClient] Sent CAPTCHA_DETECTED");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] SendCaptchaDetected failed: {ex.Message}");
        }
    }

    public void SendBossKilled(string bossName, float killDurationSec)
    {
        try
        {
            var message = new IpcMessage(MessageTypes.BOSS_KILLED);
            message.Payload["bossName"] = bossName;
            message.Payload["killDurationSec"] = killDurationSec;
            message.Payload["timestamp"] = DateTime.UtcNow.ToString("o");
            
            WriteMessage(message);
            Plugin.Log.LogInfo($"[SocketClient] Sent BOSS_KILLED: {bossName} (duration: {killDurationSec:F1}s)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] SendBossKilled failed: {ex.Message}");
        }
    }
    
    public void SendLogEvent(string level, string logMessage)
    {
        try
        {
            var message = new IpcMessage(MessageTypes.LOG_EVENT);
            message.Payload["level"] = level;
            message.Payload["message"] = logMessage;
            
            WriteMessage(message);
        }
        catch
        {
            // Silently catch to avoid infinite recursion if logging fails
        }
    }
    
    public void SendError(string errorMessage)
    {
        try
        {
            var message = new IpcMessage(MessageTypes.ERROR);
            message.Payload["message"] = errorMessage;
            message.Payload["timestamp"] = DateTime.UtcNow.ToString("o");
            
            WriteMessage(message);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] SendError failed: {ex.Message}");
        }
    }
    
    private void SendAck(string acknowledgedType)
    {
        try
        {
            var message = new IpcMessage(MessageTypes.ACK);
            message.Payload["acknowledgedType"] = acknowledgedType;
            
            WriteMessage(message);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SocketClient] SendAck failed: {ex.Message}");
        }
    }
    
    // === Heartbeat Sender (Subtask 4.8) ===
    
    private void StartHeartbeat()
    {
        heartbeatTask = Task.Run(async () =>
        {
            Plugin.Log.LogInfo("[SocketClient] Heartbeat sender started");
            
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay((int)(IpcConfig.HeartbeatIntervalSec * 1000), cts.Token);
                    
                    if (isConnected && !isShuttingDown)
                    {
                        var message = new IpcMessage(MessageTypes.HEARTBEAT);
                        // Stats nhe: chi doc counter trong process, khong goi GameAPI
                        // tu background thread (Unity API khong thread-safe).
                        message.Payload["commandsExecuted"] = commandsExecuted;
                        message.Payload["errors"] = errorsCount;
                        message.Payload["uptimeSec"] = (DateTime.UtcNow - sessionStartTime).TotalSeconds;
                        WriteMessage(message);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[SocketClient] Heartbeat error: {ex.Message}");
                }
            }
            
            Plugin.Log.LogInfo("[SocketClient] Heartbeat sender stopped");
        }, cts.Token);
    }
    
    // === Public API ===
    
    public bool IsConnected()
    {
        return isConnected;
    }
    
    public int GetCommandsExecuted()
    {
        return commandsExecuted;
    }
    
    public int GetErrorsCount()
    {
        return errorsCount;
    }
    
    public int GetReconnectAttempt()
    {
        return reconnectAttempt;
    }
    
    public void ForceReconnect()
    {
        Plugin.Log.LogInfo("[SocketClient] Force reconnect requested");
        Disconnect();
        nextReconnectTime = Time.time; // Reconnect immediately
    }

    /// <summary>Coroutine wrapper for navigation execution</summary>
    private System.Collections.IEnumerator ExecuteNavigationPath()
    {
        return _navigationController.ExecutePath(_currentNavigationPath);
    }

    // === BFS navigation public API (task 12.2) ===

    /// <summary>
    /// Tinh duong BFS tu map hien tai den targetMapName va bat dau di chuyen qua portal.
    /// PHAI goi tren Unity main thread (StartCoroutine).
    /// Tra ve true neu co duong di va navigation da bat dau; false khi khong tim duoc
    /// duong (nguoi goi se fallback sang portal chain legacy).
    /// </summary>
    public bool RequestNavigationTo(string targetMapName)
    {
        if (string.IsNullOrEmpty(targetMapName)) return false;

        try
        {
            var path = _pathfinder.ComputePath(targetMapName);

            if (_navigationController == null && _pathfinder.Graph != null)
            {
                _navigationController = new NavigationController(_pathfinder.Graph);
                Plugin.Log.LogInfo("[SocketClient] NavigationController initialized with graph");
            }

            if (path == null)
            {
                Plugin.Log.LogWarning($"[SocketClient] BFS: no path to '{targetMapName}'");
                return false;
            }

            Plugin.Log.LogInfo($"[SocketClient] BFS path computed: {path.Count} hops -> navigating");
            _currentNavigationPath = path;
            StartCoroutine("ExecuteNavigationPath");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[SocketClient] RequestNavigationTo fail: {ex.Message}");
            return false;
        }
    }

    /// <summary>Có phien navigation nao dang chay khong.</summary>
    public bool IsNavigationInProgress => _navigationController?.IsNavigating ?? false;
}
