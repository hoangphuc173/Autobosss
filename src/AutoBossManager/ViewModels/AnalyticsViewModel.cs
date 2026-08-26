using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AutoBossManager.Helpers;
using AutoBossManager.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace AutoBossManager.ViewModels
{
    /// <summary>
    /// ViewModel cho tab Analytics (task 16.3): line chart kills/gio,
    /// bar chart so sanh bot, top bosses, export CSV.
    /// Du lieu lay tu AnalyticsEngine (rolling 24h window).
    /// </summary>
    public class AnalyticsViewModel : INotifyPropertyChanged
    {
        private readonly AnalyticsEngine _engine;

        private ISeries[] _series = Array.Empty<ISeries>();
        private Axis[] _xAxes = Array.Empty<Axis>();
        private string _summary = "Chua co du lieu";
        private string _topBosses = "-";

        public AnalyticsViewModel(AnalyticsEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            ExportCsvCommand = new RelayCommand(_ => ExecuteExportCsv());
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // === Charts ===

        public ISeries[] Series
        {
            get => _series;
            private set { _series = value; OnPropertyChanged(); }
        }

        public Axis[] XAxes
        {
            get => _xAxes;
            private set { _xAxes = value; OnPropertyChanged(); }
        }

        // === Summary ===

        public string Summary
        {
            get => _summary;
            private set { _summary = value; OnPropertyChanged(); }
        }

        public string TopBossesText
        {
            get => _topBosses;
            private set { _topBosses = value; OnPropertyChanged(); }
        }

        // === Commands ===
        public ICommand ExportCsvCommand { get; }

        /// <summary>Tai lai charts tu engine. Goi dinh ky / khi mo tab.</summary>
        public void Refresh()
        {
            var buckets = _engine.GetKillsPerHourBuckets(hours: 12);

            Series = new ISeries[]
            {
                new LineSeries<int>
                {
                    Name = "Kills/hour",
                    Values = buckets.Select(b => b.Count).ToArray(),
                    Fill = null,
                    GeometrySize = 6,
                    Stroke = new SolidColorPaint(new SKColor(59, 130, 246)) { StrokeThickness = 2 },
                },
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = buckets.Select(b => b.HourLabel).ToArray(),
                    LabelsRotation = 0,
                },
            };

            var byInstance = _engine.GetKillsByInstance();
            var top = _engine.TopBosses(3);
            Summary = $"{_engine.TotalKills} kills · {_engine.KillsPerHour:F1}/h · {_engine.TotalErrors} errors";

            TopBossesText = top.Count == 0
                ? "-"
                : string.Join(" · ", top.Select(t => $"{t.BossName} ×{t.Count}"));

            // Bar series thu 2: so kill theo bot (tren cung truc)
            if (byInstance.Count > 0)
            {
                Series = new ISeries[]
                {
                    new LineSeries<int>
                    {
                        Name = "Kills/hour",
                        Values = buckets.Select(b => b.Count).ToArray(),
                        Fill = null,
                        GeometrySize = 6,
                        Stroke = new SolidColorPaint(new SKColor(59, 130, 246)) { StrokeThickness = 2 },
                    },
                    new ColumnSeries<int>
                    {
                        Name = "Kills per bot",
                        Values = byInstance.Select(b => b.Kills).ToArray(),
                        Stroke = null,
                        Fill = new SolidColorPaint(new SKColor(16, 185, 129)),
                    },
                };

                // Ghep nhan bot vao truc X cho de doc
                var labels = buckets.Select(b => b.HourLabel).ToList();
                labels.Add("bots: " + string.Join("/", byInstance.Select(b => b.InstanceId)));
                XAxes = new Axis[] { new Axis { Labels = labels.ToArray() } };
            }
        }

        private void ExecuteExportCsv()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV|*.csv",
                    FileName = $"autoboss_analytics_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                };
                if (dlg.ShowDialog() != true) return;

                File.WriteAllText(dlg.FileName, _engine.ExportCsv());
                MainStatusProxy?.Invoke($"Đã export analytics → {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MainStatusProxy?.Invoke($"⚠ Export CSV thất bại: {ex.Message}");
            }
        }

        /// <summary>Callback de hien thong bao len StatusMessage cua MainViewModel.</summary>
        public static Action<string>? MainStatusProxy { get; set; }
    }
}
