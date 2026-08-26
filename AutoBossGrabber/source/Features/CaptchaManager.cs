using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace AutoBossGrabber;

public static class CaptchaManager
{
    private static Process _pythonProcess;
    private static bool _isRunning = false;
    
    public static bool IsSolving { get; private set; } = false;
    public static float LastSolvingActivity { get; private set; } = 0f;
    // Lấy đường dẫn gốc của game
    private static string GameRoot => BepInEx.Paths.GameRootPath;
    private static string PythonExePath => Path.Combine(GameRoot, @"PyCaptcha\.venv\Scripts\python.exe");
    private static string ScriptPath => Path.Combine(GameRoot, @"PyCaptcha\src\gamebot\tools\run_bot.py");
    private static string WorkingDirectory => Path.Combine(GameRoot, @"PyCaptcha");
    
    // Đường dẫn exe nếu đã được đóng gói bằng PyInstaller
    private static string StandaloneExePath => Path.Combine(GameRoot, @"PyCaptcha\dist\run_bot\run_bot.exe");

    public static void StartPythonBot()
    {
        if (_isRunning || _pythonProcess != null && !_pythonProcess.HasExited)
        {
            return;
        }

        try
        {
            _pythonProcess = new Process();
            
            if (File.Exists(StandaloneExePath))
            {
                _pythonProcess.StartInfo.FileName = StandaloneExePath;
                _pythonProcess.StartInfo.Arguments = "5 default";
                Plugin.Log?.LogInfo("[CaptchaManager] Dùng bản PyCaptcha Standalone (exe).");
            }
            else
            {
                if (!File.Exists(PythonExePath) || !File.Exists(ScriptPath))
                {
                    Plugin.Log?.LogError("[CaptchaManager] Không tìm thấy Python hoặc file run_bot.exe.");
                    return;
                }
                _pythonProcess.StartInfo.FileName = PythonExePath;
                _pythonProcess.StartInfo.Arguments = $"\"{ScriptPath}\" 5 default";
                Plugin.Log?.LogInfo("[CaptchaManager] Dùng bản PyCaptcha mã nguồn mở (python).");
            }
            
            _pythonProcess.StartInfo.WorkingDirectory = WorkingDirectory;
            
            // RedirectStandardInput = true để truyền lệnh "scan" qua IPC
            _pythonProcess.StartInfo.UseShellExecute = false;
            _pythonProcess.StartInfo.RedirectStandardInput = true;
            _pythonProcess.StartInfo.RedirectStandardOutput = true;
            _pythonProcess.StartInfo.RedirectStandardError = true;
            _pythonProcess.StartInfo.CreateNoWindow = true;
            _pythonProcess.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            _pythonProcess.StartInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
            _pythonProcess.StartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            _pythonProcess.OutputDataReceived += (_, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Plugin.Log?.LogInfo($"[PyCaptcha] {e.Data}");
                    if (e.Data.Contains("Found ") && e.Data.Contains("targets. Clicking"))
                    {
                        IsSolving = true;
                        LastSolvingActivity = Time.realtimeSinceStartup;
                    }
                    else if (e.Data.Contains("Clicked "))
                    {
                        IsSolving = false;
                        LastSolvingActivity = Time.realtimeSinceStartup;
                    }
                }
            };
            _pythonProcess.ErrorDataReceived += (_, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                    Plugin.Log?.LogError($"[PyCaptcha ERR] {e.Data}");
            };

            _pythonProcess.Start();
            _pythonProcess.BeginOutputReadLine();
            _pythonProcess.BeginErrorReadLine();

            _isRunning = true;
            Plugin.Log?.LogInfo("[CaptchaManager] Python bot started in IPC mode.");

            // Start monitoring immediately to match bypass tool behavior
            try
            {
                IntPtr hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                Plugin.Log?.LogInfo($"[CaptchaManager] Gửi lệnh start monitor tới Python Bot qua IPC... (HWND={hwnd})");
                _pythonProcess.StandardInput.WriteLine($"start {hwnd}");
                _pythonProcess.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[CaptchaManager] Lỗi gửi lệnh start IPC tới Python: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[CaptchaManager] Failed to start python bot: {ex.Message}");
        }
    }

    public static void TriggerScan()
    {
        if (!_isRunning || _pythonProcess == null || _pythonProcess.HasExited)
        {
            Plugin.Log?.LogWarning("[CaptchaManager] Python bot chưa chạy hoặc đã crash! Đang thử khởi động lại...");
            StartPythonBot();
        }

        if (_isRunning && _pythonProcess != null && !_pythonProcess.HasExited)
        {
            try
            {
                IntPtr hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                Plugin.Log?.LogInfo($"[CaptchaManager] Gửi lệnh start monitor tới Python Bot qua IPC... (HWND={hwnd})");
                _pythonProcess.StandardInput.WriteLine($"start {hwnd}");
                _pythonProcess.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[CaptchaManager] Lỗi gửi IPC tới Python: {ex.Message}");
            }
        }
    }

    public static void StopScan()
    {
        if (_isRunning && _pythonProcess != null && !_pythonProcess.HasExited)
        {
            try
            {
                _pythonProcess.StandardInput.WriteLine("stop");
                _pythonProcess.StandardInput.Flush();
                Plugin.Log?.LogInfo("[CaptchaManager] Gửi lệnh stop monitor tới Python Bot");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[CaptchaManager] Lỗi gửi IPC tới Python: {ex.Message}");
            }
        }
    }

    public static void StopPythonBot()
    {
        if (_pythonProcess != null && !_pythonProcess.HasExited)
        {
            try
            {
                _pythonProcess.Kill();
                _pythonProcess.Dispose();
                Plugin.Log?.LogInfo("[CaptchaManager] Python bot stopped.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[CaptchaManager] Failed to stop python bot: {ex.Message}");
            }
        }
        _pythonProcess = null;
        _isRunning = false;
    }
}
