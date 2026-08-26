using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AutoBossShared
{
    /// <summary>
    /// Complete configuration for a single bot instance.
    /// Stored as JSON in AppData/AutoBossManager/profiles/{AccountName}.json
    /// </summary>
    public class BotProfile
    {
        // === Identity ===
        [JsonProperty("accountName")]
        public string AccountName { get; set; }
        
        [JsonProperty("gameExecutablePath")]
        public string GameExecutablePath { get; set; }
        
        // === Game Credentials ===
        [JsonProperty("username")]
        public string Username { get; set; }
        
        [JsonProperty("password")]
        public string Password { get; set; }  // Encrypted in storage
        
        // === Boss Hunting Configuration ===
        [JsonProperty("targetBossNames")]
        public List<string> TargetBossNames { get; set; }
        
        [JsonProperty("bossMapNames")]
        public List<string> BossMapNames { get; set; }
        
        [JsonProperty("homeMapName")]
        public string HomeMapName { get; set; }
        
        [JsonProperty("townMapName")]
        public string TownMapName { get; set; }
        
        [JsonProperty("fastTravelAnchorMap")]
        public string FastTravelAnchorMap { get; set; }
        
        [JsonProperty("portalChainMaps")]
        public List<string> PortalChainMaps { get; set; }
        
        // === Behavior Parameters ===
        [JsonProperty("maxZoneAttempts")]
        public int MaxZoneAttempts { get; set; }
        
        [JsonProperty("attackRange")]
        public float AttackRange { get; set; }
        
        [JsonProperty("combatTimeoutSec")]
        public float CombatTimeoutSec { get; set; }
        
        [JsonProperty("retreatHpPct")]
        public float RetreatHpPct { get; set; }
        
        [JsonProperty("lootRadius")]
        public float LootRadius { get; set; }
        
        // === Strategy Preset ===
        [JsonProperty("strategy")]
        public StrategyPreset Strategy { get; set; }
        
        // === Boss Skill Configuration ===
        [JsonProperty("bossSkillTriggers")]
        public List<SkillTrigger> BossSkillTriggers { get; set; }
        
        // === Farm Loop Configuration ===
        [JsonProperty("enableAutoZoneSwitch")]
        public bool EnableAutoZoneSwitch { get; set; }
        
        [JsonProperty("enableAutoReward")]
        public bool EnableAutoReward { get; set; }
        
        [JsonProperty("enableAutoSatellite")]
        public bool EnableAutoSatellite { get; set; }
        
        // === Item Filter Configuration ===
        [JsonProperty("filterMode")]
        public ItemFilterMode FilterMode { get; set; }
        
        [JsonProperty("itemFilterList")]
        public List<string> ItemFilterList { get; set; }
        
        [JsonProperty("alwaysPickGems")]
        public bool AlwaysPickGems { get; set; }
        
        [JsonProperty("alwaysPickQuestItems")]
        public bool AlwaysPickQuestItems { get; set; }
        
        [JsonProperty("minRarityToPickup")]
        public int MinRarityToPickup { get; set; }
        
        // === Auto Restart Configuration ===
        [JsonProperty("autoRestartOnCrash")]
        public bool AutoRestartOnCrash { get; set; }
        
        [JsonProperty("maxRestartAttempts")]
        public int MaxRestartAttempts { get; set; }
        
        // === Schedule Configuration (Phase 2) ===
        [JsonProperty("schedule")]
        public Schedule Schedule { get; set; }
        
        public BotProfile()
        {
            TargetBossNames = new List<string>();
            BossMapNames = new List<string>();
            PortalChainMaps = new List<string>();
            BossSkillTriggers = new List<SkillTrigger>();
            ItemFilterList = new List<string>();
            Strategy = StrategyPreset.Balanced;
            FilterMode = ItemFilterMode.Disabled;
            MaxZoneAttempts = 15;
            AttackRange = 100f;
            CombatTimeoutSec = 30f;
            RetreatHpPct = 30f;
            LootRadius = 150f;
            AlwaysPickGems = true;
            AlwaysPickQuestItems = true;
            MinRarityToPickup = 0;
            AutoRestartOnCrash = false;
            MaxRestartAttempts = 3;
        }
    }

    /// <summary>
    /// Boss skill trigger configuration.
    /// Defines when to use specific skills during combat.
    /// </summary>
    public class SkillTrigger
    {
        [JsonProperty("hpThreshold")]
        public float HpThreshold { get; set; }    // Absolute HP value (not percentage)
        
        [JsonProperty("skillKey")]
        public int SkillKey { get; set; }         // 1-4
        
        [JsonProperty("spamCount")]
        public int SpamCount { get; set; }        // How many times to press (default 1)
        
        public SkillTrigger()
        {
            SpamCount = 1;
        }
    }

    /// <summary>
    /// Schedule configuration for automated farming windows.
    /// </summary>
    public class Schedule
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }
        
        [JsonProperty("activeWindows")]
        public List<TimeWindow> ActiveWindows { get; set; }
        
        public Schedule()
        {
            Enabled = false;
            ActiveWindows = new List<TimeWindow>();
        }
    }

    /// <summary>
    /// Time window for scheduled farming.
    /// </summary>
    public class TimeWindow
    {
        [JsonProperty("dayOfWeek")]
        public DayOfWeek DayOfWeek { get; set; }
        
        [JsonProperty("startTime")]
        public TimeSpan StartTime { get; set; }
        
        [JsonProperty("endTime")]
        public TimeSpan EndTime { get; set; }
    }
}
