using System;

namespace MarblesECS
{
    public static class GamblingWheelRules
    {
        public const int OutcomeCount = 8;
        public const double InitialSeconds = 2;
        public const double MinimumSeconds = .2;
        public static int Multiplier(int index)
        {
            switch (index)
            {
                case 0: return -2;
                case 1: return -1;
                case 2: return 0;
                case 3: return 1;
                case 4: return 5;
                case 5: return 10;
                case 6: return 20;
                case 7: return 50;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
        public static int Outcome(double draw)
        {
            if (!MarbleRules.IsFinite(draw) || draw < 0 || draw >= 1)
                throw new ArgumentOutOfRangeException(nameof(draw));
            return Multiplier((int)(draw * OutcomeCount));
        }
        public static double WindowSeconds(int count) => count <= 0 ? 0 :
            Math.Max(MinimumSeconds, InitialSeconds / Math.Sqrt(count));
    }
}
