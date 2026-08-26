using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        public MainWindow(MainViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
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

        private void BtnAnalyticsRefresh_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Analytics.Refresh();
        }
    }
}
