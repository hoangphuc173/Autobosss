using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoBossShared;
using Newtonsoft.Json;

namespace AutoBossManager.Services
{
    /// <summary>
    /// TCP socket server for accepting connections from game client instances.
    /// Manages client registry, heartbeat monitoring, command dispatch, and bidirectional messaging.
    /// Pattern from Tool_Up_Level_V111 architecture with AutoBossGrabber enhancements.
    /// </summary>
    public class SocketServer : IDisposable
    {
        // === Configuration ===
        private const int HeartbeatCheckIntervalMs = 5000;

        // === Server State ===
        private TcpListener? listener;
        private ConcurrentDictionary<Guid, ClientConnection> clients;
        private CancellationTokenSource? cts;
        private Task? acceptTask;
        private Task? heartbeatTask;
        private bool isRunning = false;
        private readonly object startStopLock = new object();

        // === Events for MainViewModel Integration ===

        /// <summary>
        /// Raised when a client sends a status update.
        /// </summary>
        public event EventHandler<StatusUpdateEventArgs>? OnStatusUpdate;

        /// <summary>
        /// Raised when a client reports a boss found.
        /// </summary>
        public event EventHandler<BossFoundEventArgs>? OnBossFound;

        /// <summary>
        /// Raised when a client sends a log event.
        /// </summary>
        public event EventHandler<LogEventArgs>? OnLogEvent;

        /// <summary>
        /// Raised when a client reports an error.
        /// </summary>
        public event EventHandler<ErrorEventArgs>? OnError;

        /// <summary>
        /// Raised when a client disconnects.
        /// </summary>
        public event EventHandler<Guid>? OnClientDisconnected;

        /// <summary>
        /// Raised when a new client connects.
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs>? OnClientConnected;

        // === Initialization ===

        public SocketServer()
        {
            clients = new ConcurrentDictionary<Guid, ClientConnection>();
        }

        // === Server Lifecycle ===

        /// <summary>
        /// Start the TCP server and begin accepting client connections.
        /// </summary>
        public void Start()
        {
            lock (startStopLock)
            {
                if (isRunning)
                {
                    Console.WriteLine("[SocketServer] Already running");
                    return;
                }

                try
                {
                    listener = new TcpListener(IPAddress.Loopback, IpcConfig.ServerPort);
                    listener.Start();
                    isRunning = true;
                    cts = new CancellationTokenSource();

                    Console.WriteLine($"[SocketServer] Listening on {IpcConfig.ServerHost}:{IpcConfig.ServerPort}");

                    // Start accept loop
                    acceptTask = Task.Run(AcceptLoop, cts.Token);

                    // Start heartbeat monitor
                    heartbeatTask = Task.Run(HeartbeatMonitor, cts.Token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SocketServer] Start failed: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Stop the TCP server and disconnect all clients.
        /// </summary>
        public void Stop()
        {
            lock (startStopLock)
            {
                if (!isRunning)
                {
                    return;
                }

                Console.WriteLine("[SocketServer] Stopping...");

                isRunning = false;
                cts?.Cancel();
                listener?.Stop();

                // Disconnect all clients
                foreach (var client in clients.Values)
                {
                    try
                    {
                        client.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SocketServer] Error disconnecting client: {ex.Message}");
                    }
                }
                clients.Clear();

                // Wait for background tasks to complete
                try
                {
                    Task.WaitAll(new[] { acceptTask, heartbeatTask }.Where(t => t != null).Select(t => t!).ToArray(), TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SocketServer] Error waiting for tasks: {ex.Message}");
                }

                Console.WriteLine("[SocketServer] Stopped");
            }
        }

        // === Client Connection Management ===

        /// <summary>
        /// Background loop accepting incoming TCP connections.
        /// Task 7.2: Accept Loop
        /// </summary>
        private async Task AcceptLoop()
        {
            Console.WriteLine("[SocketServer] Accept loop started");

            TcpListener? acceptedListener = listener;
            CancellationTokenSource? tokenSource = cts;

            if (acceptedListener == null || tokenSource == null)
            {
                return;
            }

            while (isRunning && !tokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    TcpClient tcpClient = await acceptedListener.AcceptTcpClientAsync();
                    Guid instanceId = Guid.NewGuid();

                    Console.WriteLine($"[SocketServer] Client connecting: {instanceId}");

                    var clientConn = new ClientConnection(instanceId, tcpClient);
                    clientConn.OnMessage += ClientConnection_OnMessage;
                    clientConn.OnDisconnected += ClientConnection_OnDisconnected;

                    if (clients.TryAdd(instanceId, clientConn))
                    {
                        clientConn.Start();
                        Console.WriteLine($"[SocketServer] Client connected: {instanceId}");
                        OnClientConnected?.Invoke(this, new ClientConnectedEventArgs(instanceId));
                    }
                    else
                    {
                        Console.WriteLine($"[SocketServer] Failed to add client to registry: {instanceId}");
                        clientConn.Disconnect();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped, exit gracefully
                    break;
                }
                catch (Exception ex) when (!tokenSource.Token.IsCancellationRequested)
                {
                    Console.WriteLine($"[SocketServer] Accept error: {ex.Message}");
                    await Task.Delay(1000, tokenSource.Token); // Brief delay before retry
                }
            }

            Console.WriteLine("[SocketServer] Accept loop terminated");
        }

        /// <summary>
        /// Background loop monitoring client heartbeats and disconnecting stale clients.
        /// Task 7.4: Heartbeat Monitoring
        /// </summary>
        private async Task HeartbeatMonitor()
        {
            Console.WriteLine("[SocketServer] Heartbeat monitor started");

            CancellationTokenSource? tokenSource = cts;
            if (tokenSource == null)
            {
                return;
            }

            while (isRunning && !tokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HeartbeatCheckIntervalMs, tokenSource.Token);

                    var now = DateTime.Now;
                    foreach (var kvp in clients)
                    {
                        var elapsed = (now - kvp.Value.LastHeartbeat).TotalSeconds;
                        if (elapsed > IpcConfig.HeartbeatTimeoutSec)
                        {
                            Console.WriteLine($"[SocketServer] Client timeout: {kvp.Key} " +
                                $"(last heartbeat {elapsed:F1}s ago)");

                            kvp.Value.Disconnect();
                            clients.TryRemove(kvp.Key, out _);
                            OnClientDisconnected?.Invoke(this, kvp.Key);
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

            Console.WriteLine("[SocketServer] Heartbeat monitor terminated");
        }

        // === Message Routing ===

        /// <summary>
        /// Handle incoming message from a client connection.
        /// Task 7.5: Message Routing and Event Raising
        /// </summary>
        private void ClientConnection_OnMessage(object? sender, IpcMessage message)
        {
            var clientConn = sender as ClientConnection;
            if (clientConn == null)
            {
                return;
            }

            Guid instanceId = clientConn.InstanceId;

            try
            {
                switch (message.Type)
                {
                    case MessageTypes.HEARTBEAT:
                        // Heartbeat timestamp already updated by ClientConnection
                        break;

                    case MessageTypes.STATUS_UPDATE:
                        var state = ParseStatusUpdate(message, instanceId);
                        OnStatusUpdate?.Invoke(this, new StatusUpdateEventArgs(state));
                        break;

                    case MessageTypes.BOSS_FOUND:
                        string bossName = message.Payload.TryGetValue("bossName", out var bn) ? bn?.ToString() ?? "" : "";
                        string mapName = message.Payload.TryGetValue("mapName", out var mn) ? mn?.ToString() ?? "" : "";
                        string zoneName = message.Payload.TryGetValue("zoneName", out var zn) ? zn?.ToString() ?? "" : "";
                        OnBossFound?.Invoke(this, new BossFoundEventArgs(instanceId, bossName, mapName, zoneName));
                        break;

                    case MessageTypes.BOSS_KILLED:
                        string killedBoss = message.Payload.TryGetValue("bossName", out var kb) ? kb?.ToString() ?? "" : "";
                        float killDuration = message.Payload.TryGetValue("killDurationSec", out var kd)
                            ? Convert.ToSingle(kd) : 0f;
                        // Could add OnBossKilled event if needed
                        OnLogEvent?.Invoke(this, new LogEventArgs(instanceId,
                            $"Boss killed: {killedBoss} in {killDuration:F1}s"));
                        break;

                    case MessageTypes.LOG_EVENT:
                        string logMsg = message.Payload.TryGetValue("message", out var lm) ? lm?.ToString() ?? "" : "";
                        string logLevel = message.Payload.TryGetValue("level", out var ll) ? ll?.ToString() ?? "Info" : "Info";
                        OnLogEvent?.Invoke(this, new LogEventArgs(instanceId, logMsg, logLevel));
                        break;

                    case MessageTypes.ERROR:
                        string errorMsg = message.Payload.TryGetValue("message", out var em) ? em?.ToString() ?? "" : "";
                        OnError?.Invoke(this, new ErrorEventArgs(instanceId, errorMsg));
                        break;

                    case MessageTypes.CAPTCHA_DETECTED:
                        OnLogEvent?.Invoke(this, new LogEventArgs(instanceId,
                            "Captcha detected - attempting auto-solve", "Warning"));
                        break;

                    case MessageTypes.ACK:
                        // Acknowledgment received - could track for command confirmation
                        string ackedType = message.Payload.TryGetValue("acknowledgedType", out var at)
                            ? at?.ToString() ?? "" : "";
                        Console.WriteLine($"[SocketServer] ACK received from {instanceId}: {ackedType}");
                        break;

                    default:
                        Console.WriteLine($"[SocketServer] Unknown message type from {instanceId}: {message.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SocketServer] Message handling error: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse STATUS_UPDATE message payload into BotInstanceState.
        /// </summary>
        private BotInstanceState ParseStatusUpdate(IpcMessage message, Guid instanceId)
        {
            var state = new BotInstanceState
            {
                InstanceId = instanceId,
                LastHeartbeat = DateTime.Now
            };

            try
            {
                if (message.Payload.TryGetValue("state", out var stateObj))
                {
                    if (Enum.TryParse<AutoBossState>(stateObj.ToString(), out var parsedState))
                    {
                        state.CurrentState = parsedState;
                    }
                }

                if (message.Payload.TryGetValue("map", out var mapObj))
                {
                    state.CurrentMap = mapObj?.ToString() ?? "";
                }

                if (message.Payload.TryGetValue("zone", out var zoneObj))
                {
                    state.CurrentZone = Convert.ToInt32(zoneObj);
                }

                if (message.Payload.TryGetValue("playerHpPct", out var hpObj))
                {
                    state.PlayerHpPct = Convert.ToSingle(hpObj);
                }

                if (message.Payload.TryGetValue("playerMpPct", out var mpObj))
                {
                    state.PlayerMpPct = Convert.ToSingle(mpObj);
                }

                if (message.Payload.TryGetValue("bossKillsThisSession", out var killsObj))
                {
                    state.BossKillsThisSession = Convert.ToInt32(killsObj);
                }

                if (message.Payload.TryGetValue("currentTarget", out var targetObj))
                {
                    state.LastBossKilled = targetObj?.ToString() ?? "";
                }

                if (message.Payload.TryGetValue("accountName", out var accObj))
                {
                    state.AccountName = accObj?.ToString() ?? "";
                }

                if (message.Payload.TryGetValue("sessionStartTime", out var sessionObj))
                {
                    state.SessionStartTime = Convert.ToDateTime(sessionObj);
                }

                // Set status based on state
                // Idle = connected but not farming; every other runner state is active work.
                state.Status = state.CurrentState == AutoBossState.Idle
                    ? ConnectionStatus.Connected
                    : ConnectionStatus.Active;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SocketServer] Status update parse error: {ex.Message}");
            }

            return state;
        }

        /// <summary>
        /// Handle client disconnection event.
        /// </summary>
        private void ClientConnection_OnDisconnected(object? sender, Guid instanceId)
        {
            clients.TryRemove(instanceId, out _);
            Console.WriteLine($"[SocketServer] Client disconnected: {instanceId}");
            OnClientDisconnected?.Invoke(this, instanceId);
        }

        // === Command Sending ===

        /// <summary>
        /// Send a command to a specific client.
        /// Task 7.6: Command Sending Methods
        /// </summary>
        public void SendCommand(Guid instanceId, string command, System.Collections.Generic.Dictionary<string, object>? parameters = null)
        {
            if (!clients.TryGetValue(instanceId, out var clientConn))
            {
                Console.WriteLine($"[SocketServer] Client not found: {instanceId}");
                return;
            }

            try
            {
                var message = new IpcMessage(MessageTypes.COMMAND);
                message.Payload["command"] = command;

                if (parameters != null)
                {
                    foreach (var kvp in parameters)
                    {
                        message.Payload[kvp.Key] = kvp.Value;
                    }
                }

                clientConn.SendMessage(message);
                Console.WriteLine($"[SocketServer] Sent command to {instanceId}: {command}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SocketServer] Send command error: {ex.Message}");
            }
        }

        /// <summary>
        /// Broadcast a command to all connected clients.
        /// Task 7.6: Command Sending Methods
        /// </summary>
        public void BroadcastCommand(string command, System.Collections.Generic.Dictionary<string, object>? parameters = null)
        {
            var message = new IpcMessage(MessageTypes.COMMAND);
            message.Payload["command"] = command;

            if (parameters != null)
            {
                foreach (var kvp in parameters)
                {
                    message.Payload[kvp.Key] = kvp.Value;
                }
            }

            int successCount = 0;
            int failCount = 0;

            foreach (var clientConn in clients.Values)
            {
                try
                {
                    clientConn.SendMessage(message);
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SocketServer] Broadcast error to {clientConn.InstanceId}: {ex.Message}");
                    failCount++;
                }
            }

            Console.WriteLine($"[SocketServer] Broadcast command: {command} " +
                $"(success: {successCount}, failed: {failCount})");
        }

        /// <summary>
        /// Send configuration update to a specific client.
        /// Task 7.6: Command Sending Methods
        /// </summary>
        public void SendConfigUpdate(Guid instanceId, BotProfile profile)
        {
            if (!clients.TryGetValue(instanceId, out var clientConn))
            {
                Console.WriteLine($"[SocketServer] Client not found: {instanceId}");
                return;
            }

            try
            {
                var message = new IpcMessage(MessageTypes.CONFIG_UPDATE);
                
                // Serialize relevant config fields
                message.Payload["maxZoneAttempts"] = profile.MaxZoneAttempts;
                message.Payload["retreatHpPct"] = profile.RetreatHpPct;
                message.Payload["attackRange"] = profile.AttackRange;
                message.Payload["combatTimeoutSec"] = profile.CombatTimeoutSec;
                message.Payload["lootRadius"] = profile.LootRadius;
                
                // Serialize boss skill triggers
                if (profile.BossSkillTriggers != null && profile.BossSkillTriggers.Count > 0)
                {
                    message.Payload["bossSkillTriggers"] = profile.BossSkillTriggers;
                }
                
                // Serialize farm loop settings
                message.Payload["enableAutoZoneSwitch"] = profile.EnableAutoZoneSwitch;
                message.Payload["enableAutoReward"] = profile.EnableAutoReward;
                message.Payload["enableAutoSatellite"] = profile.EnableAutoSatellite;
                
                // Serialize item filter settings
                message.Payload["itemFilterMode"] = profile.FilterMode.ToString();
                message.Payload["itemFilterList"] = profile.ItemFilterList;
                message.Payload["alwaysPickGems"] = profile.AlwaysPickGems;
                message.Payload["alwaysPickQuestItems"] = profile.AlwaysPickQuestItems;
                message.Payload["minRarityToPickup"] = profile.MinRarityToPickup;

                clientConn.SendMessage(message);
                Console.WriteLine($"[SocketServer] Sent config update to {instanceId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SocketServer] Send config update error: {ex.Message}");
            }
        }

        // === Public Queries ===

        /// <summary>
        /// Get list of all connected client IDs.
        /// </summary>
        public System.Collections.Generic.List<Guid> GetConnectedClients()
        {
            return new System.Collections.Generic.List<Guid>(clients.Keys);
        }

        /// <summary>
        /// Get count of connected clients.
        /// </summary>
        public int GetClientCount()
        {
            return clients.Count;
        }

        /// <summary>
        /// Check if server is currently running.
        /// </summary>
        public bool IsRunning => isRunning;

        // === IDisposable ===

        public void Dispose()
        {
            Stop();
            cts?.Dispose();
        }
    }

    // === ClientConnection Class ===

    /// <summary>
    /// Represents a single connected client with its own receive loop and state.
    /// Task 7.3: ClientConnection Class
    /// </summary>
    internal class ClientConnection
    {
        public Guid InstanceId { get; }
        public DateTime LastHeartbeat { get; private set; }

        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;
        private Task? receiveTask;
        private bool isRunning = false;
        private CancellationTokenSource cts;

        public event EventHandler<IpcMessage>? OnMessage;
        public event EventHandler<Guid>? OnDisconnected;

        public ClientConnection(Guid instanceId, TcpClient tcpClient)
        {
            InstanceId = instanceId;
            client = tcpClient;
            LastHeartbeat = DateTime.Now;

            var stream = client.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8);
            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Start the background receive loop.
        /// </summary>
        public void Start()
        {
            isRunning = true;
            receiveTask = Task.Run(ReceiveLoop, cts.Token);
        }

        /// <summary>
        /// Background loop receiving messages from client.
        /// </summary>
        private async Task ReceiveLoop()
        {
            Console.WriteLine($"[ClientConnection] Receive loop started for {InstanceId}");

            try
            {
                while (isRunning && !cts.Token.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync();
                    if (line == null)
                    {
                        // Connection closed by client
                        Console.WriteLine($"[ClientConnection] Connection closed by client: {InstanceId}");
                        break;
                    }

                    try
                    {
                        var message = JsonConvert.DeserializeObject<IpcMessage>(line);

                        if (message != null)
                        {
                            // Update heartbeat timestamp
                            if (message.Type == MessageTypes.HEARTBEAT)
                            {
                                LastHeartbeat = DateTime.Now;
                            }

                            OnMessage?.Invoke(this, message);
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"[ClientConnection] JSON parse error: {ex.Message}");
                        // Continue receiving - don't disconnect on parse error
                    }
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[ClientConnection] IO error for {InstanceId}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientConnection] Receive loop error for {InstanceId}: {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        /// <summary>
        /// Send a message to the client.
        /// </summary>
        public void SendMessage(IpcMessage message)
        {
            if (!isRunning || writer == null)
            {
                throw new InvalidOperationException("Connection is not active");
            }

            try
            {
                string json = JsonConvert.SerializeObject(message);
                writer.WriteLine(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientConnection] Send error for {InstanceId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Disconnect the client and clean up resources.
        /// </summary>
        public void Disconnect()
        {
            if (!isRunning) return;

            isRunning = false;
            cts?.Cancel();

            try
            {
                reader?.Close();
                writer?.Close();
                client?.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientConnection] Disconnect error for {InstanceId}: {ex.Message}");
            }

            OnDisconnected?.Invoke(this, InstanceId);
        }
    }

    // === Event Args Classes ===

    public class StatusUpdateEventArgs : EventArgs
    {
        public BotInstanceState State { get; }

        public StatusUpdateEventArgs(BotInstanceState state)
        {
            State = state;
        }
    }

    public class BossFoundEventArgs : EventArgs
    {
        public Guid InstanceId { get; }
        public string BossName { get; }
        public string MapName { get; }
        public string ZoneName { get; }

        public BossFoundEventArgs(Guid instanceId, string bossName, string mapName, string zoneName)
        {
            InstanceId = instanceId;
            BossName = bossName;
            MapName = mapName;
            ZoneName = zoneName;
        }
    }

    public class LogEventArgs : EventArgs
    {
        public Guid InstanceId { get; }
        public string Message { get; }
        public string Level { get; }

        public LogEventArgs(Guid instanceId, string message, string level = "Info")
        {
            InstanceId = instanceId;
            Message = message;
            Level = level;
        }
    }

    public class ErrorEventArgs : EventArgs
    {
        public Guid InstanceId { get; }
        public string Message { get; }

        public ErrorEventArgs(Guid instanceId, string message)
        {
            InstanceId = instanceId;
            Message = message;
        }
    }

    public class ClientConnectedEventArgs : EventArgs
    {
        public Guid InstanceId { get; }

        public ClientConnectedEventArgs(Guid instanceId)
        {
            InstanceId = instanceId;
        }
    }
}
