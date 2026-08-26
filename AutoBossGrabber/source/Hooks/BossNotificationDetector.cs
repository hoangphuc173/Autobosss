using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AutoBossGrabber;

/// <summary>
/// Phát hiện thông báo boss xuất hiện trên UI.
/// Quét các Text/TextMeshPro component tìm keyword:
///   - "Boss xuất hiện", "Boss xuat hien"
///   - "Vua", "Vừa", "vua", "vừa"
///   - Boss name (Cooler, Frieza, Cell, ...)
/// </summary>
public static class BossNotificationDetector
{
    private static readonly string[] BossKeywords =
    {
        "boss xuất hiện", "boss xuat hien",
        "vua xuat hien", "vừa xuất hiện",
        "boss spawn", "boss appeared",
        "cooler", "frieza", "cell", "buu", "broly"
    };

    private static float _lastScanTime = -999f;
    private static float _lastDetectionTime = -999f;
    private const float ScanInterval = 1f; // scan mỗi 1s
    private const float DetectionCooldown = 10f; // cooldown 10s sau mỗi lần detect

    /// <summary>
    /// Quét UI tìm thông báo boss. Throttle 1s/lần.
    /// </summary>
    public static bool DetectBossNotification()
    {
        // Throttle scan
        if (Time.time - _lastScanTime < ScanInterval)
            return false;

        _lastScanTime = Time.time;

        // Cooldown sau mỗi lần detect (tránh trigger lặp lại)
        if (Time.time - _lastDetectionTime < DetectionCooldown)
            return false;

        try
        {
            // Quét tất cả Text component
            var texts = ScanAllTextComponents();
            foreach (var txt in texts)
            {
                if (ContainsBossKeyword(txt))
                {
                    _lastDetectionTime = Time.time;
                    Plugin.Log.LogInfo($"[BossNotification] DETECTED: '{txt}'");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[BossNotification] Scan fail: {ex.Message}");
        }

        return false;
    }

    private static List<string> ScanAllTextComponents()
    {
        var result = new List<string>();

        // Scan Unity UI Text
        try
        {
            var textObjs = Il2CppAPI.FindObjectsOfTypeAll(typeof(Text));
            if (textObjs != null)
            {
                foreach (var obj in textObjs)
                {
                    var text = obj as Text;
                    if (text == null) continue;
                    if (!text.gameObject.activeInHierarchy) continue;
                    if (string.IsNullOrWhiteSpace(text.text)) continue;
                    result.Add(text.text.Trim());
                }
            }
        }
        catch { }

        // Scan TextMeshPro
        try
        {
            var tmpObjs = Il2CppAPI.FindObjectsOfTypeAll(typeof(TMPro.TextMeshProUGUI));
            if (tmpObjs != null)
            {
                foreach (var obj in tmpObjs)
                {
                    var tmp = obj as TMPro.TextMeshProUGUI;
                    if (tmp == null) continue;
                    if (!tmp.gameObject.activeInHierarchy) continue;
                    if (string.IsNullOrWhiteSpace(tmp.text)) continue;
                    result.Add(tmp.text.Trim());
                }
            }
        }
        catch { }

        return result;
    }

    private static bool ContainsBossKeyword(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string lower = text.ToLowerInvariant();

        foreach (var keyword in BossKeywords)
        {
            if (lower.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reset cooldown - gọi khi user bật tool thủ công (F1)
    /// </summary>
    public static void ResetCooldown()
    {
        _lastDetectionTime = -999f;
    }
}
