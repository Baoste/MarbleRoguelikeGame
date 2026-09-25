using System;

namespace MarblesECS
{
    /// <summary>不依赖 Unity，可单独执行回归测试的数值规则。</summary>
    public static class MarbleRules
    {
        public static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        public static ShotQuote Quote(MarbleTuning tuning, float flow)
        {
            if (!IsFinite(flow)) throw new ArgumentOutOfRangeException(nameof(flow));
            var t = Math.Max(0f, Math.Min(1f, flow));
            var investment = tuning.MinInvestment + (tuning.MaxInvestment - tuning.MinInvestment) * t;
            return new ShotQuote(investment, investment * tuning.BloodCostMultiplier,
                tuning.MinRadius + (tuning.MaxRadius - tuning.MinRadius) * t,
                investment * tuning.MassPerBlood,
                tuning.BaseScore,
                tuning.LauncherScoreMultiplier);
        }

        public static float Investment(MarbleTuning tuning, float flow)
        {
            if (!IsFinite(flow)) throw new ArgumentOutOfRangeException(nameof(flow));
            float t = Math.Max(0f, Math.Min(1f, flow));
            return tuning.MinInvestment + (tuning.MaxInvestment - tuning.MinInvestment) * t;
        }

        public static long Score(double baseValue, double drug, double device, double zone, double launcher, double rush, long cap)
        {
            if (baseValue < 0) throw new ArgumentOutOfRangeException(nameof(baseValue));
            CheckMultiplier(drug); CheckMultiplier(device); CheckMultiplier(zone); CheckMultiplier(launcher); CheckMultiplier(rush);
            return Settle(baseValue, 0, 0, CombineDeviceMultiplier(device, launcher), drug, zone, 0, rush, cap);
        }

        /// <summary>DesignRules 27: temporary additions precede multipliers; flat reward precedes Rush.</summary>
        public static long Settle(double baseScore, double bonus, double scoreAdd, double multiplier,
            double scoreMul, double zoneScale, long flatScore, double rush, long cap)
        {
            if (!IsFinite(baseScore) || baseScore < 0 || !IsFinite(bonus) || !IsFinite(scoreAdd) || flatScore < 0)
                throw new ArgumentOutOfRangeException();
            CheckMultiplier(multiplier); CheckMultiplier(scoreMul); CheckMultiplier(zoneScale); CheckMultiplier(rush);
            double value = Math.Max(0d, baseScore + bonus + scoreAdd);
            // A zero multiplier must annihilate even a very large intermediate value.
            value = multiplier == 0 || scoreMul == 0 || zoneScale == 0 ? 0 : value * multiplier * scoreMul * zoneScale;
            value = rush == 0 ? 0 : (value + flatScore) * rush;
            if (double.IsPositiveInfinity(value)) value = cap;
            return RoundScore(value, cap);
        }

        public static double RushProbability(double basis, double bonus, double temporary)
        {
            if (!IsFinite(basis) || !IsFinite(bonus) || !IsFinite(temporary)) throw new ArgumentOutOfRangeException();
            return Math.Max(0d, Math.Min(1d, basis + bonus + temporary));
        }

        public static long RoundScore(double value, long cap)
        {
            if (!IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (cap < 1 || cap > 9000000000000000L) throw new ArgumentOutOfRangeException(nameof(cap));
            if (value >= cap) return cap;
            return Math.Min(cap, (long)Math.Floor(value));
        }

        public static long AddScore(long current, long amount, long cap)
        {
            if (current < 0 || amount < 0 || cap < 1 || current > cap) throw new ArgumentOutOfRangeException();
            return amount >= cap - current ? cap : current + amount;
        }

        public static double CombineDeviceMultiplier(double current, double multiplier)
        {
            CheckMultiplier(current); CheckMultiplier(multiplier);
            return Math.Min(1000d, current * multiplier);
        }

        public static void CheckMultiplier(double value)
        {
            if (!IsFinite(value) || value < 0 || value > 1000d) throw new ArgumentOutOfRangeException(nameof(value));
        }

        /// <summary>玩法专用 PRNG；稳定随机序列不代表跨平台物理确定性。</summary>
        public static bool Roll(ref uint state, double probability)
        {
            if (!IsFinite(probability) || probability < 0 || probability > 1) throw new ArgumentOutOfRangeException(nameof(probability));
            return NextRandom(ref state) < probability;
        }

        public static double NextRandom(ref uint state)
        {
            if (state == 0) state = 0x6D2B79F5u;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return state / 4294967296d;
        }
    }
}
