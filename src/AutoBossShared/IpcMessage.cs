using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AutoBossShared
{
    /// <summary>
    /// Base message structure for all IPC communication between Manager and Client.
    /// Serialized as line-delimited JSON over TCP socket.
    /// </summary>
    public class IpcMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }
        
        [JsonProperty("payload")]
        public Dictionary<string, object> Payload { get; set; }
        
        public IpcMessage()
        {
            Timestamp = DateTime.Now;
            Payload = new Dictionary<string, object>();
        }
        
        public IpcMessage(string type) : this()
        {
            Type = type;
        }
    }
    
    /// <summary>
    /// Message types for IPC communication.
    /// Type field values in IpcMessage.
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
    /// Command types for remote control.
    /// Payload.command field values in COMMAND messages.
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
}
