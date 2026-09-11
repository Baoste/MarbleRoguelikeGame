using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class ZoneDefinition
    {
        public uint Id;
        public string Name;
        // 0 = fixed score, 1 = pooled gambling score, 2 = drain.
        public int Kind;
        public double ScoreMultiplier = 1;
        public bool RushEnabled = true;
        public int Priority = 100;
        public float CenterX;
        public float CenterZ = -6.8f;
        public float Width = 1.8f;
        public float Depth = 1.6f;

        public ZoneDefinition Copy() { return (ZoneDefinition)MemberwiseClone(); }

        public void Validate()
        {
            BalanceGuard.Range(Id, 1, 99999, nameof(Id));
            BalanceGuard.Text(Name, nameof(Name));
            BalanceGuard.Range(Kind, 0, 2, nameof(Kind));
            BalanceGuard.Range(ScoreMultiplier, 0, 1000, nameof(ScoreMultiplier));
            BalanceGuard.Range(CenterX, -100, 100, nameof(CenterX));
            BalanceGuard.Range(CenterZ, -100, 100, nameof(CenterZ));
            BalanceGuard.Range(Width, 0.1, 100, nameof(Width));
            BalanceGuard.Range(Depth, 0.1, 100, nameof(Depth));
            if (Kind == 2 && (RushEnabled || ScoreMultiplier != 0))
                throw new ArgumentException("Drain zones must have zero multiplier and Rush disabled.");
        }
    }
}
