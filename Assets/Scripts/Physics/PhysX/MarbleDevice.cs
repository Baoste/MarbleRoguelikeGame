using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed class MarbleDevice : MonoBehaviour
    {
        internal static readonly HashSet<MarbleDevice> Active = new HashSet<MarbleDevice>();
        public int InstanceId, PairId;
        public DeviceKind Kind;
        public float Radius = .12f, Range = .2f, Strength = 2;
        public Vector3 Forward => transform.rotation * Vector3.back;
        public Vector3 Up => transform.up;
        private void OnEnable() { Active.Add(this); }
        private void OnDisable() { Active.Remove(this); }
        public MarbleContact Contact(MarbleKey key, Vector3 position, float speed = 0) => new MarbleContact
        { Key = key, TargetId = 100000 + InstanceId, Kind = MarbleContactKind.Device, Multiplier = 1, Position = position, Speed = speed, RushDisabled = true };

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Kind == DeviceKind.Magnet ? Color.magenta : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, Kind == DeviceKind.Magnet || Kind == DeviceKind.Amplifier ? Range : Radius);
            Gizmos.DrawRay(transform.position, Forward * .4f);
        }
    }
}
