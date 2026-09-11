using System;

namespace MarblesECS
{
    public readonly struct ShotQuote
    {
        public readonly float Investment, Cost, Radius, Mass;
        public readonly double BaseValue;
        public readonly double LauncherMultiplier;
        public ShotQuote(float investment, float cost, float radius, float mass, double baseValue, double launcherMultiplier)
        {
            Investment = investment; Cost = cost; Radius = radius; Mass = mass;
            BaseValue = baseValue; LauncherMultiplier = launcherMultiplier;
        }
    }
}
