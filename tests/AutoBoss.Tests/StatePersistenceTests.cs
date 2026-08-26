using Xunit;
using AutoBossGrabber;

namespace AutoBoss.Tests;

[Collection("PluginEnv")]
public class StatePersistenceTests : IDisposable
{
    private readonly string _tmpDir;

    public StatePersistenceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "autoboss-state-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true);
    }

    [Fact]
    public void Save_TryLoad_RoundTrip_PreservesFields()
    {
        var state = new PersistedBotState
        {
            State = "ZoneScanLoop",
            Map = "Cung",
            Zone = 7,
            ZoneAttempts = 12,
            WasFarmingActive = true,
            SavedAt = DateTime.Now,
        };

        StatePersistence.Save(state, _tmpDir);

        var loaded = StatePersistence.TryLoad(_tmpDir);
        Assert.NotNull(loaded);
        Assert.Equal("ZoneScanLoop", loaded.State);
        Assert.Equal("Cung", loaded.Map);
        Assert.Equal(7, loaded.Zone);
        Assert.Equal(12, loaded.ZoneAttempts);
        Assert.True(loaded.WasFarmingActive);
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsNull()
    {
        Assert.Null(StatePersistence.TryLoad(_tmpDir));
    }

    // === IsFresh (REQ 15.3-15.4) ===

    [Fact]
    public void IsFresh_ActiveAndUnder5Minutes_True()
    {
        var s = new PersistedBotState { WasFarmingActive = true, SavedAt = DateTime.Now.AddMinutes(-2) };
        Assert.True(s.IsFresh(DateTime.Now));
    }

    [Fact]
    public void IsFresh_Over5Minutes_False()
    {
        var s = new PersistedBotState { WasFarmingActive = true, SavedAt = DateTime.Now.AddMinutes(-6) };
        Assert.False(s.IsFresh(DateTime.Now));
    }

    [Fact]
    public void IsFresh_NotFarming_False_EvenIfFreshTimestamp()
    {
        var s = new PersistedBotState { WasFarmingActive = false, SavedAt = DateTime.Now };
        Assert.False(s.IsFresh(DateTime.Now));
    }

    // === CleanupOldFiles (task 22.4) ===

    [Fact]
    public void CleanupOldFiles_DeletesOnlyOlderThanMaxAge()
    {
        Directory.CreateDirectory(_tmpDir);
        var oldFile = Path.Combine(_tmpDir, "bot_state.json");
        var newFile = Path.Combine(_tmpDir, "other_state.json");
        File.WriteAllText(oldFile, "{}");
        File.WriteAllText(newFile, "{}");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddHours(-30));

        StatePersistence.CleanupOldFiles(TimeSpan.FromHours(24), _tmpDir);

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }
}
