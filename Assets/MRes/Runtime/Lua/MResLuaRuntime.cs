using System;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace MRes
{
    public static class MResLuaRuntime
    {
        private static LuaEnv env;
        private static MResLuaRuntimeDriver driver;
        private static bool disposing;
        private static readonly HashSet<MResLuaScript> Behaviours = new HashSet<MResLuaScript>();
        public static int ActiveBehaviourCount => Behaviours.Count;
        public static LuaEnv Env
        {
            get
            {
                if (disposing) throw new MResLuaException("Cannot create a Lua environment during shutdown.");
                if (env == null)
                {
                    try { env = new LuaEnv(); }
                    catch (Exception e) { throw new MResLuaException("Could not initialize xLua. Check the native plugin for this platform.", e); }
                }
                if (Application.isPlaying && driver == null)
                {
                    var go = new GameObject("MRes Lua Runtime") { hideFlags = HideFlags.HideInHierarchy };
                    driver = go.AddComponent<MResLuaRuntimeDriver>();
                    UnityEngine.Object.DontDestroyOnLoad(go);
                }
                return env;
            }
        }
        internal static void Register(MResLuaScript behaviour) => Behaviours.Add(behaviour);
        internal static void Unregister(MResLuaScript behaviour) => Behaviours.Remove(behaviour);
        public static void Tick() { env?.Tick(); }
        public static void Dispose()
        {
            if (disposing) return;
            disposing = true;
            try
            {
                // Release functions/tables before the VM, regardless of Unity's OnDestroy order.
                foreach (var behaviour in new List<MResLuaScript>(Behaviours)) behaviour.Dispose();
                Behaviours.Clear();
                if (env != null) { env.Dispose(); env = null; }
            }
            finally { disposing = false; }
        }
        internal static void DriverDestroyed(MResLuaRuntimeDriver value)
        {
            if (!ReferenceEquals(driver, value)) return;
            try { Dispose(); } finally { driver = null; }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Dispose();
            if (driver != null) MResLoadHandle.Destroy(driver.gameObject);
            driver = null;
        }
    }
}
