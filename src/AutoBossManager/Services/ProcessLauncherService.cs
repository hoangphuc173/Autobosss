using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using AutoBossShared;

namespace AutoBossManager.Services
{
    /// <summary>
    /// Ket qua launch mot game instance.
    /// </summary>
    public class LaunchResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public string Account { get; init; } = "";
    }

    /// <summary>
    /// Multi-instance launcher tu Manager (task 24 cua spec):
    /// - LaunchProfile: mo game exe theo profile, track PID theo account (24.1)
    /// - Auto-restart khi process thoat bat thuong, gioi han MaxRestartAttempts (24.2)
    /// - Chan launch trung cung account (24.3)
    /// Thread-safe.
    /// </summary>
    public class ProcessLauncherService
    {
        public class TrackedProcess
        {
            public string Account { get; }
            public Process Process { get; }
            public int RestartCount;

            public TrackedProcess(string account, Process process, int restartCount)
            {
                Account = account;
                Process = process;
                RestartCount = restartCount;
            }
        }

        private sealed record TrackedEntry(string Account, Process Process, int RestartCount);

        private readonly object _lock = new();
        private readonly Dictionary<string, TrackedProcess> _tracked = new();

        /// <summary>Callback thong bao su kien cho UI/log (account, message, isError).</summary>
        public Action<string, string, bool>? Notify { get; set; }

        public IReadOnlyList<(string Account, int Pid, int Restarts)> Snapshot()
        {
            lock (_lock)
                return _tracked.Values
                    .Where(t => !t.Process.HasExited)
                    .Select(t => (t.Account, t.Process.Id, t.RestartCount))
                    .ToList();
        }

        public bool IsRunning(string account)
        {
            lock (_lock)
            {
                return _tracked.TryGetValue(account ?? "", out var t) && !t.Process.HasExited;
            }
        }

        /// <summary>Launch game cho profile. Tra ve ket qua kem thong bao.</summary>
        public LaunchResult Launch(BotProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.AccountName))
                return new LaunchResult { Success = false, Message = "Profile thiếu AccountName" };

            var account = profile.AccountName;

            if (!File.Exists(profile.GameExecutablePath))
                return new LaunchResult { Success = false, Account = account,
                    Message = $"Không tìm thấy game exe: {profile.GameExecutablePath}" };

            lock (_lock)
            {
                // 24.3: chan trung instance cung account
                if (_tracked.TryGetValue(account, out var existing) && !existing.Process.HasExited)
                    return new LaunchResult { Success = false, Account = account,
                        Message = $"Account '{account}' đang chạy (PID {existing.Process.Id}) - không launch trùng" };

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = profile.GameExecutablePath,
                        Arguments = $"--account \"{account}\"",
                        WorkingDirectory = Path.GetDirectoryName(profile.GameExecutablePath)!,
                        UseShellExecute = true,
                    };
                    var proc = Process.Start(psi);
                    if (proc == null)
                        return new LaunchResult { Success = false, Account = account, Message = "Process.Start trả về null" };

                    var restartBase = _tracked.TryGetValue(account, out var old) ? old.RestartCount : 0;
                    var tracked = new TrackedProcess(account, proc, restartBase);

                    // 24.2: auto-restart khi crash neu profile bat co
                    if (profile.AutoRestartOnCrash)
                    {
                        proc.EnableRaisingEvents = true;
                        proc.Exited += (_, _) => OnProcessExited(tracked, profile);
                    }

                    _tracked[account] = tracked;
                    Notify?.Invoke(account, $"Launched game PID={proc.Id}", false);
                    return new LaunchResult { Success = true, Account = account, Message = $"Launched PID {proc.Id}" };
                }
                catch (Exception ex)
                {
                    return new LaunchResult { Success = false, Account = account, Message = $"Launch fail: {ex.Message}" };
                }
            }
        }

        private void OnProcessExited(TrackedProcess tracked, BotProfile profile)
        {
            // ExitCode != 0 hoac bi kill = crash -> restart (task 24.2 / REQ 5.7)
            int code = 0;
            try { code = tracked.Process.ExitCode; } catch { }

            if (code == 0)
            {
                Notify?.Invoke(tracked.Account, "Game exited normally - no auto-restart", false);
                return;
            }

            int attempts;
            lock (_lock)
            {
                attempts = ++tracked.RestartCount;
            }

            if (attempts > Math.Max(1, profile.MaxRestartAttempts))
            {
                Notify?.Invoke(tracked.Account,
                    $"CRASHED và đã hết lượt restart ({profile.MaxRestartAttempts}) - cần can thiệp thủ công", true);
                return;
            }

            Notify?.Invoke(tracked.Account,
                $"Crashed (exit={code}) -> auto-restart lần {attempts}/{profile.MaxRestartAttempts} sau 5s", true);

            // Restart trong thread rieng de khong block event thread
            new Thread(() =>
            {
                Thread.Sleep(5000);
                Launch(profile);
            })
            { IsBackground = true }.Start();
        }
    }
}
