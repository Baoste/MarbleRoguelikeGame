using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal struct Transform3D : IComponentData
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }
}
