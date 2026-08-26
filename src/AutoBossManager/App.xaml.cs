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
                var mainWindow = new MainWindow(mainViewModel);

                // Wire up SocketServer events to MainViewModel + AnalyticsEngine
                WireUpSocketServerEvents(socketServer, mainViewModel, analytics, mainWindow);

                return mainWindow;
            });
        }

        /// <summary>
        /// Wire up SocketServer events to MainViewModel for real-time updates.
        /// Task 7.6: Integration with MainViewModel
        /// </summary>
        private void WireUpSocketServerEvents(SocketServer socketServer, MainViewModel mainViewModel, AnalyticsEngine analytics, MainWindow mainWindow)
        {
            // Status updates - update bot instance state
            socketServer.OnStatusUpdate += (sender, e) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.UpdateBotInstance(e.State);
                });
            };

            // Client connected - add new bot instance
            socketServer.OnClientConnected += (sender, e) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.StatusMessage = $"Client connected: {e.InstanceId}";
                });
            };

            // Client disconnected - remove bot instance
            socketServer.OnClientDisconnected += (sender, instanceId) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.RemoveBotInstance(instanceId);
                    mainViewModel.StatusMessage = $"Client disconnected: {instanceId}";
                });
            };

            // Boss found notifications
            socketServer.OnBossFound += (sender, e) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.StatusMessage = $"Boss found: {e.BossName} at {e.MapName} {e.ZoneName}";
                });
            };

            // Boss killed - record into analytics
            socketServer.OnBossKilled += (sender, e) =>
            {
                analytics.RecordBossKill(e.InstanceId, e.BossName);
            };

            // Log events - surface in status bar (Console.WriteLine is invisible in a WPF app)
            socketServer.OnLogEvent += (sender, e) =>
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainViewModel.StatusMessage = $"[{e.Level}] {e.Message}";
                });
            };

            // Error events - record into analytics for error-rate tracking
            socketServer.OnError += (sender, e) =>
            {
                analytics.RecordError(e.InstanceId, e.Message);
                mainWindow.Dispatcher.Invoke(() =>
                {
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
            }
            catch (Exception ex)
            {
                mainViewModel.StatusMessage = $"Failed to start socket server: {ex.Message}";
                MessageBox.Show($"Failed to start socket server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

            // Stop SocketServer before disposing services
            var socketServer = _serviceProvider?.GetService<SocketServer>();
            socketServer?.Stop();

            // Cleanup
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}
