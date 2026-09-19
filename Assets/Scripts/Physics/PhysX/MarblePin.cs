using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>A solid pin's view. Callbacks report facts; ECS applies the effect once per marble.</summary>
    [DisallowMultipleComponent]
    public sealed class MarblePin : MonoBehaviour
    {
        [Min(1)] public int TargetId = 100000;
        [Range(0f, 1000f)] public float ScoreMultiplier = 2f;
        [Range(0f, 1f)] public float RushChanceAdd;

        public bool IsValid => TargetId > 0 && MarbleRules.IsFinite(ScoreMultiplier) &&
            ScoreMultiplier >= 0 && ScoreMultiplier <= 1000 && MarbleRules.IsFinite(RushChanceAdd) &&
            RushChanceAdd >= 0 && RushChanceAdd <= 1;

        internal MarbleContact CreateContact(MarbleKey key, Vector3 position) => new MarbleContact
        {
            Key = key, TargetId = TargetId, Position = position,
            Kind = ScoreMultiplier == 1 ? MarbleContactKind.RushPin : MarbleContactKind.Multiplier,
            Multiplier = ScoreMultiplier, RushChanceAdd = RushChanceAdd, RushDisabled = true
        };

        private void Reset()
        {
            TargetId = TargetIdAllocator.ForPin(this, 0);
        }

        private void OnValidate()
        {
            TargetId = TargetIdAllocator.ForPin(this, TargetId);
        }
    }
}
