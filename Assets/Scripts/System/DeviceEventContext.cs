using System;
using Unity.Entities;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Device-only callback: no ball is required. Valid only during OnTick / OnActivate.</summary>
    [Preserve]
    public sealed class DeviceEventContext
    {
        private SimulationContext c;
        private readonly int instanceId;
        private readonly DeviceDefinition definition;
        internal DeviceEventContext(SimulationContext context, Entity device)
        {
            c = context;
            var owned = c.Manager.GetComponentData<OwnedDeviceData>(device);
            instanceId = owned.InstanceId;
            definition = CampaignStateUtility.Device(c, owned.DefinitionId);
        }
        private SimulationContext C => c ?? throw new InvalidOperationException("The Lua device callback has ended.");
        internal void Close() { c = null; }
        public int InstanceId { get { _ = C; return instanceId; } }
        public int RemainingDurability => CampaignStateUtility.FindDevice(C, instanceId, out var entity)
            ? DeviceDurabilitySystem.Remaining(C, C.Manager.GetComponentData<OwnedDeviceData>(entity)) : 0;
        public int RoundId => C.Round.RoundId;
        public double Time => C.Round.Time;
        public float DeltaTime => C.StepSeconds;
        public double Parameter { get { _ = C; return definition.Value; } }
        public double ScoreMultiplier { get { _ = C; return definition.ScoreMultiplier; } }
        public float Strength { get { _ = C; return definition.Strength; } }
        public float Range { get { _ = C; return definition.Range; } }
        public void SetStatus(string text) => C.DeviceScripts.SetStatus(instanceId, RoundId, text);
    }
}
