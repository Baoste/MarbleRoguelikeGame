using UnityEngine;

namespace MarblesECS
{
    /// <summary>TrySpawn 失败时不得留下活动物理球；Remove 必须可重复调用。</summary>
    public interface IMarblePhysics
    {
        bool TrySpawn(MarbleSpawnData data);
        bool TryGetPosition(MarbleKey key, out Vector3 position);
        void Remove(MarbleKey key);
    }
}
