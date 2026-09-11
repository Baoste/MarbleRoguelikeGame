using Unity.Entities;

namespace MarblesECS
{
    public struct LauncherMovementInput : IComponentData
    {
        public float Horizontal;
        public float RequestedX;
        public byte Automatic;
        public byte HasPositionRequest;
    }
}
