using Unity.Entities;

namespace MarblesECS
{
    internal struct ShotOrigin : IComponentData
    {
        public int RoundId;
        public ulong SpawnSequence;
        public Entity Launcher;
        public float BloodInvestment;
    }
}
