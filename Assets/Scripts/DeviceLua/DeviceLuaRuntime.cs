using System;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace MarblesECS
{
    public static class DeviceLuaRuntime
    {
        private static LuaEnv env;
        private static DeviceLuaRuntimeDriver driver;
        private static bool disposing;
        private static readonly HashSet<DeviceLuaScript> Behaviours = new HashSet<DeviceLuaScript>();
        public static int ActiveBehaviourCount => Behaviours.Count;
        public static LuaEnv Env
        {
            get
            {
                if (disposing) throw new DeviceLuaException("Cannot create a Lua environment during shutdown.");
                if (env == null)
                {
                    try { env = new LuaEnv(); }
                    catch (Exception e) { throw new DeviceLuaException("Could not initialize xLua. Check the native plugin for this platform.", e); }
                }
                if (Application.isPlaying && driver == null)
                {
                    var go = new GameObject("Device Lua Runtime") { hideFlags = HideFlags.HideInHierarchy };
                    driver = go.AddComponent<DeviceLuaRuntimeDriver>();
                    UnityEngine.Object.DontDestroyOnLoad(go);
                }
                return env;
            }
        }
        internal static void Register(DeviceLuaScript behaviour) => Behaviours.Add(behaviour);
        internal static void Unregister(DeviceLuaScript behaviour) => Behaviours.Remove(behaviour);
        public static void Tick() { env?.Tick(); }
        public static void Dispose()
        {
            if (disposing) return;
            disposing = true;
            try
            {
                // Release functions/tables before the VM, regardless of Unity's OnDestroy order.
                foreach (var behaviour in new List<DeviceLuaScript>(Behaviours)) behaviour.Dispose();
                Behaviours.Clear();
                if (env != null) { env.Dispose(); env = null; }
            }
            finally { disposing = false; }
        }
        internal static void DriverDestroyed(DeviceLuaRuntimeDriver value)
        {
            if (!ReferenceEquals(driver, value)) return;
            try { Dispose(); } finally { driver = null; }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Dispose();
            if (driver != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(driver.gameObject);
                else UnityEngine.Object.DestroyImmediate(driver.gameObject);
            }
            driver = null;
        }
    }
}
