using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Immutable solid-contact data. Only valid in its originating OnContact callback.</summary>
    [Preserve]
    public sealed class DeviceImpactInfo
    {
        private readonly DeviceContactContext owner;
        private readonly MarbleContact contact;
        internal DeviceImpactInfo(DeviceContactContext owner, MarbleContact contact)
        { this.owner = owner; this.contact = contact; }
        public Vector3 Position { get { _ = owner.Context; return contact.Position; } }
        public Vector3 Normal { get { _ = owner.Context; return contact.Normal / Mathf.Sqrt(contact.Normal.sqrMagnitude); } }
        public Vector3 IncomingVelocity { get { _ = owner.Context; return contact.IncomingVelocity; } }
        public float IncomingSpeed => IncomingVelocity.magnitude;
    }
}
