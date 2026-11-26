using System;
using Unity.Entities;

namespace MarblesECS
{
    internal struct MarbleScore : IComponentData
    {
        public double BaseScore;
        public double ScoreBonus;
        public double BaseMultiplier;
        private ScoreMultiplierProduct deviceMultiplier;

        internal ScoreMultiplierProduct DeviceMultiplier => deviceMultiplier;
        internal double ScoreMultiplier
        {
            get
            {
                var product = deviceMultiplier;
                product.Multiply(BaseMultiplier);
                return product.Value;
            }
        }

        internal bool SetDeviceMultiplier(double factor)
        {
            MarbleRules.CheckMultiplier(factor);
            return deviceMultiplier.Set(factor);
        }

        internal bool MultiplyDeviceMultiplier(double factor) => deviceMultiplier.Multiply(factor);
    }

    // Store the product in powers of 2^256 so a later reciprocal can undo even very large factors.
    // The default representation is ×1; a negative significand represents an explicit ×0.
    internal struct ScoreMultiplierProduct
    {
        private static readonly double Scale = Math.Pow(2, 256);
        private double significand;
        private long exponent;

        private double Significand => significand == 0 ? 1 : significand;

        internal double Value
        {
            get
            {
                if (significand < 0 || exponent < -5) return 0;
                if (exponent > 0) return 1000;
                double result = Significand;
                for (long i = exponent; i < 0; i++) result /= Scale;
                return Math.Min(1000, result);
            }
        }

        internal bool Set(double factor)
        {
            var next = new ScoreMultiplierProduct();
            next.Multiply(factor);
            bool changed = Significand != next.Significand || exponent != next.exponent;
            this = next;
            return changed;
        }

        internal bool Multiply(double factor)
        {
            if (!MarbleRules.IsFinite(factor) || factor < 0) throw new ArgumentOutOfRangeException(nameof(factor));
            if (factor == 1 || significand < 0) return false;
            if (factor == 0)
            {
                significand = -1;
                exponent = 0;
                return true;
            }
            var next = new ScoreMultiplierProduct { significand = factor };
            next.Normalize();
            significand = Significand * next.significand;
            exponent += next.exponent;
            Normalize();
            return true;
        }

        private void Normalize()
        {
            while (significand >= Scale) { significand /= Scale; exponent++; }
            while (significand < 1) { significand *= Scale; exponent--; }
        }
    }
}
