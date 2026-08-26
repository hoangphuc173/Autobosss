using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AutoBossShared
{
    /// <summary>
    /// Real-time state of a connected bot instance.
    /// Maintained in Manager memory, periodically received from Client.
    /// </summary>
    public class BotInstanceState
    {
        // === Identity ===
        [JsonProperty("instanceId")]
        public Guid InstanceId { get; set; }
        
        [JsonProperty("accountName")]
        public string AccountName { get; set; }
        
        // === Connection Status ===
        [JsonProperty("status")]
        public ConnectionStatus Status { get; set; }
        
        [JsonProperty("lastHeartbeat")]
        public DateTime LastHeartbeat { get; set; }
        
        [JsonProperty("sessionStartTime")]
        public DateTime SessionStartTime { get; set; }
        
        // === Game State ===
        [JsonProperty("currentState")]
        public AutoBossState CurrentState { get; set; }
        
        [JsonProperty("currentMap")]
        public string CurrentMap { get; set; }
        
        [JsonProperty("currentZone")]
        public int CurrentZone { get; set; }
        
        [JsonProperty("playerPosition")]
        public Vector2 PlayerPosition { get; set; }
        
        // === Player Stats ===
        [JsonProperty("playerHpPct")]
        public float PlayerHpPct { get; set; }
        
        [JsonProperty("playerMpPct")]
        public float PlayerMpPct { get; set; }
        
        // === Progress Metrics ===
        [JsonProperty("bossKillsThisSession")]
        public int BossKillsThisSession { get; set; }
        
        [JsonProperty("totalBossKills")]
        public int TotalBossKills { get; set; }
        
        [JsonProperty("deathCount")]
        public int DeathCount { get; set; }
        
        [JsonProperty("captchaSolveCount")]
        public int CaptchaSolveCount { get; set; }
        
        [JsonProperty("errorCount")]
        public int ErrorCount { get; set; }
        
        // === Recent Activity ===
        [JsonProperty("recentErrors")]
        public List<string> RecentErrors { get; set; }
        
        [JsonProperty("lastBossKillTime")]
        public DateTime LastBossKillTime { get; set; }
        
        [JsonProperty("lastBossKilled")]
        public string LastBossKilled { get; set; }
        
        // === Calculated Metrics (not serialized) ===
        [JsonIgnore]
        public TimeSpan Uptime => DateTime.Now - SessionStartTime;
        
        [JsonIgnore]
        public double BossKillsPerHour => 
            BossKillsThisSession / Math.Max(Uptime.TotalHours, 0.01);
        
        public BotInstanceState()
        {
            InstanceId = Guid.NewGuid();
            Status = ConnectionStatus.Disconnected;
            SessionStartTime = DateTime.Now;
            LastHeartbeat = DateTime.Now;
            CurrentState = AutoBossState.Idle;
            RecentErrors = new List<string>();
            PlayerPosition = new Vector2();
            PlayerHpPct = 100f;
            PlayerMpPct = 100f;
        }
    }

    /// <summary>
    /// Simple 2D vector structure for player position.
    /// </summary>
    public struct Vector2
    {
        [JsonProperty("x")]
        public float X { get; set; }
        
        [JsonProperty("y")]
        public float Y { get; set; }
        
        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }
        
        public override string ToString() => $"({X:F1}, {Y:F1})";
    }
}
