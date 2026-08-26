using System.Collections.Generic;
using AutoBossShared;

namespace AutoBossManager.Services
{
    /// <summary>
    /// Strategy preset system (task 19 cua spec).
    /// Ba preset chuan + ApplyPreset copy gia tri vao BotProfile;
    /// user van sua tiep sau khi apply (preset chi la diem bat dau - REQ 7.6).
    /// </summary>
    public static class StrategyPresetManager
    {
        public class Preset
        {
            public string Name { get; init; } = "";
            public string Description { get; init; } = "";
            public StrategyPreset Kind { get; init; }
            public int MaxZoneAttempts { get; init; }
            public float AttackRange { get; init; }
            public float CombatTimeoutSec { get; init; }
            public float RetreatHpPct { get; init; }
            public float LootRadius { get; init; }
            public bool EnableRandomization { get; init; }
            public int RandomizationIntensity { get; init; }
        }

        // REQ 7.1-7.3
        public static readonly Preset Aggressive = new()
        {
            Name = "Aggressive",
            Description = "Nhanh, quet nhieu khu, retreat som, randomization cao",
            Kind = StrategyPreset.Aggressive,
            MaxZoneAttempts = 15,
            AttackRange = 3.0f,
            CombatTimeoutSec = 45f,
            RetreatHpPct = 15f,
            LootRadius = 150f,
            EnableRandomization = true,
            RandomizationIntensity = 2, // high
        };

        public static readonly Preset Balanced = new()
        {
            Name = "Balanced",
            Description = "Can bang cho da so truong hop",
            Kind = StrategyPreset.Balanced,
            MaxZoneAttempts = 15,
            AttackRange = 2.5f,
            CombatTimeoutSec = 60f,
            RetreatHpPct = 20f,
            LootRadius = 200f,
            EnableRandomization = true,
            RandomizationIntensity = 1, // medium
        };

        public static readonly Preset Safe = new()
        {
            Name = "Safe",
            Description = "An toan, dwell lau, retreat tre, randomization thap",
            Kind = StrategyPreset.Safe,
            MaxZoneAttempts = 20,
            AttackRange = 2.0f,
            CombatTimeoutSec = 90f,
            RetreatHpPct = 30f,
            LootRadius = 250f,
            EnableRandomization = true,
            RandomizationIntensity = 0, // low
        };

        public static IReadOnlyList<Preset> All => new[] { Aggressive, Balanced, Safe };

        /// <summary>Tim preset theo ten (khong phan biet hoa thuong). Null khi khong thay/Custom.</summary>
        public static Preset? Find(string name)
        {
            foreach (var p in All)
                if (string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        /// <summary>
        /// Copy gia tri preset vao profile (task 19.2). Tra ve profile de chain.
        /// KHONG dong AccountName/Password - chi thong so chien dau.
        /// </summary>
        public static BotProfile Apply(BotProfile profile, Preset preset)
        {
            profile.Strategy = preset.Kind;
            profile.MaxZoneAttempts = preset.MaxZoneAttempts;
            profile.AttackRange = preset.AttackRange;
            profile.CombatTimeoutSec = preset.CombatTimeoutSec;
            profile.RetreatHpPct = preset.RetreatHpPct;
            profile.LootRadius = preset.LootRadius;
            profile.EnableRandomization = preset.EnableRandomization;
            profile.RandomizationIntensity = preset.RandomizationIntensity;
            return profile;
        }
    }
}
