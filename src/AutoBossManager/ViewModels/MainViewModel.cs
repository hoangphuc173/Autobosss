using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using AutoBossManager.Helpers;
using AutoBossShared;

namespace AutoBossManager.ViewModels
{
    /// <summary>
    /// Main ViewModel for the AutoBoss Manager application.
    /// Manages collection of bot instances and aggregate statistics.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _refreshTimer;
        private int _connectedClientCount;
        private int _totalBossKills;
        private TimeSpan _totalUptime;
        private string _statusMessage = "Ready";
        private Services.SocketServer? _socketServer;

        // === Observable Collections ===
        public ObservableCollection<BotInstanceViewModel> BotInstances { get; }

        // === Aggregate Statistics ===
        public int ConnectedClientCount
        {
            get => _connectedClientCount;
            set { _connectedClientCount = value; OnPropertyChanged(); }
        }

        public int TotalBossKills
        {
            get => _totalBossKills;
            set { _totalBossKills = value; OnPropertyChanged(); }
        }

        public TimeSpan TotalUptime
        {
            get => _totalUptime;
            set
            {
                _totalUptime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalUptimeFormatted));
            }
        }

        public string TotalUptimeFormatted =>
            $"{(int)TotalUptime.TotalHours:D2}:{TotalUptime.Minutes:D2}:{TotalUptime.Seconds:D2}";

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public double AverageBossKillsPerHour
        {
            get
            {
                var avgHours = BotInstances
                    .Where(b => b.Status == ConnectionStatus.Active)
                    .Select(b => b.BossKillsPerHour)
                    .DefaultIfEmpty(0)
                    .Average();
                return avgHours;
            }
        }

        public string AverageBossKillsPerHourFormatted =>
            $"{AverageBossKillsPerHour:F1}";

        // === Commands ===
        public ICommand StartAllCommand { get; }
        public ICommand StopAllCommand { get; }
        public ICommand EmergencyStopCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand AddBotCommand { get; }

        // === Events ===
        public event EventHandler<string>? GlobalCommandRequested;

        // === Constructor ===
        public MainViewModel()
        {
            BotInstances = new ObservableCollection<BotInstanceViewModel>();

            // Initialize commands
            StartAllCommand = new RelayCommand(_ => ExecuteStartAll());
            StopAllCommand = new RelayCommand(_ => ExecuteStopAll());
            EmergencyStopCommand = new RelayCommand(_ => ExecuteEmergencyStop());
            RefreshCommand = new RelayCommand(_ => RefreshStatistics());
            AddBotCommand = new RelayCommand(_ => ExecuteAddBot());

            // Set up refresh timer (1 second interval)
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            // Add sample data for testing
            // InitializeSampleData();
        }

        // === Public Methods ===

        /// <summary>
        /// Add a new bot instance to the collection
        /// </summary>
        public void AddBotInstance(BotInstanceState state)
        {
            var viewModel = new BotInstanceViewModel();
            viewModel.UpdateFromState(state);
            viewModel.CommandRequested += BotInstance_CommandRequested;

            BotInstances.Add(viewModel);
            RefreshStatistics();

            StatusMessage = $"Bot instance added: {state.AccountName}";
        }

        /// <summary>
        /// Update an existing bot instance or add if not found
        /// </summary>
        public void UpdateBotInstance(BotInstanceState state)
        {
            var existing = BotInstances.FirstOrDefault(b => b.InstanceId == state.InstanceId);

            if (existing != null)
            {
                existing.UpdateFromState(state);
            }
            else
            {
                AddBotInstance(state);
            }

            RefreshStatistics();
        }

        /// <summary>
        /// Remove a bot instance from the collection
        /// </summary>
        public void RemoveBotInstance(Guid instanceId)
        {
            var existing = BotInstances.FirstOrDefault(b => b.InstanceId == instanceId);

            if (existing != null)
            {
                existing.CommandRequested -= BotInstance_CommandRequested;
                BotInstances.Remove(existing);
                RefreshStatistics();

                StatusMessage = $"Bot instance removed: {existing.AccountName}";
            }
        }

        /// <summary>
        /// Refresh aggregate statistics from all bot instances
        /// </summary>
        public void RefreshStatistics()
        {
            ConnectedClientCount = BotInstances.Count(b =>
                b.Status == ConnectionStatus.Active ||
                b.Status == ConnectionStatus.Connected);

            TotalBossKills = BotInstances.Sum(b => b.BossKillsThisSession);

            TotalUptime = TimeSpan.FromSeconds(
                BotInstances.Sum(b => b.Uptime.TotalSeconds));

            OnPropertyChanged(nameof(AverageBossKillsPerHour));
            OnPropertyChanged(nameof(AverageBossKillsPerHourFormatted));
        }

        /// <summary>
        /// Set the SocketServer reference for sending commands to clients.
        /// Called during application startup to wire up IPC.
        /// </summary>
        public void SetSocketServer(Services.SocketServer socketServer)
        {
            _socketServer = socketServer;
        }

        // === Private Methods ===

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            // Refresh time-based properties for all bot instances
            foreach (var bot in BotInstances)
            {
                bot.RefreshTimeProperties();
            }

            // Refresh aggregate statistics
            RefreshStatistics();
        }

        private void ExecuteStartAll()
        {
            GlobalCommandRequested?.Invoke(this, "START_ALL");
            StatusMessage = "Starting all bot instances...";
        }

        private void ExecuteStopAll()
        {
            GlobalCommandRequested?.Invoke(this, "STOP_ALL");
            StatusMessage = "Stopping all bot instances...";
        }

        private void ExecuteEmergencyStop()
        {
            GlobalCommandRequested?.Invoke(this, "EMERGENCY_STOP");
            StatusMessage = "EMERGENCY STOP - All bots halted!";
        }

        private void ExecuteAddBot()
        {
            // Placeholder - will be implemented when profile manager is added
            StatusMessage = "Add Bot feature coming in Task 7...";
        }

        private void BotInstance_CommandRequested(object? sender, string command)
        {
            if (sender is BotInstanceViewModel botVm && _socketServer != null)
            {
                // Send command to specific bot instance via SocketServer
                _socketServer.SendCommand(botVm.InstanceId, command);
                StatusMessage = $"Command {command} sent to {botVm.AccountName}";
            }
        }

        /// <summary>
        /// Initialize sample data for UI testing
        /// </summary>
        private void InitializeSampleData()
        {
            // Add 3 sample bot instances for testing
            var sample1 = new BotInstanceState
            {
                InstanceId = Guid.NewGuid(),
                AccountName = "TestBot01",
                Status = ConnectionStatus.Active,
                CurrentState = AutoBossState.MoveToBoss,
                CurrentMap = "Cung Điện",
                CurrentZone = 5,
                PlayerHpPct = 85.5f,
                PlayerMpPct = 92.0f,
                BossKillsThisSession = 12,
                SessionStartTime = DateTime.Now.AddHours(-1.5),
                LastBossKilled = "Vua Vegita"
            };
            AddBotInstance(sample1);

            var sample2 = new BotInstanceState
            {
                InstanceId = Guid.NewGuid(),
                AccountName = "TestBot02",
                Status = ConnectionStatus.Active,
                CurrentState = AutoBossState.EngageBoss,
                CurrentMap = "Sa Mạc",
                CurrentZone = 3,
                PlayerHpPct = 62.0f,
                PlayerMpPct = 78.5f,
                BossKillsThisSession = 8,
                SessionStartTime = DateTime.Now.AddMinutes(-45),
                LastBossKilled = "Cooler"
            };
            AddBotInstance(sample2);

            var sample3 = new BotInstanceState
            {
                InstanceId = Guid.NewGuid(),
                AccountName = "TestBot03",
                Status = ConnectionStatus.Paused,
                CurrentState = AutoBossState.Idle,
                CurrentMap = "Làng Kakarot",
                CurrentZone = 1,
                PlayerHpPct = 100.0f,
                PlayerMpPct = 100.0f,
                BossKillsThisSession = 0,
                SessionStartTime = DateTime.Now.AddMinutes(-10),
                LastBossKilled = ""
            };
            AddBotInstance(sample3);

            RefreshStatistics();
            StatusMessage = "Sample data loaded (3 test bots)";
        }

        // === INotifyPropertyChanged ===
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
