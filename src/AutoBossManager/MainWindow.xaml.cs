using System;
using System.ComponentModel;
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

        private void BtnAnalyticsRefresh_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Analytics.Refresh();
        }
    }
}
