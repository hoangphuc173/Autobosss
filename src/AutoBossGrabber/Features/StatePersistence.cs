using System;
using System.IO;
using Newtonsoft.Json;
using BepInEx;

namespace AutoBossGrabber;

/// <summary>
/// Snapshot trang thai bot de phuc hoi sau crash (task 22 cua spec).
/// Pure model - JSON round-trip test duoc ngoai game.
/// </summary>
public class PersistedBotState
{
    [JsonProperty("savedAt")] public DateTime SavedAt { get; set; } = DateTime.Now;
    [JsonProperty("state")] public string State { get; set; } = "Idle";
    [JsonProperty("map")] public string Map { get; set; } = "";
    [JsonProperty("zone")] public int Zone { get; set; }
    [JsonProperty("zoneAttempts")] public int ZoneAttempts { get; set; }
    [JsonProperty("wasFarmingActive")] public bool WasFarmingActive { get; set; }

    /// <summary>State con "tuoi" trong 5 phut (REQ 15.3-15.4).</summary>
    public bool IsFresh(DateTime now) =>
        WasFarmingActive && (now - SavedAt).TotalMinutes <= 5 &&
        (now - SavedAt).TotalMinutes >= -1; // chong dong ho lui
}

/// <summary>
/// Luu / doc / don dep trang thai bot (task 22.1-22.4).
/// File: {dir}/bot_state.json - mac dinh BepInEx/config/AutoBossGrabber/.
/// Thu muc inject duoc de unit test voi tmp dir (giong GraphCache).
/// </summary>
public static class StatePersistence
{
    private const string FileName = "bot_state.json";
    private const string DirOverrideEnvKey = "AUTOBOSS_STATE_DIR";

    public static string DefaultDir()
    {
        try { return Path.Combine(Paths.ConfigPath, "AutoBossGrabber"); }
        catch
        {
            // Chay ngoai game (unit test): dung thu muc tam theo env neu co
            var env = Environment.GetEnvironmentVariable(DirOverrideEnvKey);
            return env ?? ".";
        }
    }

    private static string FilePath(string dir) => Path.Combine(dir ?? DefaultDir(), FileName);

    /// <summary>Luu snapshot (ghi atomically qua file .tmp de tranh file hong khi crash giua chung).</summary>
    public static void Save(PersistedBotState state, string dir = null)
    {
        try
        {
            var d = dir ?? DefaultDir();
            Directory.CreateDirectory(d);
            var finalPath = FilePath(dir);
            var tmpPath = finalPath + ".tmp";
            File.WriteAllText(tmpPath, JsonConvert.SerializeObject(state, Formatting.Indented));
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmpPath, finalPath);
            Plugin.Log.LogInfo($"[StatePersist] Saved: state={state.State} map='{state.Map}' zone={state.Zone} farming={state.WasFarmingActive}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[StatePersist] Save fail (bo qua): {ex.Message}");
        }
    }

    /// <summary>Doc snapshot. Tra ve null khi khong ton tai / doc loi.</summary>
    public static PersistedBotState TryLoad(string dir = null)
    {
        try
        {
            var path = FilePath(dir);
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<PersistedBotState>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[StatePersist] Load fail: {ex.Message}");
            return null;
        }
    }

    /// <summary>Xoa file state (khi stale hoac nguoi dung tat bot sach se).</summary>
    public static void Delete(string dir = null)
    {
        try
        {
            var path = FilePath(dir);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    /// <summary>Xoa cac file state cu hon 24 gio trong thu muc (task 22.4).</summary>
    public static void CleanupOldFiles(TimeSpan maxAge, string dir = null)
    {
        try
        {
            var d = dir ?? DefaultDir();
            if (!Directory.Exists(d)) return;
            foreach (var f in Directory.GetFiles(d, "*state*.json"))
            {
                try
                {
                    if (DateTime.Now - File.GetLastWriteTime(f) > maxAge)
                    {
                        File.Delete(f);
                        Plugin.Log.LogInfo($"[StatePersist] Cleaned old state file: {Path.GetFileName(f)}");
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
