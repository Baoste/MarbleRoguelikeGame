using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    public struct LauncherMovementState : IComponentData
    {
        public Vector3 LocalPosition;
        public double Travel;
    }
}
