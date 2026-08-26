using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AutoBossManager.Helpers;
using AutoBossShared;

namespace AutoBossManager.ViewModels
{
    /// <summary>
    /// ViewModel for a single bot instance.
    /// Represents real-time state and provides control commands.
    /// </summary>
    public class BotInstanceViewModel : INotifyPropertyChanged
    {
        private Guid _instanceId;
        private string _accountName = string.Empty;
        private ConnectionStatus _status;
        private AutoBossState _currentState;
        private string _currentMap = string.Empty;
        private int _currentZone;
        private float _playerHpPct;
        private float _playerMpPct;
        private int _bossKillsThisSession;
        private DateTime _sessionStartTime;
        private DateTime _lastHeartbeat;
        private string _lastBossKilled = string.Empty;

        // === Identity ===
        public Guid InstanceId
        {
            get => _instanceId;
            set { _instanceId = value; OnPropertyChanged(); }
        }

        public string AccountName
        {
            get => _accountName;
            set { _accountName = value; OnPropertyChanged(); }
        }

        // === Connection Status ===
        public ConnectionStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public DateTime LastHeartbeat
        {
            get => _lastHeartbeat;
            set { _lastHeartbeat = value; OnPropertyChanged(); }
        }

        public DateTime SessionStartTime
        {
            get => _sessionStartTime;
            set
            {
                _sessionStartTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Uptime));
                OnPropertyChanged(nameof(UptimeFormatted));
            }
        }

        // === Game State ===
        public AutoBossState CurrentState
        {
            get => _currentState;
            set { _currentState = value; OnPropertyChanged(); }
        }

        public string CurrentMap
        {
            get => _currentMap;
            set { _currentMap = value; OnPropertyChanged(); }
        }

        public int CurrentZone
        {
            get => _currentZone;
            set { _currentZone = value; OnPropertyChanged(); }
        }

        // === Player Stats ===
        public float PlayerHpPct
        {
            get => _playerHpPct;
            set
            {
                _playerHpPct = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HpColor));
            }
        }

        public float PlayerMpPct
        {
            get => _playerMpPct;
            set { _playerMpPct = value; OnPropertyChanged(); }
        }

        // === Progress Metrics ===
        public int BossKillsThisSession
        {
            get => _bossKillsThisSession;
            set
            {
                _bossKillsThisSession = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BossKillsPerHour));
            }
        }

        public string LastBossKilled
        {
            get => _lastBossKilled;
            set { _lastBossKilled = value; OnPropertyChanged(); }
        }

        // === Computed Properties ===
        public TimeSpan Uptime => DateTime.Now - SessionStartTime;

        public string UptimeFormatted =>
            $"{(int)Uptime.TotalHours:D2}:{Uptime.Minutes:D2}:{Uptime.Seconds:D2}";

        public double BossKillsPerHour =>
            BossKillsThisSession / Math.Max(Uptime.TotalHours, 0.01);

        public string BossKillsPerHourFormatted =>
            $"{BossKillsPerHour:F1}";

        public string StatusColor => Status switch
        {
            ConnectionStatus.Active => "Green",
            ConnectionStatus.Connected => "Yellow",
            ConnectionStatus.Paused => "Orange",
            ConnectionStatus.Error => "Red",
            ConnectionStatus.Stopping => "Gray",
            _ => "LightGray"
        };

        public string StatusText => Status.ToString();

        public string HpColor => PlayerHpPct switch
        {
            >= 70 => "Green",
            >= 30 => "Orange",
            _ => "Red"
        };

        // === Commands ===
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand ConfigCommand { get; }

        // === Events ===
        public event EventHandler<string>? CommandRequested;

        // === Constructor ===
        public BotInstanceViewModel()
        {
            _sessionStartTime = DateTime.Now;
            _lastHeartbeat = DateTime.Now;
            _status = ConnectionStatus.Disconnected;
            _currentState = AutoBossState.Idle;

            // Initialize commands
            StartCommand = new RelayCommand(_ => RequestCommand("START_FARMING"));
            StopCommand = new RelayCommand(_ => RequestCommand("STOP_FARMING"));
            PauseCommand = new RelayCommand(_ => RequestCommand("PAUSE"));
            ResumeCommand = new RelayCommand(_ => RequestCommand("RESUME"));
            ConfigCommand = new RelayCommand(_ => RequestCommand("OPEN_CONFIG"));
        }

        // === Public Methods ===

        /// <summary>
        /// Update this ViewModel from a BotInstanceState
        /// </summary>
        public void UpdateFromState(BotInstanceState state)
        {
            InstanceId = state.InstanceId;
            AccountName = state.AccountName;
            Status = state.Status;
            CurrentState = state.CurrentState;
            CurrentMap = state.CurrentMap;
            CurrentZone = state.CurrentZone;
            PlayerHpPct = state.PlayerHpPct;
            PlayerMpPct = state.PlayerMpPct;
            BossKillsThisSession = state.BossKillsThisSession;
            SessionStartTime = state.SessionStartTime;
            LastHeartbeat = state.LastHeartbeat;
            LastBossKilled = state.LastBossKilled;

            // Refresh computed properties
            OnPropertyChanged(nameof(Uptime));
            OnPropertyChanged(nameof(UptimeFormatted));
            OnPropertyChanged(nameof(BossKillsPerHour));
            OnPropertyChanged(nameof(BossKillsPerHourFormatted));
        }

        /// <summary>
        /// Notify UI to refresh time-based properties
        /// </summary>
        public void RefreshTimeProperties()
        {
            OnPropertyChanged(nameof(Uptime));
            OnPropertyChanged(nameof(UptimeFormatted));
            OnPropertyChanged(nameof(BossKillsPerHour));
            OnPropertyChanged(nameof(BossKillsPerHourFormatted));
        }

        // === Private Methods ===

        private void RequestCommand(string command)
        {
            CommandRequested?.Invoke(this, command);
        }

        // === INotifyPropertyChanged ===
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
