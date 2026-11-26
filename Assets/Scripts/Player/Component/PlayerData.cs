using Unity.Entities;

namespace MarblesECS
{
    internal struct PlayerData : IComponentData
    {
        public float Blood;
        public double BloodSpent;
        public float BloodCostMultiplier;
        public long BiomassScrapBalance;
    }
}
