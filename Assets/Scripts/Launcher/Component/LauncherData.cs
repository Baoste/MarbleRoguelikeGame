using Unity.Entities;

namespace MarblesECS
{
    internal struct LauncherData : IComponentData
    {
        public float Flow;
        public double NextFireAt;
        public byte FireHeld;
        public int PendingSingleShots;
    }
}
