using Unity.Entities;

namespace MarblesECS
{
    internal struct LauncherData : IComponentData
    {
        public float Flow;
        // Both input modes measure their own interval from the last successful shot.
        public double LastFireAt;
        public byte HasFired;
        public byte FireHeld;
        // At most one request for the next simulation step; never a shot queue.
        public byte FireRequested;
        public double HeldSeconds;
        public double HeldShotProgress;
    }
}
