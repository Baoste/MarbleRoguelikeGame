using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    internal struct DeviceInstanceValue : IBufferElementData
    { internal FixedString128Bytes Key; internal double Value; }

    /// <summary>Named numeric state owned by an inventory instance, retained across rounds and presentation rebuilds.</summary>
    internal static class DeviceInstanceState
    {
        internal static Dictionary<string, double> Read(SimulationContext c, Entity entity)
        {
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            if (c.Manager.HasBuffer<DeviceInstanceValue>(entity))
                foreach (var item in c.Manager.GetBuffer<DeviceInstanceValue>(entity)) values[item.Key.ToString()] = item.Value;
            return values;
        }
        internal static double Get(SimulationContext c, Entity entity, string key)
        {
            DeviceRoundState.ValidateKey(key); var fixedKey = new FixedString128Bytes(key);
            if (c.Manager.HasBuffer<DeviceInstanceValue>(entity))
                foreach (var item in c.Manager.GetBuffer<DeviceInstanceValue>(entity)) if (item.Key.Equals(fixedKey)) return item.Value;
            return 0;
        }
        internal static void Set(SimulationContext c, Entity entity, string key, double value)
        { var values = Read(c, entity); DeviceRoundState.Put(values, key, value); Write(c, entity, values); }
        internal static void Write(SimulationContext c, Entity entity, Dictionary<string, double> values)
        {
            var buffer = c.Manager.HasBuffer<DeviceInstanceValue>(entity)
                ? c.Manager.GetBuffer<DeviceInstanceValue>(entity) : c.Manager.AddBuffer<DeviceInstanceValue>(entity);
            buffer.Clear();
            foreach (var item in values) buffer.Add(new DeviceInstanceValue { Key = new FixedString128Bytes(item.Key), Value = item.Value });
        }
    }
}
