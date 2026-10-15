using UnityEngine;
using UnityEngine.SceneManagement;

namespace MRes
{
    /// <summary>Explicit lifecycle clock for isolated, manually stepped training players.</summary>
    public static class MResManualSimulation
    {
        public static bool Enabled { get; set; }

        public static void Step(Scene scene, float seconds)
        {
            if (!Enabled) throw new System.InvalidOperationException("Manual MRes clock is disabled.");
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var item in root.GetComponentsInChildren<MResColliderActivation>()) item.ManualTick();
                foreach (var item in root.GetComponentsInChildren<MResRuntimeBehaviour>())
                    if (item.isActiveAndEnabled) item.ManualTick(seconds);
            }
            MResLuaRuntime.Tick();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Enabled = false;
    }
}
