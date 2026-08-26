using Xunit;
using AutoBossManager.Services;

namespace AutoBoss.Tests;

[Collection("PluginEnv")]
public class LogAggregatorTests : IDisposable
{
    private readonly string _tmpDir;

    public LogAggregatorTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "autoboss-log-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true);
    }

    [Fact]
    public void Add_WritesJsonlFileForToday()
    {
        using var agg = new LogAggregator(_tmpDir);
        agg.Add(new AggregatedLogEntry { Level = "Info", Source = "abcd1234", Message = "hello world" });
        agg.Dispose();

        var files = Directory.GetFiles(_tmpDir, "autoboss_*.jsonl");
        Assert.NotEmpty(files);

        var content = File.ReadAllText(files[0]);
        Assert.Contains("hello world", content);
        Assert.Contains("\"lvl\":\"Info\"", content);
        Assert.Contains("abcd1234", content);
    }

    [Fact]
    public void ReadFiltered_ByLevel_AndKeyword()
    {
        using var agg = new LogAggregator(_tmpDir);
        agg.Add(new AggregatedLogEntry { Level = "Info", Source = "a1", Message = "boss found Vegiita" });
        agg.Add(new AggregatedLogEntry { Level = "Error", Source = "a1", Message = "socket dropped" });
        agg.Add(new AggregatedLogEntry { Level = "Warning", Source = "b2", Message = "captcha detected" });

        var errorsOnly = agg.ReadFiltered(level: "Error");
        Assert.Single(errorsOnly);
        Assert.Contains("socket dropped", errorsOnly[0].Line);

        var captcha = agg.ReadFiltered(keyword: "captcha");
        Assert.Single(captcha);
        Assert.Contains("b2", captcha[0].Line);

        var all = agg.ReadFiltered();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void ExportToText_WritesAllMatchingLines()
    {
        using var agg = new LogAggregator(_tmpDir);
        agg.Add(new AggregatedLogEntry { Level = "Info", Message = "line one" });
        agg.Add(new AggregatedLogEntry { Level = "Warning", Message = "line two" });

        var outPath = Path.Combine(_tmpDir, "export.txt");
        var count = agg.ExportToText(outPath);

        Assert.Equal(2, count);
        var text = File.ReadAllText(outPath);
        Assert.Contains("line one", text);
        Assert.Contains("line two", text);
    }

    [Fact]
    public void MultipleEntries_AppendWithoutOverwrite()
    {
        using var agg = new LogAggregator(_tmpDir);
        agg.Add(new AggregatedLogEntry { Message = "first" });
        agg.Add(new AggregatedLogEntry { Message = "second" });
        agg.Dispose();

        var files = Directory.GetFiles(_tmpDir, "autoboss_*.jsonl");
        var content = File.ReadAllText(files[0]);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
    }
}
