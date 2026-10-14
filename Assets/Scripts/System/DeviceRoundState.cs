using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    internal struct DeviceRoundValue : IBufferElementData
    {
        internal FixedString128Bytes Key;
        internal int Round;
        internal double Value;
    }

    /// <summary>Named numeric state for a device's active or upcoming round.</summary>
    internal static class DeviceRoundState
    {
        internal const int MaxKeys = 32;
        internal static int Round(SimulationContext c) => c.Round.Phase == (byte)RoundPhase.Build
            ? checked(c.Round.RoundId + 1) : c.Round.RoundId;
        internal static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) > 120)
                throw new ArgumentException("Round-state keys require 1 to 120 UTF-8 bytes.", nameof(key));
        }
        internal static void ValidateValue(double value)
        {
            if (!MarbleRules.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        }
        internal static Dictionary<string, double> Read(SimulationContext c, Entity entity, int round)
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            if (c.Manager.HasBuffer<DeviceRoundValue>(entity))
                foreach (var item in c.Manager.GetBuffer<DeviceRoundValue>(entity))
                    if (item.Round == round) result[item.Key.ToString()] = item.Value;
            return result;
        }
        internal static double Get(SimulationContext c, Entity entity, string key)
        {
            ValidateKey(key);
            return Get(c, entity, Round(c), new FixedString128Bytes(key));
        }
        private static double Get(SimulationContext c, Entity entity, int round, FixedString128Bytes key)
        {
            if (c.Manager.HasBuffer<DeviceRoundValue>(entity))
                foreach (var item in c.Manager.GetBuffer<DeviceRoundValue>(entity))
                    if (item.Round == round && item.Key.Equals(key)) return item.Value;
            return 0;
        }
        internal static int CountPlaced(SimulationContext c, uint definitionId, string key, double value)
        {
            if (definitionId == 0) throw new ArgumentOutOfRangeException(nameof(definitionId));
            ValidateKey(key); ValidateValue(value);
            int count = 0, round = Round(c); var fixedKey = new FixedString128Bytes(key);
            using (var entities = c.DeviceQuery.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    var device = c.Manager.GetComponentData<OwnedDeviceData>(entity);
                    if (device.Placed && device.DefinitionId == definitionId && Get(c, entity, round, fixedKey) == value) count++;
                }
            return count;
        }
        internal static void Set(SimulationContext c, Entity entity, string key, double value)
        {
            var values = Read(c, entity, Round(c)); Put(values, key, value); Write(c, entity, Round(c), values);
        }
        internal static void Put(Dictionary<string, double> values, string key, double value)
        {
            ValidateKey(key); ValidateValue(value);
            if (!values.ContainsKey(key) && values.Count >= MaxKeys)
                throw new InvalidOperationException("At most 32 round-state values are allowed per device.");
            values[key] = value;
        }
        internal static void Write(SimulationContext c, Entity entity, int round, Dictionary<string, double> values)
        {
            var buffer = c.Manager.HasBuffer<DeviceRoundValue>(entity)
                ? c.Manager.GetBuffer<DeviceRoundValue>(entity) : c.Manager.AddBuffer<DeviceRoundValue>(entity);
            buffer.Clear();
            foreach (var item in values)
                buffer.Add(new DeviceRoundValue { Key = new FixedString128Bytes(item.Key), Round = round, Value = item.Value });
        }
    }
}
