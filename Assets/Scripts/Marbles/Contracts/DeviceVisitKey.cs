using System;

namespace MarblesECS
{
    // Contact sources have positive 32-bit IDs and fit together with a source tag in one word.
    internal readonly struct ContactTargetKey : IEquatable<ContactTargetKey>
    {
        private enum Source : byte { Zone, Pin, Device }
        internal readonly long Value;

        private ContactTargetKey(Source source, int id) => Value = ((long)source << 32) | (uint)id;

        internal static ContactTargetKey ForDevice(int instanceId) => new ContactTargetKey(Source.Device, instanceId);

        internal static ContactTargetKey FromContact(in MarbleContact contact)
        {
            if (contact.Kind == MarbleContactKind.Device) return ForDevice(contact.DeviceInstanceId);
            bool pin = contact.IsPin || contact.Kind == MarbleContactKind.Pin ||
                contact.Kind == MarbleContactKind.RushPin || contact.Kind == MarbleContactKind.Pierced;
            return new ContactTargetKey(pin ? Source.Pin : Source.Zone, contact.TargetId);
        }

        public bool Equals(ContactTargetKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ContactTargetKey other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
    }

    internal readonly struct DeviceVisitKey : IEquatable<DeviceVisitKey>
    {
        // Lua and link keys need their full 64 bits, so store their namespace separately.
        private enum Scope : byte { Contact, SharedLua, Pair, Link }
        private readonly long value;
        private readonly Scope scope;

        private DeviceVisitKey(Scope scope, long value) { this.scope = scope; this.value = value; }

        internal static DeviceVisitKey ForTarget(ContactTargetKey target) => new DeviceVisitKey(Scope.Contact, target.Value);
        internal static DeviceVisitKey ForDevice(int instanceId) => ForTarget(ContactTargetKey.ForDevice(instanceId));
        internal static DeviceVisitKey ForSharedLua(long key) => new DeviceVisitKey(Scope.SharedLua, key);

        internal static DeviceVisitKey FromLuaArgument(long key, int instanceId, int pairId, long linkKey)
        {
            if (key == 0) return ForDevice(instanceId);
            // The legacy numeric aliases are scoped to the current callback.
            if (key == linkKey) return new DeviceVisitKey(Scope.Link, linkKey);
            if (key == -(long)pairId) return new DeviceVisitKey(Scope.Pair, pairId);
            return ForSharedLua(key);
        }

        public bool Equals(DeviceVisitKey other) => value == other.value && scope == other.scope;
        public override bool Equals(object obj) => obj is DeviceVisitKey other && Equals(other);
        public override int GetHashCode() => unchecked((value.GetHashCode() * 397) ^ (int)scope);
    }
}
