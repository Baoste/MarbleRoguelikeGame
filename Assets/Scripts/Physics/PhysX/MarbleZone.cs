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

        public bool RushEnabled = true;
        [Tooltip("启用得分叠加：进球增加全盘共享倍率并刷新时间；关闭时不增加、不刷新，也不享受叠加。仅得分区有效。")]
        public bool ScoreStackEnabled = true;
        [Min(0)] public long FlatScore;

        public bool IsValid => TargetId > 0 && MarbleRules.IsFinite(Multiplier) &&
            Multiplier >= 0 && Multiplier <= 1000 &&
            FlatScore >= 0 && (Kind == MarbleContactKind.Score || Kind == MarbleContactKind.Multiplier ||
                Kind == MarbleContactKind.Drain || Kind == MarbleContactKind.RandomScore);

        internal MarbleContact CreateContact(MarbleKey key, Vector3 position)
        {
            return new MarbleContact
            {
                Key = key, Kind = Kind, TargetId = TargetId, Priority = Priority,
                Multiplier = Multiplier, Position = position,
                RushDisabled = !RushEnabled, ScoreStackEnabled = ScoreStackEnabled, FlatScore = FlatScore
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
