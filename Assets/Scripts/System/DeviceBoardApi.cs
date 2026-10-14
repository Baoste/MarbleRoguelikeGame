using System;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Temporary board-wide effects; this API has the lifetime of its owning Lua callback.</summary>
    [Preserve]
    public sealed class DeviceBoardApi
    {
        public bool RushActive => RushSystem.Active(context());
        public double RushMeter => context().Round.RushMeter;
        public double RushMaximum { get { _ = context(); return RushSystem.Threshold; } }
        private readonly Func<SimulationContext> context;
        private readonly int sourceId;
        internal DeviceBoardApi(Func<SimulationContext> context, int sourceId) { this.context = context; this.sourceId = sourceId; }
        public int AddScoreMultiplier(double amount, double seconds)
        { var c = context(); return c.BoardScores.Add(c, sourceId, amount, seconds); }
        public double GetScoreMultiplierEndTime(int effectId)
        { var c = context(); return c.BoardScores.EndTime(c, effectId); }
        public double RushChanceBonus { get { var c = context(); return c.BoardRush.Bonus(c); } }
        public bool SetRushChanceBonus(string key, double amount, double seconds)
        { var c = context(); return c.BoardRush.Set(c, sourceId, key, amount, seconds); }
        public bool RemoveRushChanceBonus(string key)
        { var c = context(); return c.BoardRush.Remove(c, sourceId, key); }
        public double GetRushChanceBonusEndTime(string key)
        { var c = context(); return c.BoardRush.EndTime(c, sourceId, key); }
    }
}
