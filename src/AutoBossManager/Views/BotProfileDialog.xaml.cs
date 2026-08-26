using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using AutoBossManager.Services;
using AutoBossShared;

namespace AutoBossManager.Views
{
    /// <summary>
    /// Dialog them moi / chinh sua bot profile.
    /// Ket qua: Profile (khong null khi DialogResult = true) + co LaunchGame hay khong.
    /// </summary>
    public partial class BotProfileDialog : Window
    {
        private readonly ProfileManager _profileManager;
        private BotProfile? _original;

        public BotProfile? Profile { get; private set; }
        public bool LaunchGame { get; private set; }

        public BotProfileDialog(ProfileManager profileManager, string? existingAccount = null)
        {
            InitializeComponent();
            _profileManager = profileManager;

            // Neu account da ton tai -> load de edit (password duoc decrypt boi ProfileManager)
            if (!string.IsNullOrEmpty(existingAccount) && _profileManager.ProfileExists(existingAccount))
            {
                _original = _profileManager.LoadProfile(existingAccount);
                Title = $"Bot Profile - {existingAccount} (edit)";
            }

            LoadFromProfile(_original ?? new BotProfile());
        }

        private void LoadFromProfile(BotProfile p)
        {
            TxtAccountName.Text = p.AccountName ?? "";
            TxtUsername.Text = p.Username ?? "";
            PwbPassword.Password = p.Password ?? "";
            TxtGameExe.Text = p.GameExecutablePath ?? "";
            TxtHomeMap.Text = string.IsNullOrEmpty(p.HomeMapName) ? "Quay" : p.HomeMapName;
            TxtBossNames.Text = p.TargetBossNames != null && p.TargetBossNames.Count > 0
                ? string.Join(Environment.NewLine, p.TargetBossNames)
                : "Vua Vegita" + Environment.NewLine + "Cooler";
            TxtMaxZoneAttempts.Text = p.MaxZoneAttempts.ToString();
            TxtAttackRange.Text = p.AttackRange.ToString("0.##");
            TxtCombatTimeout.Text = p.CombatTimeoutSec.ToString("0.#");
            TxtRetreatHp.Text = p.RetreatHpPct.ToString("0.#");
            TxtLootRadius.Text = p.LootRadius.ToString("0.#");
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable|*.exe|All files|*.*",
                Title = "Chọn file game exe"
            };
            if (dlg.ShowDialog(this) == true)
            {
                TxtGameExe.Text = dlg.FileName;
            }
        }

        /// <summary>Ap preset vao cac field tren UI (task 19.2).</summary>
        private void CmbPreset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbPreset.SelectedItem is not System.Windows.Controls.ComboBoxItem { Content: string name })
                return;
            var preset = Services.StrategyPresetManager.Find(name);
            if (preset == null) return;

            TxtMaxZoneAttempts.Text = preset.MaxZoneAttempts.ToString();
            TxtAttackRange.Text = preset.AttackRange.ToString("0.##");
            TxtCombatTimeout.Text = preset.CombatTimeoutSec.ToString("0.#");
            TxtRetreatHp.Text = preset.RetreatHpPct.ToString("0.#");
            TxtLootRadius.Text = preset.LootRadius.ToString("0.#");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var profile = _original ?? new BotProfile();

                profile.AccountName = TxtAccountName.Text.Trim();
                profile.Username = TxtUsername.Text.Trim();
                // Giu nguyen password da ma hoa neu user khong nhap lai.
                profile.Password = PwbPassword.Password.Length > 0
                    ? PwbPassword.Password
                    : profile.Password;
                profile.GameExecutablePath = TxtGameExe.Text.Trim();
                profile.HomeMapName = string.IsNullOrWhiteSpace(TxtHomeMap.Text) ? "Quay" : TxtHomeMap.Text.Trim();
                profile.TownMapName = "Ngoai";

                profile.TargetBossNames = TxtBossNames.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct()
                    .ToList();

                profile.MaxZoneAttempts = ParseInt(TxtMaxZoneAttempts.Text, 15);
                profile.AttackRange = ParseFloat(TxtAttackRange.Text, 2.5f);
                profile.CombatTimeoutSec = ParseFloat(TxtCombatTimeout.Text, 60f);
                profile.RetreatHpPct = ParseFloat(TxtRetreatHp.Text, 20f);
                profile.LootRadius = ParseFloat(TxtLootRadius.Text, 200f);

                // Randomization sync (task 19.3/20.3): theo preset dang chon
                var selectedPreset = CmbPreset.SelectedItem is System.Windows.Controls.ComboBoxItem { Content: string pn }
                    ? Services.StrategyPresetManager.Find(pn) : null;
                if (selectedPreset != null)
                {
                    profile.EnableRandomization = selectedPreset.EnableRandomization;
                    profile.RandomizationIntensity = selectedPreset.RandomizationIntensity;
                }

                // Validate truoc khi dong dialog - bao loi ngay tai cho.
                var validation = _profileManager.ValidateProfile(profile);
                if (!validation.IsValid)
                {
                    TxtError.Text = "⚠ " + string.Join("\n⚠ ", validation.Errors);
                    return;
                }

                _profileManager.SaveProfile(profile);

                Profile = profile;
                LaunchGame = ChkLaunchNow.IsChecked == true;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                TxtError.Text = "⚠ " + ex.Message;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private static int ParseInt(string s, int fallback) =>
            int.TryParse(s?.Trim(), out var v) ? v : fallback;

        private static float ParseFloat(string s, float fallback) =>
            float.TryParse(s?.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        /// <summary>Launch game process voi working dir = thu muc exe.</summary>
        public static void LaunchGameProcess(BotProfile profile)
        {
            if (string.IsNullOrEmpty(profile.GameExecutablePath) || !File.Exists(profile.GameExecutablePath))
            {
                MessageBox.Show($"Không tìm thấy game exe: {profile.GameExecutablePath}",
                    "Launch thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = profile.GameExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(profile.GameExecutablePath)!,
                UseShellExecute = true,
            });
        }
    }
}
