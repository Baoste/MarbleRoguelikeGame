using Unity.Entities;

namespace MarblesECS
{
    internal struct ActiveScoreEffectData : IBufferElementData
    {
        public int EffectId;
        public double Multiplier;
        public double EndsAt;
    }
}
