using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutoBossManager.Helpers;
using AutoBossShared;

namespace AutoBossManager.ViewModels
{
    /// <summary>Mot entry trong hang doi captcha (task 23.3).</summary>
    public class CaptchaEntry
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Account { get; set; } = "";
        public string Status { get; set; } = "Auto-solving";
        public Guid InstanceId { get; set; }

        public string TimeFormatted => Time.ToString("HH:mm:ss");
    }

    /// <summary>
    /// Mot dong log trong panel Logs.
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public string Level { get; init; } = "Info";
        public string Source { get; init; } = "";
        public string Message { get; init; } = "";

        public string TimeFormatted => Timestamp.ToString("HH:mm:ss");
        public string Display => $"[{TimeFormatted}] [{Level}] {Source} {Message}".TrimEnd();

        public string LevelColor => Level switch
        {
            "Error" => "#EF4444",
            "Warning" => "#F59E0B",
            _ => "#94A3B8",
        };
    }

    /// <summary>
    /// Main ViewModel for the AutoBoss Manager application.
    /// Manages collection of bot instances and aggregate statistics.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private const int MaxLogEntries = 1000;

        private readonly DispatcherTimer _refreshTimer;
        private readonly Services.ProfileManager _profileManager;

        // === Analytics tab VM (task 16.3) ===
        public ViewModels.AnalyticsViewModel Analytics { get; }
        private int _connectedClientCount;
        private int _totalBossKills;
        private TimeSpan _totalUptime;
        private string _statusMessage = "Ready";
        private Services.SocketServer? _socketServer;
        private Services.ProcessLauncherService? _launcher;

        // === Global Pause (task 21.4): chan moi bot khoi farm ===
        private bool _globalPause;
        public bool GlobalPause
        {
            get => _globalPause;
            set
            {
                if (_globalPause == value) return;
                _globalPause = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GlobalPauseText));

                if (_socketServer != null)
                {
                    var cmd = value ? Commands.PAUSE : Commands.RESUME;
                    _socketServer.BroadcastCommand(cmd);
                    AppendLog(value ? "Warning" : "Info",
                        $"GLOBAL PAUSE {(value ? "ON - tat ca bot tam dung" : "OFF - resume")}", Guid.Empty);
                }
                StatusMessage = value
                    ? "⏸ GLOBAL PAUSE ON - mọi lệnh Start All bị chặn"
                    : "▶ Global pause OFF";
            }
        }

        public string GlobalPauseText => GlobalPause ? "⏸ PAUSED" : "⏸ Global Pause";

        // === Observable Collections ===
        public ObservableCollection<BotInstanceViewModel> BotInstances { get; }
        public ObservableCollection<LogEntry> LogEntries { get; }
        public ObservableCollection<CaptchaEntry> CaptchaQueue { get; }

        // === Selected bot (cho shortcut Space = Start/Stop) ===
        private BotInstanceViewModel? _selectedBot;
        public BotInstanceViewModel? SelectedBot
        {
            get => _selectedBot;
            set { _selectedBot = value; OnPropertyChanged(); }
        }

        /// <summary>Space: toggle Start/Stop cho bot dang chon (task 26.1).</summary>
        public void ToggleSelectedBot()
        {
            if (SelectedBot == null)
            {
                StatusMessage = "Chưa chọn bot nào (click vào 1 hàng trước).";
                return;
            }

            var isRunning = SelectedBot.Status == ConnectionStatus.Active;
            var cmd = isRunning ? "STOP_FARMING" : "START_FARMING";
            BotInstance_CommandRequested(SelectedBot, cmd);
        }

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
        public ICommand ClearLogsCommand { get; }
        public ICommand GlobalPauseCommand { get; }
        public ICommand ToggleSelectedBotCommand { get; }
        public ICommand LaunchAllCommand { get; }

        // === Events ===
        public event EventHandler<string>? GlobalCommandRequested;

        // === Constructor ===
        public MainViewModel(Services.ProfileManager profileManager, Services.AnalyticsEngine analyticsEngine)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            Analytics = new ViewModels.AnalyticsViewModel(analyticsEngine ?? throw new ArgumentNullException(nameof(analyticsEngine)));

            BotInstances = new ObservableCollection<BotInstanceViewModel>();
            LogEntries = new ObservableCollection<LogEntry>();
            CaptchaQueue = new ObservableCollection<CaptchaEntry>();

            // Initialize commands
            StartAllCommand = new RelayCommand(_ => ExecuteStartAll());
            StopAllCommand = new RelayCommand(_ => ExecuteStopAll());
            EmergencyStopCommand = new RelayCommand(_ => ExecuteEmergencyStop());
            RefreshCommand = new RelayCommand(_ => RefreshStatistics());
            AddBotCommand = new RelayCommand(_ => ExecuteAddBot());
            ClearLogsCommand = new RelayCommand(_ => LogEntries.Clear());
            GlobalPauseCommand = new RelayCommand(_ => GlobalPause = !GlobalPause);
            ToggleSelectedBotCommand = new RelayCommand(_ => ToggleSelectedBot());
            LaunchAllCommand = new RelayCommand(_ => ExecuteLaunchAll());

            // Set up refresh timer (1 second interval)
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        /// <summary>
        /// Stop background timer. Must be called on UI thread (app exit).
        /// </summary>
        public void Shutdown()
        {
            _refreshTimer.Stop();
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
                RefreshStatistics();
            }
            else
            {
                AddBotInstance(state);
            }
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

        /// <summary>Inject launcher cho Launch All (task 24).</summary>
        public void SetProcessLauncher(Services.ProcessLauncherService launcher)
        {
            _launcher = launcher;
            if (_launcher != null)
            {
                _launcher.Notify = (account, msg, isErr) =>
                    AppendLog(isErr ? "Error" : "Info", $"[Launcher] {account}: {msg}", Guid.Empty);
            }
        }

        // === Notifications (task 18) ===
        private Services.NotificationManager? _notifier;
        public Services.NotificationManager? Notifier => _notifier;

        public void SetNotifier(Services.NotificationManager notifier)
        {
            _notifier = notifier;
        }

        /// <summary>Launch tat ca profile da luu (task 24.4 bulk launch).</summary>
        private void ExecuteLaunchAll()
        {
            if (_launcher == null)
            {
                StatusMessage = "Launcher chua san sang.";
                return;
            }

            var profiles = _profileManager.LoadAllProfiles();
            if (profiles.Count == 0)
            {
                StatusMessage = "Chua co profile nao - dung Add Bot de tao truoc.";
                return;
            }

            int ok = 0, skip = 0, fail = 0;
            foreach (var p in profiles)
            {
                var result = _launcher.Launch(p);
                if (result.Success) ok++;
                else if (result.Message.Contains("đang chạy")) skip++;
                else { fail++; AppendLog("Error", $"[Launcher] {p.AccountName}: {result.Message}", Guid.Empty); }
            }

            StatusMessage = $"🚀 Launch All: {ok} mới, {skip} đang chạy, {fail} lỗi";
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
            if (GlobalPause)
            {
                StatusMessage = "⛔ GLOBAL PAUSE đang bật - tắt Global Pause trước khi Start All!";
                AppendLog("Warning", "Start All bị chặn do Global Pause", Guid.Empty);
                return;
            }
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
            if (GlobalPause) GlobalPause = false; // emergency tu dong tat pause de STOP di duoc
            GlobalCommandRequested?.Invoke(this, "EMERGENCY_STOP");
            StatusMessage = "EMERGENCY STOP - All bots halted!";
        }

        private void ExecuteAddBot()
        {
            try
            {
                var owner = Application.Current?.MainWindow;

                var dialog = new Views.BotProfileDialog(_profileManager)
                {
                    Owner = owner,
                };

                if (dialog.ShowDialog() != true || dialog.Profile == null)
                {
                    return;
                }

                StatusMessage = $"Đã lưu profile '{dialog.Profile.AccountName}'";

                if (dialog.LaunchGame)
                {
                    Views.BotProfileDialog.LaunchGameProcess(dialog.Profile);
                    StatusMessage = $"Đã lưu profile và launch game cho '{dialog.Profile.AccountName}'. Chờ bot kết nối...";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠ Lỗi mở dialog Add Bot: {ex.Message}";
            }
        }

        /// <summary>
        /// Them log vao panel (newest-first, gioi han MaxLogEntries dong).
        /// Phai goi tren UI thread.
        /// </summary>
        public void AppendLog(string level, string message, Guid instanceId)
        {
            LogEntries.Insert(0, new LogEntry
            {
                Level = level,
                Message = message,
                Source = ShortId(instanceId),
            });

            while (LogEntries.Count > MaxLogEntries)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        }

        // === Captcha queue (task 23.3) ===

        public void AddCaptcha(Guid instanceId)
        {
            var vm = BotInstances.FirstOrDefault(b => b.InstanceId == instanceId);
            CaptchaQueue.Insert(0, new CaptchaEntry
            {
                InstanceId = instanceId,
                Account = vm?.AccountName ?? ShortId(instanceId),
                Status = "Auto-solving (CNN)",
            });
            while (CaptchaQueue.Count > 50) CaptchaQueue.RemoveAt(CaptchaQueue.Count - 1);
        }

        public void MarkCaptchaStatus(Guid instanceId, string status)
        {
            var e = CaptchaQueue.FirstOrDefault(c => c.InstanceId == instanceId && c.Status.StartsWith("Auto"));
            if (e != null) e.Status = status;
        }

        /// <summary>Retry = bao bot resume de python solver thu lai o popup ke tiep.</summary>
        public void RetryCaptcha(CaptchaEntry entry)
        {
            if (_socketServer == null || !_socketServer.GetConnectedClients().Contains(entry.InstanceId))
            {
                StatusMessage = "Retry captcha that bai: bot offline.";
                return;
            }
            _socketServer.SendCommand(entry.InstanceId, Commands.RESUME);
            entry.Status = "Retry sent";
            StatusMessage = "Da gui RESUME cho bot de thu giai lai captcha.";
        }
        private static string ShortId(Guid id) =>
            id.ToString("N")[..8];

        private void BotInstance_CommandRequested(object? sender, string command)
        {
            if (sender is not BotInstanceViewModel botVm)
            {
                return;
            }

            // Mo dialog cau hinh va push config xuong bot (task 25 + 7.6).
            if (command == "OPEN_CONFIG")
            {
                OpenConfigDialog(botVm);
                return;
            }

            if (_socketServer == null)
            {
                StatusMessage = $"Khong gui duoc {command}: server chua khoi dong.";
                return;
            }

            if (!_socketServer.GetConnectedClients().Contains(botVm.InstanceId))
            {
                StatusMessage = $"Khong gui duoc {command}: {botVm.AccountName} da mat ket noi.";
                return;
            }

            // Send command to specific bot instance via SocketServer
            _socketServer.SendCommand(botVm.InstanceId, command);
            StatusMessage = $"Command {command} sent to {botVm.AccountName}";
        }

        private void OpenConfigDialog(BotInstanceViewModel botVm)
        {
            var owner = Application.Current?.MainWindow;

            // Prefill tu profile da luu (neu co), fallback = gia tri hien tai cua bot.
            var profile = _profileManager.ProfileExists(botVm.AccountName)
                ? _profileManager.LoadProfile(botVm.AccountName) ?? new BotProfile { AccountName = botVm.AccountName }
                : new BotProfile { AccountName = botVm.AccountName };

            var dialog = new Views.BotConfigDialog(profile, botVm.AccountName) { Owner = owner };
            if (dialog.ShowDialog() != true || dialog.Result == null)
            {
                return;
            }

            var updated = dialog.Result;

            try
            {
                _profileManager.SaveProfile(updated);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "Lưu profile thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_socketServer != null && _socketServer.GetConnectedClients().Contains(botVm.InstanceId))
            {
                _socketServer.SendConfigUpdate(botVm.InstanceId, updated);
                StatusMessage = $"Đã push config mới xuống '{botVm.AccountName}'.";
            }
            else
            {
                StatusMessage = $"Đã lưu config cho '{botVm.AccountName}' (bot offline - sẽ áp dụng lần kết nối sau).";
            }
        }

        // === INotifyPropertyChanged ===
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
