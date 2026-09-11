using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal struct PhysicsHandle : IComponentData
    {
        public int BodyHandle;
    }
}
