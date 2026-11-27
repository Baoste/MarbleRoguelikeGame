using System;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace MarblesECS
{
    [Flags]
    internal enum DeviceLuaCallbacks { Tick = 1, BallUpdate = 2, Field = 4 }

    /// <summary>One isolated device script. Can also run without a presentation object.</summary>
    public sealed class DeviceLuaScript : IDisposable
    {
        private LuaTable environment;
        private readonly Dictionary<string, LuaFunction> functions = new Dictionary<string, LuaFunction>();
        private readonly DeviceMetadata metadata;
        private readonly string name;
        private GameObject root;
        private DeviceLuaObject self;
        private bool initialized, disposing;
        public bool IsDisposed { get; private set; }
        public DeviceMetadata Metadata => metadata;
        internal DeviceLuaCallbacks Callbacks { get; private set; }
        internal event Action CallbacksChanged;
        internal DeviceLuaScript(string source, DeviceMetadata metadata, string name)
        {
            this.metadata = metadata; this.name = name;
            try
            {
                var lua = DeviceLuaRuntime.Env;
                environment = lua.NewTable();
                using (var meta = lua.NewTable()) { meta.Set("__index", lua.Global); environment.SetMetaTable(meta); }
                environment.Set("_G", environment); environment.Set("metadata", metadata);
                DisposeResults(lua.DoString(source, "Device:" + name, environment));
                initialized = true;
                RefreshScheduledCallbacks();
                DeviceLuaRuntime.Register(this);
            }
            catch (Exception e) { Dispose(); throw new DeviceLuaException("Lua initialization failed: " + name, e); }
        }
        public void BindRoot(GameObject value) => BindRoot(value, null);
        internal void BindRoot(GameObject value, Transform authoredVisual)
        {
            if (IsDisposed) return;
            root = value;
            self = value == null ? null : new DeviceLuaObject(value, metadata, authoredVisual);
            environment.Set("self", self);
        }
        internal void AttachPresentation(GameObject owner, Transform model)
        {
            if (ReferenceEquals(root, owner)) self?.AttachPresentation(model);
        }
        internal void UnbindRoot(GameObject value) { if (ReferenceEquals(root, value)) BindRoot(null); }

        // Initialization callbacks can define Lua globals before their first dispatch. Probe without
        // locking in a function binding earlier than HasFunction did before scheduled dispatch.
        internal void RefreshScheduledCallbacks()
        {
            if (IsDisposed) return;
            DeviceLuaCallbacks callbacks = 0;
            if (HasScheduledFunction("OnTick")) callbacks |= DeviceLuaCallbacks.Tick;
            if (HasScheduledFunction("OnBallUpdate")) callbacks |= DeviceLuaCallbacks.BallUpdate;
            if (HasScheduledFunction("OnField")) callbacks |= DeviceLuaCallbacks.Field;
            SetCallbacks(callbacks);
        }

        private void SetCallbacks(DeviceLuaCallbacks callbacks)
        {
            if (Callbacks == callbacks) return;
            Callbacks = callbacks;
            CallbacksChanged?.Invoke();
        }

        private bool HasScheduledFunction(string eventName)
        {
            if (functions.TryGetValue(eventName, out var cached)) return cached != null;
            environment.Get(eventName, out LuaFunction function);
            using (function) return function != null;
        }

        public bool HasFunction(string eventName)
        {
            if (IsDisposed || environment == null) return false;
            if (!functions.TryGetValue(eventName, out var function))
            { environment.Get(eventName, out function); functions.Add(eventName, function); }
            return function != null;
        }
        public void Invoke(string eventName, params object[] args)
        {
            if (!HasFunction(eventName)) return;
            try { DisposeResults(functions[eventName].Call(args)); }
            catch (Exception e) { throw new DeviceLuaException(name + ": " + eventName + " failed.", e); }
        }
        private static void DisposeResults(object[] results)
        { if (results != null) foreach (var value in results) if (value is LuaBase lua) lua.Dispose(); }
        public void Dispose()
        {
            if (IsDisposed || disposing) return;
            disposing = true;
            try { if (initialized) Invoke("OnDestroy"); }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                IsDisposed = true;
                foreach (var function in functions.Values) function?.Dispose();
                functions.Clear(); environment?.Dispose(); environment = null; root = null; self = null;
                SetCallbacks(0);
                DeviceLuaRuntime.Unregister(this);
            }
        }
    }
}
