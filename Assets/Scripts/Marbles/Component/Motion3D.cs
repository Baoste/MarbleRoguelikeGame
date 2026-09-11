using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal struct Motion3D : IComponentData
    {
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
    }
}
