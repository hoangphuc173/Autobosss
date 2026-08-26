using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoBossManager.Services
{
    /// <summary>
    /// Mot dong log duoc luu lai (model chung cho UI + file).
    /// </summary>
    public class AggregatedLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Level { get; set; } = "Info";
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Log aggregator tap trung (task 17 cua spec):
    /// - Ghi moi entry vao file JSON theo ngay: logs/autoboss_2026-08-26.jsonl (moi dong 1 JSON)
    /// - Rotation khi file vuot 50MB -> doi ten .old
    /// - Tu dong xoa file log cu hon 7 ngay
    /// - Search/filter theo keyword + level + bot
    /// File ghi append-only nen an toan voi crash.
    /// </summary>
    public class LogAggregator : IDisposable
    {
        private const long MaxFileBytes = 50L * 1024 * 1024; // 50MB (REQ 9.5)
        private static readonly TimeSpan Retention = TimeSpan.FromDays(7); // REQ 9.6

        private readonly object _lock = new();
        private readonly string _dir;
        private StreamWriter? _writer;
        private DateTime _currentFileDate;
        private string _currentPath = "";

        public LogAggregator(string? dir = null)
        {
            _dir = dir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoBossManager", "logs");
            Directory.CreateDirectory(_dir);
            CleanupOldFiles();
        }

        public string Directory_ => _dir;

        private string PathFor(DateTime date) =>
            Path.Combine(_dir, $"autoboss_{date:yyyy-MM-dd}.jsonl");

        /// <summary>Ghi 1 entry. Thread-safe.</summary>
        public void Add(AggregatedLogEntry entry)
        {
            if (entry == null) return;
            try
            {
                lock (_lock)
                {
                    EnsureWriterLocked(entry.Timestamp);
                    var line = Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        ts = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        lvl = entry.Level,
                        src = entry.Source,
                        msg = entry.Message,
                    });
                    _writer!.WriteLine(line);
                    // Flush ngay - log phai ben qua crash va ReadFiltered doc song song
                    _writer.Flush();

                    // Rotation (REQ 9.5)
                    if (_writer.BaseStream.Length > MaxFileBytes)
                    {
                        RotateLocked();
                    }
                }
            }
            catch
            {
                // Khong bao gio de exception log lam chet app
            }
        }

        private void EnsureWriterLocked(DateTime ts)
        {
            var today = ts.Date;
            if (_writer != null && _currentFileDate == today) return;

            _writer?.Dispose();
            _currentFileDate = today;
            _currentPath = PathFor(today);
            _writer = new StreamWriter(File.Open(_currentPath,
                FileMode.Append, FileAccess.Write, FileShare.Read));
        }

        private void RotateLocked()
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            var rotated = _currentPath + $".rot_{DateTime.Now:HHmmss}.old";
            if (File.Exists(_currentPath))
            {
                File.Move(_currentPath, rotated);
            }
        }

        /// <summary>Doc tat ca log trong window theo filter (task 17.2 search).</summary>
        public List<(string File, string Line)> ReadFiltered(
            string? keyword = null, string? level = null, string? sourceContains = null)
        {
            var results = new List<(string, string)>();
            try
            {
                lock (_lock)
                {
                    foreach (var file in Directory.GetFiles(_dir, "autoboss_*.jsonl*")
                                 .OrderBy(f => f))
                    {
                        // File dang duoc writer giu -> phai mo voi FileShare.ReadWrite
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var sr = new StreamReader(fs);
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (!string.IsNullOrEmpty(level) &&
                                !line.Contains($"\"lvl\":\"{level}\"", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!string.IsNullOrEmpty(sourceContains) &&
                                !line.Contains(sourceContains, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!string.IsNullOrEmpty(keyword) &&
                                !line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                                continue;
                            results.Add((Path.GetFileName(file), line));
                        }
                    }
                }
            }
            catch
            {
                // Khong bao gio de exception log lam chet app
            }
            return results;
        }

        /// <summary>Xuat log da filter ra file text (task 17.2 export button).</summary>
        public int ExportToText(string filePath, string? keyword = null, string? level = null)
        {
            var rows = ReadFiltered(keyword, level);
            File.WriteAllLines(filePath, rows.Select(r => r.Line));
            return rows.Count;
        }

        /// <summary>Xoa file log cu hon retention (REQ 9.6). Goi khi khoi dong + hang ngay.</summary>
        public void CleanupOldFiles()
        {
            try
            {
                var cutoff = DateTime.Now - Retention;
                foreach (var f in Directory.GetFiles(_dir, "autoboss_*"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < cutoff)
                            File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try { _writer?.Flush(); _writer?.Dispose(); } catch { }
                _writer = null;
            }
        }
    }
}
