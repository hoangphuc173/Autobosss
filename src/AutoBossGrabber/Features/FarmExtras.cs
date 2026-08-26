using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AutoBossGrabber;

/// <summary>
/// Bo sung farm loop (task 13 cua spec):
/// - Tu dong bam popup nhan thuong (13.4)
/// - Tu dong dung item satellite tang exp (13.5)
/// - Dem zones cleared / rewards / satellites va gui analytics len Manager (13.9)
///
/// Tat ca deu co cooldown rieng de khong spam UI game. Counters reset moi phien.
/// </summary>
public class FarmExtras
{
    // === Counters (analytics) ===
    public int ZonesCleared { get; private set; }
    public int RewardsClaimed { get; private set; }
    public int SatellitesUsed { get; private set; }

    private float _rewardClickCooldownUntil;
    private float _satelliteCooldownUntil;
    private float _nextAnalyticsAt;

    private static readonly string[] RewardTextKeywords =
    {
        "nhan thuong", "thuong", "reward", "claim", "xac nhan", "confirm",
        "tiep tuc", "ok", "nhan", "mo qua", "qua tang",
    };
    private static readonly string[] RewardPathKeywords =
        { "reward", "prize", "gift", "quest", "daily", "bonus", "popup" };

    /// <summary>Goi khi xac nhan da chuyen khu thanh cong.</summary>
    public void OnZoneCleared()
    {
        ZonesCleared++;
    }

    /// <summary>Diem so 1 button co kha nang la nut nhan thuong. Pure function - test duoc.</summary>
    public static int ScoreRewardCandidate(string text, string name, string path)
    {
        var t = (text ?? "").Trim().ToLowerInvariant();
        var n = (name ?? "").ToLowerInvariant();
        var p = (path ?? "").ToLowerInvariant();
        if (t.Length == 0 && n.Length == 0 && p.Length == 0) return int.MinValue / 2;

        int score = 0;

        foreach (var kw in RewardTextKeywords)
            if (t.Contains(kw)) { score += 600; break; }

        foreach (var kw in RewardPathKeywords)
            if (p.Contains(kw)) { score += 250; }

        if (n.Contains("reward") || n.Contains("claim")) score += 300;

        // Nut qua ngan qua chung chung ("OK" don le van duoc phep nhung diem thap)
        if (t == "ok" || t == "xac nhan") score += 150;

        return score;
    }

    /// <summary>
    /// Quet cac Button dang hien, neu thay nut nhan thuong thi click.
    /// Goi dinh ky tu Update() cua runner khi dang farm.
    /// </summary>
    public bool TryClaimRewardPopup()
    {
        if (Time.time < _rewardClickCooldownUntil) return false;

        try
        {
            var buttons = Il2CppAPI.FindObjectsOfType(typeof(Button));
            if (buttons == null || buttons.Length == 0)
                buttons = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
            if (buttons == null || buttons.Length == 0) return false;

            Button best = null;
            int bestScore = 400; // nguong toi thieu - tranh click lung tung
            foreach (var obj in buttons)
            {
                var btn = obj as Button;
                if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) continue;

                string text = UIHelper.GetButtonText(btn);
                string name = btn.gameObject?.name ?? "";
                string path = UIHelper.GetTransformPath(btn.transform);

                int score = ScoreRewardCandidate(text, name, path);
                if (score > bestScore)
                {
                    best = btn;
                    bestScore = score;
                }
            }

            if (best == null) return false;

            best.onClick.Invoke();
            RewardsClaimed++;
            _rewardClickCooldownUntil = Time.time + 2.5f;
            Plugin.Log.LogInfo($"[FarmExtras] Claimed reward popup (#{RewardsClaimed})");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[FarmExtras] TryClaimRewardPopup fail: {ex.Message}");
            _rewardClickCooldownUntil = Time.time + 2.5f;
            return false;
        }
    }

    /// <summary>
    /// Dung item satellite trong danh sach id cau hinh (moi id mot lan moi chu ky).
    /// </summary>
    public bool TryUseSatellite(AutoBossConfig config)
    {
        if (config?.SatelliteItemIds == null || config.SatelliteItemIds.Count == 0) return false;
        if (Time.time < _satelliteCooldownUntil) return false;

        try
        {
            foreach (var id in config.SatelliteItemIds)
            {
                GameAPI.TryUseItem(id);
                SatellitesUsed++;
                _satelliteCooldownUntil = Time.time + 60f;
                Plugin.Log.LogInfo($"[FarmExtras] Used satellite item id={id} (#{SatellitesUsed})");
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[FarmExtras] TryUseSatellite fail: {ex.Message}");
            _satelliteCooldownUntil = Time.time + 60f;
        }
        return false;
    }

    /// <summary>
    /// Gui analytics dinh ky len Manager qua LOG_EVENT (task 13.9).
    /// Tra ve chuoi stats hoac null neu chua den luot gui.
    /// </summary>
    public string SendAnalyticsIfDue(bool farmingActive)
    {
        if (!farmingActive || Time.time < _nextAnalyticsAt) return null;
        _nextAnalyticsAt = Time.time + 60f;

        var msg = $"FARM_STATS zones={ZonesCleared} rewards={RewardsClaimed} satellites={SatellitesUsed}";
        try
        {
            Plugin.Instance.SocketClient?.SendLogEvent("Info", msg);
        }
        catch { }
        return msg;
    }

    public void ResetSession()
    {
        ZonesCleared = 0;
        RewardsClaimed = 0;
        SatellitesUsed = 0;
    }

    public string GetStatsString() =>
        $"zones={ZonesCleared} rewards={RewardsClaimed} satellites={SatellitesUsed}";
}
