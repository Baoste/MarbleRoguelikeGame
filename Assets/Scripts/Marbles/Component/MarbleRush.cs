using Unity.Entities;

namespace MarblesECS
{
    internal struct MarbleRush : IComponentData
    {
        public double RushChanceBonus;
        public double DurationBonus;
        public double Contribution;
        public bool HasScoreContribution;
        public double ScoreContribution;
    }
}
