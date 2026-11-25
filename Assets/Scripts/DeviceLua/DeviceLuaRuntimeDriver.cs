using UnityEngine;

namespace MarblesECS
{
    [AddComponentMenu("")]
    public sealed class DeviceLuaRuntimeDriver : MonoBehaviour
    {
        private void Update() => DeviceLuaRuntime.Tick();
        private void OnDestroy() => DeviceLuaRuntime.DriverDestroyed(this);
        private void OnApplicationQuit() => DeviceLuaRuntime.DriverDestroyed(this);
    }
}
