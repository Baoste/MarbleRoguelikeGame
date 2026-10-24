using Unity.Entities;

namespace MarblesECS
{
    internal struct LauncherData : IComponentData
    {
        public float Flow;
        public double NextFireAt;
        public byte FireHeld;
        // At most one request for the next simulation step; never a shot queue.
        public byte FireRequested;
        public double HeldSeconds;
        public double HeldShotProgress;
    }
}
