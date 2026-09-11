using Unity.Entities;

namespace MarblesECS
{
    internal struct PlayerData : IComponentData
    {
        public float Blood;
        public float BloodCostMultiplier;
        public double DrugScoreMultiplier;
    }
}
