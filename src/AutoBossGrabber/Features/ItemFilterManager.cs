using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AutoBossShared;

namespace AutoBossGrabber;

/// <summary>
/// Bo loc vat pham khi tu dong nhặt (task 14 cua spec autoboss-manager-integration).
/// - Mode Disabled / Whitelist / Blacklist theo ten vat pham.
/// - Special rules: ngoc + do nhiem vu luon nhặt khi bat co (vuot qua bo loc).
/// - Rarity: nhung item co rarity < MinRarityToPickup bi bo qua (0 = lay tat ca).
/// -
/// Loi quyet dinh duoc tach sang ham thuan <see cref="Decide"/> de unit test
/// khong can game runtime. ShouldPickup(object) chi la wrapper doc du lieu game.
/// </summary>
public class ItemFilterManager
{
    private static ItemFilterManager _instance;
    public static ItemFilterManager Instance => _instance ??= new ItemFilterManager();

    private ItemFilterMode _mode = ItemFilterMode.Disabled;
    private readonly HashSet<string> _filterList = new(StringComparer.Ordinal);
    private bool _alwaysPickGems = true;
    private bool _alwaysPickQuestItems = true;
    private int _minRarityToPickup = 0;

    // === Thong ke ===
    public int ItemsPickedUp { get; private set; }
    public int ItemsFiltered { get; private set; }
    private float _lastStatLogAt = -999f;

    /// <summary>
    /// Cau hinh bo loc (duoc goi tu SocketClient khi nhan CONFIG_UPDATE tu Manager).
    /// </summary>
    public void Configure(ItemFilterMode mode, IEnumerable<string> itemList,
        bool alwaysPickGems, bool alwaysPickQuestItems, int minRarityToPickup)
    {
        _mode = mode;
        _alwaysPickGems = alwaysPickGems;
        _alwaysPickQuestItems = alwaysPickQuestItems;
        _minRarityToPickup = Math.Max(0, minRarityToPickup);

        _filterList.Clear();
        if (itemList != null)
        {
            foreach (var raw in itemList)
            {
                var normalized = NormalizeName(raw);
                if (!string.IsNullOrEmpty(normalized))
                {
                    _filterList.Add(normalized);
                }
            }
        }

        Plugin.Log.LogInfo(
            $"[ItemFilter] Configured: mode={_mode}, list={_filterList.Count} items, " +
            $"gems={_alwaysPickGems}, quest={_alwaysPickQuestItems}, minRarity={_minRarityToPickup}");
    }

    /// <summary>
    /// Chuan hoa ten vat pham de so khop: bo khoang trang du, lower-case,
    /// phan rã Unicode FormD roi bo dau thanh (non-spacing mark), d -> d.
    /// VD: "Ngọc Kín" / "NGỌC KÍN" -> "ngoc kin".
    /// </summary>
    public static string NormalizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // Bat buoc decompose (FormD): 'à' precomposed -> 'a' + combining mark
        var decomposed = raw.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);

        var sb = new System.Text.StringBuilder(decomposed.Length);
        bool lastWasSpace = false;
        foreach (var c in decomposed)
        {
            if (char.IsWhiteSpace(c))
            {
                // Trim dau + collapse nhieu khoang trang thanh dung 1 khoang
                if (sb.Length == 0 || lastWasSpace) continue;
                sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            lastWasSpace = false;
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c);
        }
        return sb.ToString().Replace('đ', 'd');
    }

    // === Heuristic nhan dang loai dac biet tu ten (normalized) ===
    private static readonly string[] GemKeywords = { "ngoc", "gem", "da quy", "kim cuong" };
    private static readonly string[] QuestKeywords = { "nhiem vu", "quest", "su mang", "thu phuc" };

    public static bool IsGemName(string normalizedName)
    {
        foreach (var k in GemKeywords)
            if (normalizedName.Contains(k)) return true;
        return false;
    }

    public static bool IsQuestName(string normalizedName)
    {
        foreach (var k in QuestKeywords)
            if (normalizedName.Contains(k)) return true;
        return false;
    }

    /// <summary>
    /// LOI QUYET DINH THUAN (khong cham vao game/state) - don vi kiem thu chinh.
    /// Thu tu uu tien theo spec 12.x: special rules -> rarity filter -> mode.
    /// Tra ve TRUE khi khong chac chan (safe default, REQ 12.6).
    /// </summary>
    /// <param name="normalizedName">Ten da chuan hoa (co the rong neu khong doc duoc).</param>
    /// <param name="rarity">Rarity neu doc duoc, null = khong biet.</param>
    /// <param name="isGem">Ten co trung tu khoa ngoc khong.</param>
    /// <param name="isQuest">Ten co trung tu khoa nhiem vu khong.</param>
    public bool Decide(string normalizedName, int? rarity, bool isGem, bool isQuest)
    {
        // 1) Special rules uu tien cao nhat (REQ 12.4)
        if (isGem && _alwaysPickGems) return true;
        if (isQuest && _alwaysPickQuestItems) return true;

        // 2) Rarity filter (REQ 12.x): 0 = tat ca; chi ap dung khi Biet rarity.
        if (_minRarityToPickup > 0 && rarity.HasValue && rarity.Value < _minRarityToPickup)
        {
            return false;
        }

        // 3) Theo mode
        switch (_mode)
        {
            case ItemFilterMode.Whitelist:
                // Chi nhặt khi ten nam trong danh sach; ten rong = khong xac dinh = bo qua (an toan voi whitelist).
                return !string.IsNullOrEmpty(normalizedName) && _filterList.Contains(normalizedName);

            case ItemFilterMode.Blacklist:
                // Nhạt tat ca tru cac ten trong danh sach; ten rong = khong xac dinh = cho phep.
                return string.IsNullOrEmpty(normalizedName) || !_filterList.Contains(normalizedName);

            case ItemFilterMode.Disabled:
            default:
                return true;
        }
    }

    /// <summary>
    /// Wrapper goi tu pickup loop: doc ten/rarity tu object item cua game roi Decide.
    /// Moi loi doc du lieu deu tra ve TRUE (nhặt) theo safe default.
    /// </summary>
    public bool ShouldPickup(object mapItem)
    {
        string displayName = null;
        int? rarity = null;
        try
        {
            displayName = GameAPI.GetItemDisplayName(mapItem);
            rarity = GameAPI.TryReadItemRarity(mapItem);
        }
        catch
        {
            // Khong doc duoc -> xu ly nhu ten rong (Decide tu quyet dinh an toan).
        }

        var normalized = NormalizeName(displayName);
        bool pass = Decide(normalized, rarity, IsGemName(normalized), IsQuestName(normalized));

        if (pass) ItemsPickedUp++;
        else ItemsFiltered++;

        LogStatsThrottled();
        return pass;
    }

    public string GetStatsString() =>
        $"[ItemFilter] mode={_mode} picked={ItemsPickedUp} filtered={ItemsFiltered}";

    public void ResetStats()
    {
        ItemsPickedUp = 0;
        ItemsFiltered = 0;
    }

    private void LogStatsThrottled()
    {
        if (Time.time - _lastStatLogAt < 15f || (ItemsPickedUp + ItemsFiltered == 0)) return;
        _lastStatLogAt = Time.time;
        Plugin.Log.LogInfo(GetStatsString());
    }
}
