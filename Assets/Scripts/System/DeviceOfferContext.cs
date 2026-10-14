using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Build-phase inventory offer. Writes and acceptance commit only after Lua completes successfully.</summary>
    [Preserve]
    public sealed class DeviceOfferContext
    {
        private SimulationContext c;
        private readonly int instanceId, offeredInstanceId, roundId;
        private readonly DeviceDefinition definition, offeredDefinition;
        private readonly Dictionary<string, double> values;
        private readonly Dictionary<string, double> instanceValues;
        private OwnedDeviceData device;
        internal bool Accepted { get; private set; }
        private string status;
        internal DeviceOfferContext(SimulationContext context, Entity target, Entity offered)
        {
            c = context;
            var receiver = c.Manager.GetComponentData<OwnedDeviceData>(target);
            var source = c.Manager.GetComponentData<OwnedDeviceData>(offered);
            instanceId = receiver.InstanceId; offeredInstanceId = source.InstanceId;
            device = receiver; instanceValues = DeviceInstanceState.Read(c, target);
            definition = CampaignStateUtility.Device(c, receiver.DefinitionId);
            offeredDefinition = CampaignStateUtility.Device(c, source.DefinitionId);
            roundId = DeviceRoundState.Round(c); values = DeviceRoundState.Read(c, target, roundId);
        }
        private SimulationContext C => c ?? throw new InvalidOperationException("The Lua device callback has ended.");
        public int InstanceId { get { _ = C; return instanceId; } }
        public uint DefinitionId { get { _ = C; return definition.Id; } }
        /// <summary>The upcoming round for which this Build-phase offer is being prepared.</summary>
        public int RoundId { get { _ = C; return roundId; } }
        public int OfferedInstanceId { get { _ = C; return offeredInstanceId; } }
        public uint OfferedDefinitionId { get { _ = C; return offeredDefinition.Id; } }
        public string OfferedName { get { _ = C; return offeredDefinition.Name; } }
        public string OfferedRarity { get { _ = C; return offeredDefinition.Rarity.ToString().ToLowerInvariant(); } }
        public double Parameter { get { _ = C; return definition.Value; } }
        public float Strength { get { _ = C; return definition.Strength; } }
        public double GetRoundValue(string key)
        { _ = C; DeviceRoundState.ValidateKey(key); return values.TryGetValue(key, out var value) ? value : 0; }
        public void SetRoundValue(string key, double value) { _ = C; DeviceRoundState.Put(values, key, value); }
        public double GetInstanceValue(string key)
        { _ = C; DeviceRoundState.ValidateKey(key); return instanceValues.TryGetValue(key, out var value) ? value : 0; }
        public void SetInstanceValue(string key, double value) { _ = C; DeviceRoundState.Put(instanceValues, key, value); }
        public int DurabilityLimit => DeviceDurabilitySystem.Limit(C, device);
        public int RemainingDurability => DeviceDurabilitySystem.Remaining(C, device);
        public void SetDurabilityLimit(int maximum) { _ = C; device = DeviceDurabilitySystem.WithLimit(device, maximum); }
        public void RestoreDurability() => device = DeviceDurabilitySystem.Restored(C, device);
        /// <summary>Accept exactly the offered inventory instance; does not spend durability or award sale coins.</summary>
        public void Accept() { _ = C; Accepted = true; }
        public void SetStatus(string text) { _ = C; status = text ?? string.Empty; }
        internal void Commit(Entity target)
        {
            DeviceRoundState.Write(C, target, roundId, values);
            DeviceInstanceState.Write(C, target, instanceValues);
            C.Manager.SetComponentData(target, device);
            if (status != null) C.DeviceScripts.SetStatus(instanceId, roundId, status);
        }
        internal void Close() { c = null; }
    }
}
