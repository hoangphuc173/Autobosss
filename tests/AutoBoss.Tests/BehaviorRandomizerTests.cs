using Xunit;
using AutoBossGrabber;

namespace AutoBoss.Tests;

[Collection("PluginEnv")]
public class BehaviorRandomizerTests
{
    private static BehaviorRandomizer Create(bool enabled = true, int intensity = 1, int seed = 42)
        => new BehaviorRandomizer(
            enabledProvider: () => enabled,
            intensityProvider: () => intensity,
            random: new Random(seed));

    // === Dwell ±10% ===

    [Fact]
    public void Dwell_StaysWithinPlusMinus10Percent_Medium()
    {
        var r = Create(intensity: BehaviorRandomizer.IntensityMedium);
        for (int i = 0; i < 200; i++)
        {
            float v = r.RandomizeDwellTime(1.20f);
            Assert.InRange(v, 1.20f * 0.90f, 1.20f * 1.10f);
        }
    }

    [Fact]
    public void Dwind_LowIntensity_NarrowerBand()
    {
        var r = Create(intensity: BehaviorRandomizer.IntensityLow);
        for (int i = 0; i < 200; i++)
        {
            float v = r.RandomizeDwellTime(2.0f);
            Assert.InRange(v, 2.0f * 0.95f, 2.0f * 1.05f);
        }
    }

    [Fact]
    public void Dwind_Disabled_ReturnsBaseExactly()
    {
        var r = Create(enabled: false);
        Assert.Equal(3.3f, r.RandomizeDwellTime(3.3f));
        Assert.Equal(0.45f, r.RandomizeMovementDelay(0.45f));
        Assert.Equal(0f, r.RandomizeMicroPause());
    }

    // === Movement ±20% ===

    [Fact]
    public void MovementDelay_StaysWithinPlusMinus20Percent()
    {
        var r = Create(intensity: BehaviorRandomizer.IntensityMedium);
        for (int i = 0; i < 200; i++)
        {
            float v = r.RandomizeMovementDelay(0.45f);
            Assert.InRange(v, 0.45f * 0.80f, 0.45f * 1.20f);
        }
    }

    // === High intensity ===

    [Fact]
    public void HighIntensity_WiderThanMedium_BoundsRespected()
    {
        var r = Create(intensity: BehaviorRandomizer.IntensityHigh, seed: 7);
        for (int i = 0; i < 300; i++)
        {
            float v = r.RandomizeMovementDelay(1.0f);
            Assert.InRange(v, 0.70f, 1.30f); // ±30% = 1.5 * ±20%
        }
    }

    // === Micro pause ===

    [Fact]
    public void MicroPause_Within50To200ms_Medium()
    {
        var r = Create(intensity: BehaviorRandomizer.IntensityMedium);
        for (int i = 0; i < 100; i++)
        {
            float v = r.RandomizeMicroPause();
            Assert.InRange(v, 0.050f, 0.200f);
        }
    }

    // === Click offset ===

    [Fact]
    public void ClickOffset_Within15Percent_Medium()
    {
        var r = Create(intensity: BehaviorRandomizer.IntensityMedium);
        for (int i = 0; i < 100; i++)
        {
            var (dx, dy) = r.RandomizeClickOffset();
            Assert.InRange(dx, -0.15f, 0.15f);
            Assert.InRange(dy, -0.15f, 0.15f);
        }
    }

    [Fact]
    public void ClickOffset_Disabled_IsZeroCenter()
    {
        var r = Create(enabled: false);
        var (dx, dy) = r.RandomizeClickOffset();
        Assert.Equal(0f, dx);
        Assert.Equal(0f, dy);
    }

    // === Scan direction ===

    [Fact]
    public void ScanDirection_ProducesBothDirectionsOverTime()
    {
        var r = Create();
        int forwards = 0, backwards = 0;
        for (int i = 0; i < 100; i++)
        {
            if (r.RandomizeScanDirection()) forwards++; else backwards++;
        }
        Assert.True(forwards > 10 && backwards > 10, $"fw={forwards} bw={backwards}");
    }

    // === Intensity scale ===

    [Theory]
    [InlineData(BehaviorRandomizer.IntensityLow, 0.5)]
    [InlineData(BehaviorRandomizer.IntensityMedium, 1.0)]
    [InlineData(BehaviorRandomizer.IntensityHigh, 1.5)]
    public void IntensityScale_MapsCorrectly(int intensity, double expected)
    {
        Assert.Equal(expected, BehaviorRandomizer.IntensityScale(intensity));
    }
}
