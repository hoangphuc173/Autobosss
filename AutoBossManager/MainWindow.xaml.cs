using System;
using System.Windows;
using AutoBossManager.ViewModels;

namespace AutoBossManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// Main window that displays the bot instance dashboard
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            
            InitializeComponent();
        }
    }
}
