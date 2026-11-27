using UnityEngine;

namespace MarblesECS
{
    public struct MarbleContact
    {
        private const int DeviceTargetIdOffset = 100000;
        internal const int MaxDeviceInstanceId = int.MaxValue - DeviceTargetIdOffset;

        public MarbleKey Key;
        public MarbleContactKind Kind;
        // Numeric ID used for contact ordering and score events. Visit identity also includes the source.
        public int TargetId;
        public int Priority;
        public double Multiplier;
        public double RushChanceAdd;
        public bool RushDisabled;
        public bool ScoreStackEnabled;
        public long FlatScore;
        public Vector3 Position;
        public long LinkKey;
        public float Speed;
        public bool IsPin;
        // Solid-contact snapshot, captured before gameplay reads the post-solver body velocity.
        public bool HasImpact;
        public Vector3 Normal, IncomingVelocity;

        internal int DeviceInstanceId => TargetId - DeviceTargetIdOffset;

        internal static MarbleContact ForDevice(MarbleKey key, int instanceId, Vector3 position, float speed = 0) =>
            new MarbleContact
            {
                Key = key, Kind = MarbleContactKind.Device, TargetId = TargetIdForDevice(instanceId),
                Position = position, Speed = speed, Multiplier = 1, RushDisabled = true
            };

        internal static int TargetIdForDevice(int instanceId) => checked(DeviceTargetIdOffset + instanceId);

        // A link has the same ID from either endpoint. Keep both full instance IDs.
        internal static long LinkId(int firstInstanceId, int secondInstanceId)
        {
            if (firstInstanceId > secondInstanceId)
                (firstInstanceId, secondInstanceId) = (secondInstanceId, firstInstanceId);
            return ((long)firstInstanceId << 32) | (uint)secondInstanceId;
        }
    }
}
