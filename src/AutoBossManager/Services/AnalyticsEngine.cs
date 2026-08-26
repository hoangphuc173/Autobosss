using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AutoBossManager.Services
{
    /// <summary>
    /// Analytics engine theo doi hieu suat bot theo thoi gian thuc.
    /// - Ghi event boss kill / error / captcha vao rolling window (24h).
    /// - Tinh kills/hour, error rate, kills per instance de hien thi va export.
    /// Thread-safe: cac event den tu socket thread, truy van tu UI thread.
    /// </summary>
    public class AnalyticsEngine
    {
        private sealed record AnalyticsEvent(DateTime Timestamp, Guid InstanceId, EventKind Kind, string Detail);

        private enum EventKind { BossKilled, Error, Captcha }

        // Rolling window: chi giu du lieu 24h gan nhat.
        private static readonly TimeSpan Window = TimeSpan.FromHours(24);

        private readonly object _lock = new();
        private readonly List<AnalyticsEvent> _events = new();

        /// <summary>Ghi nhan mot boss kill.</summary>
        public void RecordBossKill(Guid instanceId, string bossName)
        {
            Add(new AnalyticsEvent(DateTime.Now, instanceId, EventKind.BossKilled, bossName));
        }

        /// <summary>Ghi nhan mot loi cua bot.</summary>
        public void RecordError(Guid instanceId, string message)
        {
            Add(new AnalyticsEvent(DateTime.Now, instanceId, EventKind.Error, message));
        }

        /// <summary>Ghi nhan bot gap captcha.</summary>
        public void RecordCaptcha(Guid instanceId)
        {
            Add(new AnalyticsEvent(DateTime.Now, instanceId, EventKind.Captcha, "captcha"));
        }

        private void Add(AnalyticsEvent e)
        {
            lock (_lock)
            {
                _events.Add(e);
                PruneLocked();
            }
        }

        private void PruneLocked()
        {
            var cutoff = DateTime.Now - Window;
            if (_events.Count > 0 && _events[0].Timestamp < cutoff)
            {
                _events.RemoveAll(e => e.Timestamp < cutoff);
            }
        }

        // === Truy van ===

        /// <summary>Tong so boss kill trong window.</summary>
        public int TotalKills
        {
            get
            {
                lock (_lock) { return _events.Count(e => e.Kind == EventKind.BossKilled); }
            }
        }

        /// <summary>Kills/gio trung binh toan he thong trong window.</summary>
        public double KillsPerHour
        {
            get
            {
                lock (_lock)
                {
                    var kills = _events.Where(e => e.Kind == EventKind.BossKilled).ToList();
                    if (kills.Count == 0) return 0;
                    var span = DateTime.Now - kills.Min(k => k.Timestamp);
                    var hours = Math.Max(span.TotalHours, 1.0 / 60.0); // toi thieu 1 phut de khong chia 0
                    return kills.Count / hours;
                }
            }
        }

        /// <summary>So loi trong window.</summary>
        public int TotalErrors
        {
            get
            {
                lock (_lock) { return _events.Count(e => e.Kind == EventKind.Error); }
            }
        }

        /// <summary>Ty le loi tren moi kill (thap = tot).</summary>
        public double ErrorsPerKill
        {
            get
            {
                lock (_lock)
                {
                    int kills = _events.Count(e => e.Kind == EventKind.BossKilled);
                    return kills == 0 ? 0 : (double)_events.Count(e => e.Kind == EventKind.Error) / kills;
                }
            }
        }

        /// <summary>Top N boss bi kill nhieu nhat trong window.</summary>
        public IReadOnlyList<(string BossName, int Count)> TopBosses(int top = 5)
        {
            lock (_lock)
            {
                return _events
                    .Where(e => e.Kind == EventKind.BossKilled)
                    .GroupBy(e => e.Detail)
                    .OrderByDescending(g => g.Count())
                    .Take(top)
                    .Select(g => (g.Key, g.Count()))
                    .ToList();
            }
        }

        /// <summary>Snapshot toan bo metrics dang text ngan gon (cho StatusMessage/log).</summary>
        public override string ToString()
        {
            lock (_lock)
            {
                return $"Analytics: {TotalKills} kills ({KillsPerHour:F1}/h), {TotalErrors} errors ({ErrorsPerKill:F2}/kill)";
            }
        }

        // === Du lieu cho Analytics Dashboard (task 16.3) ===

        /// <summary>
        /// So kill theo tung gio trong window (moi nhat cuoi).
        /// Tra ve dung so bucket = hours; bucket khong co kill = 0.
        /// </summary>
        public IReadOnlyList<(string HourLabel, int Count)> GetKillsPerHourBuckets(int hours = 12)
        {
            var result = new List<(string, int)>(hours);
            var now = DateTime.Now;
            var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

            lock (_lock)
            {
                for (int i = hours - 1; i >= 0; i--)
                {
                    var start = hourStart.AddHours(-i);
                    var end = start.AddHours(1);
                    int count = _events.Count(e =>
                        e.Kind == EventKind.BossKilled &&
                        e.Timestamp >= start && e.Timestamp < end);
                    result.Add((start.ToString("HH:mm"), count));
                }
            }
            return result;
        }

        /// <summary>Tong kill cua tung bot instance (short id), sap xep giam dan.</summary>
        public IReadOnlyList<(string InstanceId, int Kills)> GetKillsByInstance()
        {
            lock (_lock)
            {
                return _events
                    .Where(e => e.Kind == EventKind.BossKilled)
                    .GroupBy(e => e.InstanceId.ToString("N")[..8])
                    .OrderByDescending(g => g.Count())
                    .Select(g => (g.Key, g.Count()))
                    .ToList();
            }
        }

        /// <summary>Xuat toan bo event dang giu thanh CSV (boss kills chi tiet).</summary>
        public string ExportCsv()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("timestamp,instanceId,kind,detail");
            lock (_lock)
            {
                foreach (var e in _events.OrderBy(e => e.Timestamp))
                {
                    var detail = (e.Detail ?? "").Replace('"', '\'');
                    sb.AppendLine($"{e.Timestamp:yyyy-MM-dd HH:mm:ss},{e.InstanceId:N},{e.Kind},\"{detail}\"");
                }
            }
            return sb.ToString();
        }
    }
}
