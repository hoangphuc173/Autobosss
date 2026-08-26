using Xunit;
using AutoBossGrabber;
using AutoBossShared;

namespace AutoBoss.Tests;

/// <summary>
/// Property tests cho ItemFilterManager.Decide - theo spec task 14.7:
/// "Whitelist mode never picks items not in list", special rules override, rarity filter.
/// Luu y: Decide nhan ten DA chuan hoa (giong luong that trong ShouldPickup).
/// </summary>
[Collection("PluginEnv")]
public class ItemFilterTests
{
    private static ItemFilterManager CreateConfigured(
        ItemFilterMode mode, string[] list,
        bool gems = true, bool quest = true, int minRarity = 0)
    {
        var f = new ItemFilterManager();
        f.Configure(mode, list, gems, quest, minRarity);
        return f;
    }

    private static string Norm(string raw) => ItemFilterManager.NormalizeName(raw);

    // === Disabled mode ===

    [Fact]
    public void Disabled_PicksEverything()
    {
        var f = CreateConfigured(ItemFilterMode.Disabled, new[] { "vang" });

        Assert.True(f.Decide(Norm("vang"), null, false, false));
        Assert.True(f.Decide(Norm("thiet giap"), 0, false, false));
        Assert.True(f.Decide("", null, false, false));
    }

    // === Whitelist mode (Property 4 cua spec: khong bao gio nhặt item ngoai list) ===

    [Fact]
    public void Whitelist_NeverPicksItemsNotInList()
    {
        var f = CreateConfigured(ItemFilterMode.Whitelist, new[] { "vang", "bac" });

        Assert.True(f.Decide(Norm("vang"), null, false, false));
        Assert.True(f.Decide(Norm("BAC"), null, false, false));   // khong phan biet hoa/thuong
        Assert.False(f.Decide(Norm("thit ga"), null, false, false));
        Assert.False(f.Decide("", null, false, false));           // ten khong doc duoc -> khong nhặt
    }

    // === Blacklist mode ===

    [Fact]
    public void Blacklist_PicksAllExceptListed()
    {
        var f = CreateConfigured(ItemFilterMode.Blacklist, new[] { "rac cu" });

        Assert.False(f.Decide(Norm("rac cu"), null, false, false));
        Assert.True(f.Decide(Norm("vang"), null, false, false));
        Assert.True(f.Decide("", null, false, false));            // ten khong doc duoc -> cho phep
    }

    // === Special rules vuot qua bo loc (REQ 12.4) ===

    [Fact]
    public void Gems_OverrideWhitelist_WhenEnabled()
    {
        var f = CreateConfigured(ItemFilterMode.Whitelist, new[] { "vang" }, gems: true);

        // "Ngọc Kín" khong co trong whitelist nhung la gem -> van pick
        Assert.True(f.Decide(Norm("Ngọc Kín"), null, true, false));
    }

    [Fact]
    public void Gems_RespectDisabledFlag()
    {
        var f = CreateConfigured(ItemFilterMode.Whitelist, new[] { "vang" }, gems: false);

        Assert.False(f.Decide(Norm("Ngọc Kín"), null, true, false));
    }

    [Fact]
    public void QuestItems_OverrideBlacklist()
    {
        var f = CreateConfigured(ItemFilterMode.Blacklist, new[] { "thu phuc" }, quest: true);

        // "Thư Phục" trung blacklist NHUNG la quest item -> van pick
        Assert.True(f.Decide(Norm("Thư Phục"), null, false, true));
    }

    // === Rarity filter ===

    [Fact]
    public void RarityFilter_SkipsBelowMinimum()
    {
        var f = CreateConfigured(ItemFilterMode.Disabled, Array.Empty<string>(), minRarity: 2);

        Assert.False(f.Decide("ao gio", 1, false, false));   // rarity 1 < 2
        Assert.True(f.Decide("ao gio", 2, false, false));
        Assert.True(f.Decide("ao gio", 3, false, false));
    }

    [Fact]
    public void RarityFilter_UnknownRarity_Passes()
    {
        var f = CreateConfigured(ItemFilterMode.Disabled, Array.Empty<string>(), minRarity: 3);

        // Khong biet rarity -> khong loai (an toan)
        Assert.True(f.Decide("vat pham bi an", null, false, false));
    }

    [Fact]
    public void MinRarity_Zero_MeansPickAll()
    {
        var f = CreateConfigured(ItemFilterMode.Disabled, Array.Empty<string>(), minRarity: 0);

        Assert.True(f.Decide("rac", 0, false, false));
    }

    // === NormalizeName ===

    [Theory]
    [InlineData("Ngọc Kín", "ngoc kin")]
    [InlineData("LÀNG KAKAROT", "lang kakarot")]
    [InlineData("  Vàng   Thôi  ", "vang thoi")]
    [InlineData("Đá quý", "da quy")]
    public void NormalizeName_StripsDiacriticsAndCase(string raw, string expected)
    {
        Assert.Equal(expected, ItemFilterManager.NormalizeName(raw));
    }

    [Fact]
    public void IsGemAndQuest_DetectVietnameseKeywords()
    {
        Assert.True(ItemFilterManager.IsGemName("ngoc kin"));
        Assert.True(ItemFilterManager.IsGemName("kim cuong do"));
        Assert.False(ItemFilterManager.IsGemName("vang thoi"));

        Assert.True(ItemFilterManager.IsQuestName("su mang nhiem vu"));
        Assert.True(ItemFilterManager.IsQuestName("thu phuc"));
        Assert.False(ItemFilterManager.IsQuestName("ao chien than"));
    }
}
