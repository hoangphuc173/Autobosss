using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using AutoBossShared;

namespace AutoBossManager.Views
{
    /// <summary>
    /// Dialog chinh sua cau hinh runtime cua mot bot roi push xuong qua CONFIG_UPDATE.
    /// Ket qua: Result (BotProfile da cap nhat) khi DialogResult = true.
    /// </summary>
    public partial class BotConfigDialog : Window
    {
        private readonly BotProfile _profile;

        public BotProfile? Result { get; private set; }

        public BotConfigDialog(BotProfile profile, string botAccount)
        {
            InitializeComponent();
            _profile = profile;
            TxtHeader.Text = $"⚙ Cấu hình runtime — {botAccount}";
            LoadFromProfile(profile);
        }

        private void LoadFromProfile(BotProfile p)
        {
            TxtMaxZoneAttempts.Text = p.MaxZoneAttempts.ToString();
            TxtAttackRange.Text = p.AttackRange.ToString("0.##");
            TxtCombatTimeout.Text = p.CombatTimeoutSec.ToString("0.#");
            TxtRetreatHp.Text = p.RetreatHpPct.ToString("0.#");
            TxtLootRadius.Text = p.LootRadius.ToString("0.#");
            TxtBossNames.Text = p.TargetBossNames != null && p.TargetBossNames.Count > 0
                ? string.Join(Environment.NewLine, p.TargetBossNames)
                : "";
            ChkAutoZone.IsChecked = p.EnableAutoZoneSwitch;

            TxtSkillTriggers.Text = p.BossSkillTriggers != null
                ? string.Join(Environment.NewLine,
                    p.BossSkillTriggers.Select(t => $"{t.HpThreshold:0},{t.SkillKey},{t.SpamCount}"))
                : "";
        }

        /// <summary>Ap preset vao cac field tren UI ngay lap tuc (task 19.2/25.3).</summary>
        private void CmbPreset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbPreset.SelectedItem is not System.Windows.Controls.ComboBoxItem { Content: string name })
                return;
            var preset = Services.StrategyPresetManager.Find(name);
            if (preset == null) return; // "(giu nguyen)"

            TxtMaxZoneAttempts.Text = preset.MaxZoneAttempts.ToString();
            TxtAttackRange.Text = preset.AttackRange.ToString("0.##");
            TxtCombatTimeout.Text = preset.CombatTimeoutSec.ToString("0.#");
            TxtRetreatHp.Text = preset.RetreatHpPct.ToString("0.#");
            TxtLootRadius.Text = preset.LootRadius.ToString("0.#");
            ChkAutoZone.IsChecked = true;
        }

        private void BtnPush_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _profile.MaxZoneAttempts = ParseInt(TxtMaxZoneAttempts.Text, _profile.MaxZoneAttempts);
                _profile.AttackRange = ParseFloat(TxtAttackRange.Text, _profile.AttackRange);
                _profile.CombatTimeoutSec = ParseFloat(TxtCombatTimeout.Text, _profile.CombatTimeoutSec);
                _profile.RetreatHpPct = ParseFloat(TxtRetreatHp.Text, _profile.RetreatHpPct);
                _profile.LootRadius = ParseFloat(TxtLootRadius.Text, _profile.LootRadius);
                _profile.EnableAutoZoneSwitch = ChkAutoZone.IsChecked == true;

                _profile.TargetBossNames = TxtBossNames.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct()
                    .ToList();

                _profile.BossSkillTriggers.Clear();
                foreach (var line in TxtSkillTriggers.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(',');
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    var trigger = new SkillTrigger
                    {
                        HpThreshold = ParseFloat(parts[0], 0f),
                        SkillKey = Math.Clamp(ParseInt(parts[1], 1), 1, 4),
                        SpamCount = parts.Length > 2 ? Math.Clamp(ParseInt(parts[2], 1), 1, 10) : 1,
                    };
                    _profile.BossSkillTriggers.Add(trigger);
                }

                Result = _profile;
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
            float.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
