using Unity.Entities;

namespace MarblesECS
{
    /// <summary>Captured at spawn. PhysX material is a projection of this ECS state.</summary>
    internal struct MarblePhysicsProperties : IComponentData
    {
        public float Restitution;
    }
}
