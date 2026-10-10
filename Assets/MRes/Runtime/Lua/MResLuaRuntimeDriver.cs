using UnityEngine;

namespace MRes
{
    [AddComponentMenu("")]
    public sealed class MResLuaRuntimeDriver : MonoBehaviour
    {
        private void Update() => MResLuaRuntime.Tick();
        private void OnDestroy() => MResLuaRuntime.DriverDestroyed(this);
        private void OnApplicationQuit() => MResLuaRuntime.DriverDestroyed(this);
    }
}
