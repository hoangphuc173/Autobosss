namespace AutoBossShared
{
    /// <summary>
    /// Shared configuration for IPC connection.
    /// Both SocketClient and SocketServer read from this.
    /// </summary>
    public static class IpcConfig
    {
        /// <summary>
        /// TCP server host (localhost).
        /// </summary>
        public const string ServerHost = "127.0.0.1";
        
        /// <summary>
        /// TCP server port.
        /// Default: 28081
        /// Can be changed if port conflict occurs.
        /// </summary>
        public static int ServerPort { get; set; } = 28081;
        
        /// <summary>
        /// Heartbeat interval in seconds.
        /// </summary>
        public const float HeartbeatIntervalSec = 3f;
        
        /// <summary>
        /// Heartbeat timeout in seconds.
        /// Client disconnected if no heartbeat for this duration.
        /// </summary>
        public const float HeartbeatTimeoutSec = 10f;
    }
}
