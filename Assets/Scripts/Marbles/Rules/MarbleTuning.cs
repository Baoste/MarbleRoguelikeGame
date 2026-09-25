using System;

namespace MarblesECS
{
    /// <summary>示例数值，不是策划定案。创建模拟时应复制，避免运行中改变规则。</summary>
    [Serializable]
    public sealed class MarbleTuning
    {
        public float InitialBlood = 100f;
        public float BloodCapacity = 100f;
        public float MinInvestment = 1f;
        public float MaxInvestment = 5f;
        // Compatibility values for the explicit content-free simulation API only.
        // Scene gameplay reads GameContent; JSON/Excel cannot author these fields.
        [NonSerialized] public float MinRadius = 0.12f;
        [NonSerialized] public float MaxRadius = 0.22f;
        [NonSerialized] public float MassPerBlood = 0.1f;
        public uint MarbleDefinitionId = 1;
        [NonSerialized] public double BaseScore = 10d;
        [NonSerialized] public double BaseRushChance;
        [NonSerialized] public float BloodCostMultiplier = 1f;
        public float FireIntervalSeconds = 0.15f;
        [NonSerialized] public float LaunchSpeed = 12f;
        [NonSerialized] public float LauncherScoreMultiplier = 1f;
        public int MaxActiveMarbles = 256;
        [NonSerialized] public float MaxLifeSeconds = 45f;
        public float DrainTimeoutSeconds = 15f;
        public float DespawnBelowY = -10f;
        public float WorldBoundsExtent = 200f;
        [NonSerialized] public float RushDurationSeconds = 5f;
        [NonSerialized] public float MaxRushSeconds = 20f;
        [NonSerialized] public float RushTimeScale = 1f;
        [NonSerialized] public bool RushExtendsDuration;
        [NonSerialized] public float BaseRestitution = 0.45f;
        public int MaxActiveBounceDrugs = 16;
        public long MaxPerMarbleScore = 1000000000L;
        public long MaxRoundScore = 9000000000000L;
        public long TargetScore = 1000L;
        public uint RandomSeed = 12345;

        public MarbleTuning Copy() { return (MarbleTuning)MemberwiseClone(); }

        public void Validate()
        {
            Positive(BloodCapacity, nameof(BloodCapacity));
            if (!MarbleRules.IsFinite(InitialBlood) || InitialBlood < 0 || InitialBlood > BloodCapacity)
                throw new ArgumentOutOfRangeException(nameof(InitialBlood));
            Positive(MinInvestment, nameof(MinInvestment));
            Positive(MaxInvestment, nameof(MaxInvestment));
            Positive(MinRadius, nameof(MinRadius)); Positive(MaxRadius, nameof(MaxRadius));
            if (MaxInvestment < MinInvestment || MaxRadius < MinRadius)
                throw new ArgumentException("最大投入/半径不得小于最小值。");
            Positive(MassPerBlood, nameof(MassPerBlood)); if (MarbleDefinitionId == 0) throw new ArgumentOutOfRangeException(nameof(MarbleDefinitionId));
            if (!MarbleRules.IsFinite(BaseScore) || BaseScore < 0) throw new ArgumentOutOfRangeException(nameof(BaseScore));
            if (!MarbleRules.IsFinite(BaseRushChance) || BaseRushChance < 0 || BaseRushChance > 1)
                throw new ArgumentOutOfRangeException(nameof(BaseRushChance));
            Positive(BloodCostMultiplier, nameof(BloodCostMultiplier));
            Positive(FireIntervalSeconds, nameof(FireIntervalSeconds)); Positive(LaunchSpeed, nameof(LaunchSpeed));
            Multiplier(LauncherScoreMultiplier, nameof(LauncherScoreMultiplier));
            Positive(MaxLifeSeconds, nameof(MaxLifeSeconds)); Positive(DrainTimeoutSeconds, nameof(DrainTimeoutSeconds));
            Positive(WorldBoundsExtent, nameof(WorldBoundsExtent)); Positive(RushDurationSeconds, nameof(RushDurationSeconds));
            Positive(MaxRushSeconds, nameof(MaxRushSeconds));
            Positive(RushTimeScale, nameof(RushTimeScale));
            if (MaxRushSeconds < RushDurationSeconds) throw new ArgumentOutOfRangeException(nameof(MaxRushSeconds));
            if (!MarbleRules.IsFinite(BaseRestitution) || BaseRestitution < 0 || BaseRestitution > 1)
                throw new ArgumentOutOfRangeException(nameof(BaseRestitution));
            if (MaxActiveBounceDrugs < 1 || MaxActiveBounceDrugs > 128)
                throw new ArgumentOutOfRangeException(nameof(MaxActiveBounceDrugs));
            if (!MarbleRules.IsFinite(DespawnBelowY)) throw new ArgumentOutOfRangeException(nameof(DespawnBelowY));
            if (MaxActiveMarbles < 1 || MaxActiveMarbles > 100000) throw new ArgumentOutOfRangeException(nameof(MaxActiveMarbles));
            if (MaxPerMarbleScore < 1 || MaxPerMarbleScore > MaxRoundScore || MaxRoundScore > 9000000000000000L)
                throw new ArgumentOutOfRangeException(nameof(MaxRoundScore));
            if (TargetScore < 1 || TargetScore > MaxRoundScore) throw new ArgumentOutOfRangeException(nameof(TargetScore));
            // 预先发现组合参数溢出，避免扣血之后才发现球的数据不可用。
            var quote = MarbleRules.Quote(this, 1f);
            Positive(quote.Cost, "最大单珠耗血"); Positive(quote.Mass, "最大单珠质量");
            quote = MarbleRules.Quote(this, 0f);
            Positive(quote.Cost, "最小单珠耗血"); Positive(quote.Mass, "最小单珠质量");
        }

        private static void Positive(float value, string name)
        {
            if (!MarbleRules.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
        }

        private static void Multiplier(float value, string name)
        {
            if (!MarbleRules.IsFinite(value) || value <= 0 || value > 1000) throw new ArgumentOutOfRangeException(name);
        }
    }
}
