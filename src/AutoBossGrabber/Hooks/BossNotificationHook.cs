using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BepInEx.Logging;

namespace AutoBossGrabber;

/// <summary>
/// Hook vào UI Text để detect boss notification NGAY KHI text được set.
/// Cách này nhanh hơn và chính xác hơn polling UI mỗi 1s.
/// </summary>
public static class BossNotificationHook
{
    private static ManualLogSource _log;
    private static float _lastDetectTime = -999f;
    private static readonly float DetectCooldown = 10f;

    private static readonly HashSet<string> _hookedTexts = new HashSet<string>();

    // Từ khóa hệ thống - phải có 1 trong này
    private static readonly string[] SystemKeywords =
    {
        "hệ thống", "he thong", "system"
    };

    // Từ khóa spawn - phải có 1 trong này
    private static readonly string[] SpawnKeywords =
    {
        "xuất hiện", "xuat hien", "appeared", "spawn"
    };

    // Keywords boss động - extract từ Config.BossNames
    private static List<string> _bossNameKeywords = new List<string>();

    // Từ khóa loại trừ - nếu có thì KHÔNG phải boss
    private static readonly string[] ExcludeKeywords =
    {
        "cellion", "junior", "mob", "quái", "quai"
    };

    public static event Action<string> OnBossNotificationDetected;

    public static void Initialize(ManualLogSource log, AutoBossConfig config)
    {
        _log = log;
        BossNotificationFilter.Initialize(log);

        // Extract keywords từ config.BossNames
        _bossNameKeywords.Clear();
        foreach (var bossName in config.BossNames)
        {
            // Tách từng từ trong boss name và lowercase
            var words = bossName.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (!_bossNameKeywords.Contains(word))
                {
                    _bossNameKeywords.Add(word);
                }
            }
        }

        _log?.LogInfo($"[BossNotificationHook] Extracted {_bossNameKeywords.Count} boss keywords: {string.Join(", ", _bossNameKeywords)}");
    }

    /// <summary>
    /// Polling nhẹ: chỉ check các Text component ĐANG ACTIVE và mới được enable gần đây.
    /// Gọi mỗi frame từ AutoBoss.Update().
    /// </summary>
    public static void CheckActiveTexts()
    {
        if (Time.time - _lastDetectTime < DetectCooldown)
            return;

        try
        {
            // Check UI.Text
            var uiTexts = UnityEngine.Object.FindObjectsOfType<Text>();
            foreach (var text in uiTexts)
            {
                if (text == null || !text.gameObject.activeInHierarchy)
                    continue;

                // Filter by hierarchy before keyword check
                if (!BossNotificationFilter.IsValidNotificationSource(text))
                    continue;

                CheckText(text.text, text.GetHashCode());
            }

            // Check TMPro
            var tmpTexts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
            foreach (var text in tmpTexts)
            {
                if (text == null || !text.gameObject.activeInHierarchy)
                    continue;

                // Filter by hierarchy before keyword check
                if (!BossNotificationFilter.IsValidNotificationSource(text))
                    continue;

                CheckText(text.text, text.GetHashCode());
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[BossNotificationHook] CheckActiveTexts error: {ex.Message}");
        }
    }

    private static void CheckText(string text, int hashCode)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Đã check text này rồi
        string key = $"{hashCode}:{text}";
        if (_hookedTexts.Contains(key))
            return;

        _hookedTexts.Add(key);

        // Cleanup old entries (giữ tối đa 100 entries)
        if (_hookedTexts.Count > 100)
            _hookedTexts.Clear();

        // YÊU CẦU: Phải có CẢ 3 loại keyword: system + spawn + boss
        string lower = text.ToLowerInvariant();

        // LOẠI TRỪ: Nếu có từ khóa exclude thì bỏ qua ngay
        foreach (var exclude in ExcludeKeywords)
        {
            if (lower.Contains(exclude))
            {
                _log?.LogInfo($"[BossNotificationHook] SKIPPED (exclude '{exclude}'): '{text}'");
                return;
            }
        }

        // Bước 1: Phải có từ khóa HỆ THỐNG
        bool hasSystemKeyword = false;
        foreach (var keyword in SystemKeywords)
        {
            if (lower.Contains(keyword))
            {
                hasSystemKeyword = true;
                break;
            }
        }

        if (!hasSystemKeyword)
            return; // Không phải thông báo hệ thống

        // DEBUG: Log mọi system message để tracking
        _log?.LogInfo($"[BossNotificationHook] System message: '{text}'");

        // Bước 2: Phải có từ khóa SPAWN
        bool hasSpawnKeyword = false;
        foreach (var keyword in SpawnKeywords)
        {
            if (lower.Contains(keyword))
            {
                hasSpawnKeyword = true;
                break;
            }
        }

        if (!hasSpawnKeyword)
        {
            _log?.LogInfo($"[BossNotificationHook] -> Missing SPAWN keyword, skipped");
            return; // Không phải thông báo xuất hiện
        }

        // Bước 3: Phải có từ khóa BOSS
        bool hasBossKeyword = false;
        string matchedKeyword = "";
        foreach (var keyword in _bossNameKeywords)
        {
            if (lower.Contains(keyword))
            {
                hasBossKeyword = true;
                matchedKeyword = keyword;
                break;
            }
        }

        if (hasBossKeyword)
        {
            _log?.LogWarning($"[BossNotificationHook] *** DETECTED *** (matched '{matchedKeyword}'): '{text}'");
            _lastDetectTime = Time.time;
            OnBossNotificationDetected?.Invoke(text);
        }
        else
        {
            _log?.LogInfo($"[BossNotificationHook] -> Missing BOSS keyword, skipped");
        }
    }

    public static bool IsOnCooldown()
    {
        return Time.time - _lastDetectTime < DetectCooldown;
    }
}
