using System;

namespace AutoBossShared
{
    /// <summary>
    /// Strategy preset templates for bot behavior configuration.
    /// </summary>
    public enum StrategyPreset
    {
        Aggressive,    // Fast movement, low dwell times, high risk
        Balanced,      // Medium settings, good for most situations
        Safe,          // Slow movement, high dwell times, low detection risk
        Custom         // User-defined parameters
    }

    /// <summary>
    /// Item filter operation mode.
    /// </summary>
    public enum ItemFilterMode
    {
        Disabled,      // Pick up all items
        Whitelist,     // Only pick items in list
        Blacklist      // Pick all except items in list
    }

    /// <summary>
    /// Connection status of a bot instance.
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,   // Not connected
        Connected,      // Connected but not started
        Active,         // Actively farming
        Paused,         // Paused by user
        Error,          // In error state
        Stopping        // Shutting down
    }

    /// <summary>
    /// AutoBoss state machine states.
    /// Mirrors the existing AutoBossRunner state machine.
    /// </summary>
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
}
