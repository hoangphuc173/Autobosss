# Bugfix Requirements Document

## Introduction

The AutoBossManager WPF application displays buttons correctly but they do not respond when clicked. All five main control buttons (Start All, Stop All, Emergency Stop, Refresh, Add Bot) are visible in the UI but produce no response when users click them. The DataGrid row buttons for individual bot control also exhibit the same non-responsive behavior. This renders the entire management interface non-functional, preventing users from controlling bot instances or sending IPC commands to game clients.

The root cause is that the MainWindow's DataContext is not properly set to the MainViewModel instance during window initialization, causing all command bindings to fail silently. While the DataContext assignment exists in the constructor (`DataContext = _viewModel`), the WPF binding engine cannot resolve the command bindings at runtime.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a user clicks any button in the bottom control bar (Start All, Stop All, Emergency Stop, Refresh, Add Bot) THEN the system produces no response, no command execution occurs, and no status message update happens

1.2 WHEN a user clicks any DataGrid row action button (Start, Stop, Pause, Resume) THEN the system produces no response and no command is sent to the corresponding bot instance

1.3 WHEN the MainWindow is initialized with the MainViewModel injected via constructor THEN the DataContext binding does not propagate to command bindings in XAML

1.4 WHEN command bindings attempt to resolve at runtime THEN they fail silently because the binding path cannot locate the command properties on the DataContext

### Expected Behavior (Correct)

2.1 WHEN a user clicks the "Start All" button THEN the system SHALL execute the StartAllCommand, trigger the GlobalCommandRequested event with "START_ALL", broadcast the command via SocketServer, and update StatusMessage to "Starting all bot instances..."

2.2 WHEN a user clicks the "Stop All" button THEN the system SHALL execute the StopAllCommand, trigger the GlobalCommandRequested event with "STOP_ALL", broadcast the command via SocketServer, and update StatusMessage to "Stopping all bot instances..."

2.3 WHEN a user clicks the "Emergency Stop" button THEN the system SHALL execute the EmergencyStopCommand, trigger the GlobalCommandRequested event with "EMERGENCY_STOP", broadcast the command via SocketServer, and update StatusMessage to "EMERGENCY STOP - All bots halted!"

2.4 WHEN a user clicks the "Refresh" button THEN the system SHALL execute the RefreshCommand, recalculate aggregate statistics (ConnectedClientCount, TotalBossKills, TotalUptime, AverageBossKillsPerHour), and update all displayed metrics

2.5 WHEN a user clicks the "Add Bot" button THEN the system SHALL execute the AddBotCommand and update StatusMessage to "Add Bot feature coming in Task 7..."

2.6 WHEN a user clicks a DataGrid row action button (Start, Stop, Pause, Resume) THEN the system SHALL execute the corresponding BotInstanceViewModel command, send the IPC message via SocketServer to the specific bot instance, and update StatusMessage with confirmation

2.7 WHEN the MainWindow is initialized with dependency-injected MainViewModel THEN the DataContext SHALL be properly set such that all XAML command bindings resolve successfully at runtime

### Unchanged Behavior (Regression Prevention)

3.1 WHEN the application starts and displays the MainWindow THEN the system SHALL CONTINUE TO render the UI correctly with all buttons visible

3.2 WHEN sample data is initialized in MainViewModel.InitializeSampleData() THEN the system SHALL CONTINUE TO display three test bot instances in the DataGrid

3.3 WHEN the DataGrid binds to BotInstances ObservableCollection THEN the system SHALL CONTINUE TO display bot data correctly (account names, status, HP/MP percentages, kill counts, uptime)

3.4 WHEN aggregate statistics are calculated THEN the system SHALL CONTINUE TO display ConnectedClientCount, TotalBossKills, TotalUptime, and AverageBossKillsPerHour correctly in the header bar

3.5 WHEN the refresh timer ticks every second THEN the system SHALL CONTINUE TO update time-based properties without errors

3.6 WHEN PropertyChanged events are raised in MainViewModel THEN the system SHALL CONTINUE TO update bound UI elements in the header bar and status message

3.7 WHEN SocketServer events are wired up in App.xaml.cs THEN the system SHALL CONTINUE TO maintain event handler connections for status updates, client connections, boss notifications, and error events
