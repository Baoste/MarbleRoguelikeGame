using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        [Header("Board Geometry")]
        [Tooltip("Optional trapezoidal board boundary. Its transform must have unit scale.")]
        public BoardPlacementSurface PlacementSurface;
        [Tooltip("Defaults to the placement surface, or PhysicsRoot on a flat board.")]
        public Transform DevicePlacementRoot;
        [Tooltip("Defaults to the device placement root.")]
        public Transform DevicePlacementPlane;
        public float DevicePlaneDistance;

        internal Transform DeviceOrigin => DevicePlacementRoot != null ? DevicePlacementRoot :
            (PlacementSurface != null ? PlacementSurface.transform : PhysicsRoot);
        internal Transform DevicePlane => DevicePlacementPlane != null ? DevicePlacementPlane : DeviceOrigin;

        internal bool TryGetDevicePlacementPose(float x, float z, float angle, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            return bridge != null && bridge.TryGetPlacementPose(x, z, angle, out position, out rotation);
        }

        internal bool TryPickPlacedDevice(Ray ray, int excludedId, out int id)
        {
            id = 0;
            return bridge != null && bridge.TryPickDevice(ray, out id, clickableOnly: false, excludedId: excludedId);
        }

        internal bool TryGetDevicePose(int id, out DevicePose pose)
        {
            pose = default;
            return bridge != null && bridge.TryGetDevicePose(id, out pose);
        }
    }
}
