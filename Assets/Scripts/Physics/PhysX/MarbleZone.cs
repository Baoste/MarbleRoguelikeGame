using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>只描述触发区；所有数值变化在 ECS 的物理后阶段提交。</summary>
    [DisallowMultipleComponent]
    public sealed class MarbleZone : MonoBehaviour
    {
        public MarbleContactKind Kind = MarbleContactKind.Score;
        [Min(1)] public int TargetId = 1;
        public int Priority;
        [Range(0f, 50f)] public float Multiplier = 1f;

        [Range(0f, 1f)] public float BaseRushChance = 0.06f;
        public bool RushEnabled = true;
        [Min(0)] public long FlatScore;

        public bool IsValid => TargetId > 0 && MarbleRules.IsFinite(Multiplier) &&
            Multiplier >= 0 && Multiplier <= 1000 &&
            MarbleRules.IsFinite(BaseRushChance) && BaseRushChance >= 0 && BaseRushChance <= 1 &&
            FlatScore >= 0 && (Kind == MarbleContactKind.Score || Kind == MarbleContactKind.Multiplier ||
                Kind == MarbleContactKind.Drain || Kind == MarbleContactKind.RandomScore);

        internal MarbleContact CreateContact(MarbleKey key, Vector3 position)
        {
            return new MarbleContact
            {
                Key = key, Kind = Kind, TargetId = TargetId, Priority = Priority,
                Multiplier = Multiplier, Position = position,
                BaseRushChance = BaseRushChance, RushDisabled = !RushEnabled, FlatScore = FlatScore
            };
        }

        private void Reset()
        {
            TargetId = TargetIdAllocator.ForZone(this, 0);
        }

        private void OnValidate()
        {
            TargetId = TargetIdAllocator.ForZone(this, TargetId);
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = Kind == MarbleContactKind.Score ? new Color(0.2f, 1f, 0.4f, 0.8f) : Color.cyan;
            var previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previous;
        }
    }
}
