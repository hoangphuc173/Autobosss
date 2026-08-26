using System;

namespace AutoBossGrabber;

/// <summary>
/// Randomizer hanh vi giong nguoi (task 20 cua spec) - giam kha nang phat hien bot.
/// - Dwell time ±10% quanh gia tri co so
/// - Movement delay ±20%
/// - Micro-pause 50-200ms giua cac chuoi hanh dong
/// - Huong quet toi/luoi 50-50
/// - Jitter vi tri click trong bounding box (khong bao gio click dung tam)
///
/// Muc do (intensity): 0=low (mot nua bien the), 1=medium (day du),
/// 2=high (1.5x bien the). Tat randomization -> tra ve gia tri goc.
/// Random inject duoc de unit test deterministic.
/// </summary>
public class BehaviorRandomizer
{
    public const int IntensityLow = 0;
    public const int IntensityMedium = 1;
    public const int IntensityHigh = 2;

    private readonly Random _random;
    private readonly Func<bool> _enabledProvider;
    private readonly Func<int> _intensityProvider;

    public BehaviorRandomizer(Func<bool> enabledProvider = null, Func<int> intensityProvider = null, Random random = null)
    {
        _enabledProvider = enabledProvider ?? (() => true);
        _intensityProvider = intensityProvider ?? (() => IntensityMedium);
        _random = random ?? new Random();
    }

    /// <summary>He so bien the theo muc do: low=0.5x, medium=1x, high=1.5x.</summary>
    public static double IntensityScale(int intensity) => intensity switch
    {
        IntensityLow => 0.5,
        IntensityHigh => 1.5,
        _ => 1.0,
    };

    /// <summary>Dwell time ±10% (nhan scale theo intensity). Tat randomization -> goc.</summary>
    public float RandomizeDwellTime(float baseSec)
        => Vary(baseSec, variancePct: 0.10);

    /// <summary>Movement delay ±20%.</summary>
    public float RandomizeMovementDelay(float baseSec)
        => Vary(baseSec, variancePct: 0.20);

    /// <summary>Micro-pause ngau nhien 50-200ms (giay), khong phu thuoc gia tri dau vao.</summary>
    public float RandomizeMicroPause()
    {
        if (!_enabledProvider()) return 0f;
        var scale = IntensityScale(_intensityProvider());
        double lo = 0.050 * scale, hi = 0.200 * scale;
        return (float)(lo + _random.NextDouble() * (hi - lo));
    }

    /// <summary>Huong quet: true = toi, false = luoi (50/50).</summary>
    public bool RandomizeScanDirection()
        => _random.Next(2) == 0;

    /// <summary>
    /// Jitter ti le trong [0..1] quanh tam (dung de tinh diem click):
    /// tra ve offset trong [-0.15..+0.15] cua chieu rong/cao (±15%).
    /// </summary>
    public (float DxRatio, float DyRatio) RandomizeClickOffset()
    {
        if (!_enabledProvider()) return (0f, 0f);
        var scale = IntensityScale(_intensityProvider());
        var max = 0.15 * scale;
        return (
            (float)(_random.NextDouble() * 2 - 1) * (float)max,
            (float)(_random.NextDouble() * 2 - 1) * (float)max);
    }

    /// <summary>Nhan ban goc ±variancePct voi scale intensity; tat -> goc.</summary>
    private float Vary(float baseValue, double variancePct)
    {
        if (!_enabledProvider() || baseValue <= 0f) return baseValue;
        var pct = variancePct * IntensityScale(_intensityProvider());
        var factor = 1.0 + (_random.NextDouble() * 2 - 1) * pct;
        return (float)(baseValue * factor);
    }
}
