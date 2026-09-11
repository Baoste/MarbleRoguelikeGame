using Unity.Entities;

namespace MarblesECS
{
    internal struct MarbleScore : IComponentData
    {
        public double BaseScore;
        public double ScoreBonus;
        public double ScoreMultiplier;
    }
}
