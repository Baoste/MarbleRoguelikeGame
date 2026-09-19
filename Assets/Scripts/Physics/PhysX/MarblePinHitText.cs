using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    [DisallowMultipleComponent]
    public sealed class MarblePinHitText : MonoBehaviour
    {
        public string Prefix = "X";
        public Vector3 WorldOffset = new Vector3(0f, 0.15f, 0f);

        private readonly HashSet<int> playedPinIds = new HashSet<int>();

        internal void ResetForSpawn()
        {
            playedPinIds.Clear();
        }

        internal void Play(MarblePin pin, Vector3 worldPosition)
        {
            if (pin == null || pin.ScoreMultiplier == 1f || !playedPinIds.Add(pin.TargetId))
                return;

            WorldTextEffectPool.TryShow(
                Prefix + pin.ScoreMultiplier.ToString("0.##"),
                worldPosition + WorldOffset);
        }
    }
}
