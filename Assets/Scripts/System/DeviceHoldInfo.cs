using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Read-only entry snapshot for OnHoldEnd; follows its callback's lifetime.</summary>
    [Preserve]
    public sealed class DeviceHoldInfo
    {
        private readonly DeviceContactContext owner;
        private readonly HeldMotion hold;
        private readonly double elapsed;
        internal DeviceHoldInfo(DeviceContactContext owner, HeldMotion hold, double time)
        { this.owner = owner; this.hold = hold; elapsed = time - hold.StartedAt; }
        public Vector3 EntryVelocity { get { _ = owner.Context; return hold.OriginalVelocity; } }
        public float EntrySpeed => EntryVelocity.magnitude;
        public Vector3 EntryAngularVelocity { get { _ = owner.Context; return hold.OriginalSpin; } }
        public float Duration { get { _ = owner.Context; return hold.Duration; } }
        public bool IsManual { get { _ = owner.Context; return hold.Manual; } }
        public double Elapsed { get { _ = owner.Context; return elapsed; } }
    }
}
