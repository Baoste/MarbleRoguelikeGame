using UnityEngine;

namespace MarblesECS
{
    /// <summary>完整复制得分数据，UI 无需访问已经销毁的实体。</summary>
    public struct MarbleScoreEvent
    {
        public MarbleKey Key;
        public int ZoneId;
        public Vector3 Position;
        public double BaseValue;
        public double ScoreBonus, ScoreAdd;
        public double PermanentMultiplier, ModifierMultiplier;
        public long FlatScore;
        public double DrugMultiplier, DeviceMultiplier, ZoneMultiplier, LauncherMultiplier, RushMultiplier;
        public long Score;
        public long RoundScore;
        public bool IsPending;
        public long PendingScore;
    }
}
