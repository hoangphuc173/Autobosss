using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Threading;

namespace AutoBossLauncher
{
    public partial class Form1 : Form
    {
        private string accountsFilePath;
        private string gameExePath;
        private AccountsFile currentAccounts;
        private CancellationTokenSource pipeServerCts;

        public Form1()
        {
            InitializeComponent();
            FindPaths();
            LoadAccounts();
            StartPipeServer();
        }

        private void FindPaths()
        {
            // Default assumes Launcher is placed next to "Vũ Trụ Đại Chiến.exe"
            // For dev, it might be in source\AutoBossLauncher\bin\Debug\net6.0-windows\
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string devPath = Path.Combine(baseDir, "..", "..", "..", "..", "..");
            
            string exe1 = Path.GetFullPath(Path.Combine(baseDir, "Vũ Trụ Đại Chiến.exe"));
            string exe2 = Path.GetFullPath(Path.Combine(devPath, "Vũ Trụ Đại Chiến.exe"));

            if (File.Exists(exe1))
            {
                gameExePath = exe1;
                accountsFilePath = Path.GetFullPath(Path.Combine(baseDir, "BepInEx", "plugins", "accounts.json"));
            }
            else if (File.Exists(exe2))
            {
                gameExePath = exe2;
                accountsFilePath = Path.GetFullPath(Path.Combine(devPath, "BepInEx", "plugins", "accounts.json"));
            }
            else
            {
                gameExePath = "";
                accountsFilePath = "accounts.json"; // fallback
            }
        }

        private void LoadAccounts()
        {
            if (File.Exists(accountsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(accountsFilePath);
                    var options = new JsonSerializerOptions 
                    { 
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    };
                    currentAccounts = JsonSerializer.Deserialize<AccountsFile>(json, options);
                    
                    if (currentAccounts?.Accounts != null)
                    {
                        dgvAccounts.AutoGenerateColumns = false;
                        dgvAccounts.DataSource = new BindingSource(new System.ComponentModel.BindingList<AccountData>(currentAccounts.Accounts), null);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi tải accounts.json: " + ex.Message);
                }
            }
            else
            {
                currentAccounts = new AccountsFile();
                MessageBox.Show("Không tìm thấy file accounts.json tại: " + accountsFilePath);
            }
        }

        private void SaveAccounts()
        {
            if (currentAccounts != null)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(currentAccounts, options);
                    File.WriteAllText(accountsFilePath, json);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi lưu accounts.json: " + ex.Message);
                }
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            SaveAccounts();
            AppendLog("Đã lưu tài khoản.");
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath))
            {
                MessageBox.Show("Không tìm thấy game Vũ Trụ Đại Chiến.exe");
                return;
            }

            // Optional: get selected index and update currentAccountIndex
            if (dgvAccounts.CurrentRow != null)
            {
                currentAccounts.CurrentAccountIndex = dgvAccounts.CurrentRow.Index;
            }
            SaveAccounts();

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(gameExePath);
                startInfo.WorkingDirectory = Path.GetDirectoryName(gameExePath);
                Process.Start(startInfo);
                AppendLog("Đang khởi chạy game...");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khởi chạy game: " + ex.Message);
            }
        }

        public void AppendLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(AppendLog), message);
                return;
            }

            if (txtLog.TextLength > 100000)
            {
                txtLog.Clear();
            }

            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            txtLog.ScrollToCaret();
        }

        private void StartPipeServer()
        {
            pipeServerCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!pipeServerCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        using (var pipeServer = new NamedPipeServerStream("AutoBossLauncherPipe", PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous))
                        {
                            await pipeServer.WaitForConnectionAsync(pipeServerCts.Token);
                            
                            using (var reader = new StreamReader(pipeServer))
                            {
                                while (!reader.EndOfStream && !pipeServerCts.Token.IsCancellationRequested)
                                {
                                    string line = await reader.ReadLineAsync();
                                    if (!string.IsNullOrEmpty(line))
                                    {
                                        AppendLog(line);
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Exiting
                    }
                    catch (Exception)
                    {
                        await Task.Delay(1000); // Retry delay
                    }
                }
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            pipeServerCts?.Cancel();
            base.OnFormClosing(e);
        }
    }
}
