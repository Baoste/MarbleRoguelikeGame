using System;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace MarblesECS
{
    /// <summary>One isolated device script. Can also run without a presentation object.</summary>
    public sealed class DeviceLuaScript : IDisposable
    {
        private LuaTable environment;
        private readonly Dictionary<string, LuaFunction> functions = new Dictionary<string, LuaFunction>();
        private readonly DeviceMetadata metadata;
        private readonly string name;
        private GameObject root;
        private bool initialized, disposing;
        public bool IsDisposed { get; private set; }
        public DeviceMetadata Metadata => metadata;
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
                DeviceLuaRuntime.Register(this);
            }
            catch (Exception e) { Dispose(); throw new DeviceLuaException("Lua initialization failed: " + name, e); }
        }
        public void BindRoot(GameObject value)
        { root = value; if (!IsDisposed) environment.Set("self", value == null ? null : new DeviceLuaObject(value, metadata)); }
        internal void UnbindRoot(GameObject value) { if (ReferenceEquals(root, value)) BindRoot(null); }
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
                functions.Clear(); environment?.Dispose(); environment = null; root = null;
                DeviceLuaRuntime.Unregister(this);
            }
        }
    }
}
