using System;

namespace MarblesECS
{
    [Flags]
    public enum ProductionFeature : byte
    {
        None = 0,
        Shop = 1,
        DevicePlacement = 2,
        Rush = 4,
        GamblingWheel = 8,
        All = Shop | DevicePlacement | Rush | GamblingWheel
    }
}
