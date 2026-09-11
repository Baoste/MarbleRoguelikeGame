using System;

namespace MarblesECS
{
    internal static class BalanceGuard
    {
        internal const long MaxInteger = 9000000000000000L;

        internal static void Range(double value, double minimum, double maximum, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name, "Expected " + minimum + " .. " + maximum + ".");
        }

        internal static void Text(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
                throw new ArgumentException("A name of 1 .. 80 characters is required.", name);
        }
    }
}
