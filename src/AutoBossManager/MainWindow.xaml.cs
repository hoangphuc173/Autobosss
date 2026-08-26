using System;
using System.ComponentModel;
using System.Linq;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AutoBossManager.Services;
using AutoBossManager.ViewModels;

namespace AutoBossManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// Main window that displays the bot instance dashboard and realtime log panel.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private ICollectionView? _logView;
        private readonly LogAggregator? _logAggregator;

        public MainWindow(MainViewModel viewModel, LogAggregator? logAggregator = null)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _logAggregator = logAggregator;
            DataContext = _viewModel;

            InitializeComponent();

            // Filter log list theo level duoc chon o ComboBox.
            _logView = CollectionViewSource.GetDefaultView(_viewModel.LogEntries);
            _logView.Filter = FilterLogEntry;
            LstLogs.ItemsSource = _logView;
        }

        private bool FilterLogEntry(object item)
        {
            if (CmbLogFilter?.SelectedItem is ComboBoxItem { Content: string level }
                && level != "All"
                && item is LogEntry entry)
            {
                return entry.Level.Equals(level, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        private void CmbLogFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _logView?.Refresh();
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.LogEntries.Clear();
        }

        private void TxtLogSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SearchPersistedLogs(TxtLogSearch.Text);
                e.Handled = true;
            }
        }

        private void BtnExportLogs_Click(object sender, RoutedEventArgs e)
        {
            var keyword = string.IsNullOrWhiteSpace(TxtLogSearch.Text) ? null : TxtLogSearch.Text.Trim();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text|*.txt|JSONL|*.jsonl",
                FileName = $"autoboss_logs_{DateTime.Now:yyyyMMdd_HHmm}.txt",
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var count = _logAggregator?.ExportToText(dlg.FileName, keyword) ?? 0;
                MessageBox.Show(this, $"Đã export {count} dòng log.\n{dlg.FileName}",
                    "Export logs", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Tim trong file log da luu (task 17.2). Ket qua hien preview toi da 200 dong.
        /// </summary>
        private void SearchPersistedLogs(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword) || _logAggregator == null)
            {
                return; // khong co keyword -> dung level combo cho filter realtime
            }

            try
            {
                var rows = _logAggregator.ReadFiltered(keyword: keyword);
                const int maxShow = 200;
                var preview = string.Join("\n", rows.Take(maxShow).Select(r => r.Line));
                MessageBox.Show(this,
                    $"Tìm thấy {rows.Count} dòng chứa '{keyword}'" +
                    (rows.Count > maxShow ? $" (hiển thị {maxShow} đầu)" : "") + ":\n\n" + preview,
                    "Kết quả tìm kiếm (file đã lưu)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Search thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnRetryCaptcha_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CaptchaEntry entry })
            {
                _viewModel.RetryCaptcha(entry);
            }
        }

        private void BtnAnalyticsRefresh_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Analytics.Refresh();
        }

        private void BtnProfilesRefresh_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshProfiles();
        private void BtnProfileLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AutoBossShared.BotProfile p }) _viewModel.LaunchProfile(p);
        }
        private void BtnProfileEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AutoBossShared.BotProfile p }) _viewModel.EditProfile(p);
        }
        private void BtnProfileDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AutoBossShared.BotProfile p }) _viewModel.DeleteProfile(p);
        }

        // ============ Notifications (task 18) ============

        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private Services.NotificationManager? _notifier;

        /// <summary>Goi tu App sau khi DI tao xong de nhan notification events.</summary>
        public void AttachNotifier(Services.NotificationManager notifier)
        {
            _notifier = notifier;
            notifier.OnNotificationAllowed += Notifier_OnNotificationAllowed;

            // System tray icon (task 18.3)
            try
            {
                _trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Application,
                    Text = "AutoBoss Manager",
                    Visible = true,
                };
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Show", null, (_, _) => { Show(); WindowState = WindowState.Normal; });
                menu.Items.Add("Exit", null, (_, _) => Close());
                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; };
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_notifier != null) _notifier.OnNotificationAllowed -= Notifier_OnNotificationAllowed;
            base.OnClosed(e);
        }

        private void Notifier_OnNotificationAllowed(object? sender, Services.NotificationManager.Notification n)
        {
            Dispatcher.Invoke(() =>
            {
                ShowToast(n);
                if (n.HighPriority) ShowBalloon(n);
            });
        }

        private void ShowToast(Services.NotificationManager.Notification n)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                        n.HighPriority ? "#EF4444EE" : "#3B82F6EE")),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = $"[{n.Type}] {n.Account}\n{n.Message}",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                },
            };

            ToastPanel.Children.Add(border);
            if (ToastPanel.Children.Count > 4)
                ToastPanel.Children.RemoveAt(0);

            // Auto-dismiss sau 5s (task 18.2)
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5),
            };
            timer.Tick += (_, _) =>
            {
                ToastPanel.Children.Remove(border);
                timer.Stop();
            };
            timer.Start();
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e) => ShowNotificationHistory();

        private void ShowBalloon(Services.NotificationManager.Notification n)
        {
            try
            {
                _trayIcon?.ShowBalloonTip(4000, $"[{n.Type}] {n.Account}", n.Message,
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
            catch { }
        }

        /// <summary>Hien history thong bao (task 18.5) - gan nut 🔔 goi.</summary>
        public void ShowNotificationHistory()
        {
            if (_notifier == null) return;
            var hist = _notifier.History;
            var lines = hist.Count == 0
                ? "(chưa có thông báo nào)"
                : string.Join("\n", hist.Reverse().Select(h => $"[{h.TimeFormatted}] [{h.Type}] {h.Account}: {h.Message}"));
            MessageBox.Show(this, lines, $"🔔 History ({hist.Count}/50)",
                MessageBoxButton.OK, MessageBoxImage.None);
        }
    }
}
