using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    public struct LauncherMovementConfig : IComponentData
    {
        public Vector3 InitialLocalPosition;
        public float HalfWidth;
        public float Speed;
    }
}
