using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AutoBossManager.ViewModels;
using AutoBossManager.Services;
using AutoBossShared;

namespace AutoBossManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// Sets up dependency injection container for MVVM services
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        public App()
        {
            // Configure dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Register services
            services.AddSingleton<ProfileManager>();
            services.AddSingleton<LogAggregator>();
            services.AddSingleton<ProcessLauncherService>();
            services.AddSingleton<NotificationManager>();
            services.AddSingleton<AnalyticsEngine>();
            services.AddSingleton<SocketServer>();

            // Register ViewModels
            services.AddSingleton<MainViewModel>();

            // Register MainWindow with factory that wires up SocketServer events
            services.AddSingleton<MainWindow>(provider =>
            {
                var mainViewModel = provider.GetRequiredService<MainViewModel>();
                var socketServer = provider.GetRequiredService<SocketServer>();
                var analytics = provider.GetRequiredService<AnalyticsEngine>();
                var logAggregator = provider.GetRequiredService<LogAggregator>();
                var notifier = provider.GetRequiredService<NotificationManager>();
                mainViewModel.SetNotifier(notifier);
                mainViewModel.SetProcessLauncher(provider.GetRequiredService<ProcessLauncherService>());
                var mainWindow = new MainWindow(mainViewModel, logAggregator);
                mainWindow.AttachNotifier(notifier);

            // Wire up SocketServer events to MainViewModel + AnalyticsEngine
            WireUpSocketServerEvents(socketServer, mainViewModel, analytics, logAggregator, mainWindow);
            ViewModels.AnalyticsViewModel.MainStatusProxy =
                msg => mainViewModel.StatusMessage = msg;

                return mainWindow;
            });
        }

        /// <summary>
        /// Wire up SocketServer events to MainViewModel for real-time updates.
        /// Task 7.6: Integration with MainViewModel
        /// </summary>
        private void WireUpSocketServerEvents(SocketServer socketServer, MainViewModel mainViewModel, AnalyticsEngine analytics, LogAggregator logAggregator, MainWindow mainWindow)
        {
            // Status updates - update bot instance state
            socketServer.OnStatusUpdate += (sender, e) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.UpdateBotInstance(e.State);
                });
            };

            // Client connected - add placeholder so UI updates instantly (detailed state arrives via STATUS_UPDATE)
            socketServer.OnClientConnected += (sender, e) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.AppendLog("Info", "Bot connected", e.InstanceId);
                    mainViewModel.StatusMessage = $"Client connected: {e.InstanceId}";
                    // Add placeholder entry immediately; OnStatusUpdate will fill details
                    var now = DateTime.Now;
                    var placeholder = new BotInstanceState
                    {
                        InstanceId = e.InstanceId,
                        Status = ConnectionStatus.Connected,
                        CurrentState = AutoBossState.Idle,
                        AccountName = $"Bot-{e.InstanceId.ToString("N")[..6]}",
                        CurrentMap = "(connecting...)",
                        SessionStartTime = now,
                        LastHeartbeat = now,
                    };
                    mainViewModel.AddOrUpdatePlaceholder(placeholder);
                });
            };

            // Client disconnected - remove bot instance
            socketServer.OnClientDisconnected += (sender, instanceId) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.AppendLog("Warning", "Bot disconnected", instanceId);
                    mainViewModel.RemoveBotInstance(instanceId);
                    mainViewModel.StatusMessage = $"Client disconnected: {instanceId}";
                });
            };

            // Boss found notifications
            socketServer.OnBossFound += (sender, e) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.AppendLog("Info", $"BOSS FOUND: {e.BossName} at {e.MapName} {e.ZoneName}", e.InstanceId);
                    mainViewModel.StatusMessage = $"Boss found: {e.BossName} at {e.MapName} {e.ZoneName}";
                });
            };

            // Boss killed - record into analytics
            socketServer.OnBossKilled += (sender, e) =>
            {
                analytics.RecordBossKill(e.InstanceId, e.BossName);
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.AppendLog("Info", $"BOSS KILLED: {e.BossName} ({e.KillDurationSec:F1}s)", e.InstanceId);
                });
            };

            // Log events - surface in Logs tab + status bar
            socketServer.OnLogEvent += (sender, e) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.AppendLog(e.Level, e.Message, e.InstanceId);
                    mainViewModel.StatusMessage = $"[{e.Level}] {e.Message}";
                });
            };

            // Error events - record into analytics for error-rate tracking
            socketServer.OnError += (sender, e) =>
            {
                analytics.RecordError(e.InstanceId, e.Message);
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.AppendLog("Error", e.Message, e.InstanceId);
                    mainViewModel.StatusMessage = $"ERROR [{e.InstanceId}]: {e.Message}";
                });
            };

            // Wire up MainViewModel global commands to SocketServer
            mainViewModel.GlobalCommandRequested += (sender, command) =>
            {
                switch (command)
                {
                    case "START_ALL":
                        socketServer.BroadcastCommand(Commands.START_FARMING);
                        break;
                    case "STOP_ALL":
                        socketServer.BroadcastCommand(Commands.STOP_FARMING);
                        break;
                    case "EMERGENCY_STOP":
                        socketServer.BroadcastCommand(Commands.STOP_FARMING);
                        break;
                }
            };

            // Wire up individual bot commands through MainViewModel
            // The MainViewModel.BotInstance_CommandRequested handler will need access to SocketServer
            // We'll pass the SocketServer reference to MainViewModel
            mainViewModel.SetSocketServer(socketServer);

            // Start the socket server
            try
            {
                socketServer.Start();
                mainViewModel.StatusMessage = "Socket server started on port 28081";
                mainViewModel.AppendLog("Info", "IPC server listening tai 127.0.0.1:28081", Guid.Empty);
                mainViewModel.AppendLog("Info", "Manager kho dong - cho bot ket noi...", Guid.Empty);
            }
            catch (Exception ex)
            {
                mainViewModel.StatusMessage = $"Failed to start socket server: {ex.Message}";
                mainViewModel.AppendLog("Error", $"Khong mo duoc port 28081: {ex.Message}", Guid.Empty);
                MessageBox.Show($"Failed to start socket server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string ShortId(Guid id) => id.ToString("N")[..Math.Min(8, id.ToString("N").Length)];

    protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Get and show MainWindow from DI container
            var mainWindow = _serviceProvider?.GetService<MainWindow>();
            mainWindow?.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Stop UI refresh timer first (must run on UI thread)
            _serviceProvider?.GetService<MainViewModel>()?.Shutdown();

            // Flush + close log file handles (task 17.8 graceful shutdown)
            _serviceProvider?.GetService<LogAggregator>()?.Dispose();

            // Stop SocketServer before disposing services
            var socketServer = _serviceProvider?.GetService<SocketServer>();
            socketServer?.Stop();

            // Cleanup
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}
