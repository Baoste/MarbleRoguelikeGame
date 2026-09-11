using Unity.Entities;

namespace MarblesECS
{
    internal struct MarbleRush : IComponentData
    {
        public double BaseRushChance;
        public double RushChanceBonus;
    }
}
