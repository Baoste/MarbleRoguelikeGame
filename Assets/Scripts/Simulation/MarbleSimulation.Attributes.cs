using System;
using Unity.Entities;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public bool ApplyAttribute(MarbleKey key, uint source, GameAttribute attribute, AttributeOperation operation,
            double value, long durationTicks = 0, int priority = 0)
        {
            ThrowIfDisposed();
            if (!AttributeRuntime.Enabled(context) || insidePhysicsStep || !context.TryGetFlying(key, out var entity)) return false;
            return AddAttributeEffect(entity, source, attribute, operation, value, durationTicks, priority);
        }
        public bool ApplyGlobalAttribute(uint source, GameAttribute attribute, AttributeOperation operation,
            double value, long durationTicks = 0, int priority = 0)
        {
            ThrowIfDisposed();
            if (!AttributeRuntime.Enabled(context) || insidePhysicsStep) return false;
            return AddAttributeEffect(context.PlayerEntity, source, attribute, operation, value, durationTicks, priority);
        }
        public double GetGlobalAttribute(GameAttribute attribute) => AttributeRuntime.Global(context, attribute);
        public bool ApplyDeviceAttribute(int instanceId, uint source, GameAttribute attribute, AttributeOperation operation,
            double value, long durationTicks = 0, int priority = 0)
        {
            ThrowIfDisposed();
            if (!AttributeRuntime.Enabled(context) || insidePhysicsStep || !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            return AddAttributeEffect(entity, source, attribute, operation, value, durationTicks, priority);
        }
        public double GetMarbleAttribute(MarbleKey key, GameAttribute attribute) => context.TryGetFlying(key, out var entity)
            ? AttributeRuntime.Get(context, entity, attribute) : throw new ArgumentException("Marble is no longer active.");
        public ShotQuote QuoteShot(float flow) => AttributeRuntime.Enabled(context)
            ? AttributeRuntime.Prepare(context, flow).Quote : MarbleRules.Quote(context.Tuning, flow);
        private bool AddAttributeEffect(Entity entity, uint source, GameAttribute attribute, AttributeOperation operation,
            double value, long durationTicks, int priority)
        {
            var definition = context.Content.Definition(attribute);
            if (source == 0 || durationTicks < 0 || !definition.Allows(operation) || !MarbleRules.IsFinite(value) ||
                (operation == AttributeOperation.Multiply && value < 0)) throw new ArgumentException("Invalid attribute effect.");
            var buffer = context.Manager.GetBuffer<AttributeModifierData>(entity);
            var entry = new AttributeModifierData { Source = source, Effect = new AttributeEffect { Attribute = attribute,
                Operation = operation, Value = value, Priority = priority }, ExpireTick = durationTicks == 0 ? 0 : checked(context.Round.Tick + durationTicks) };
            bool replaced = false;
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].Source == source && buffer[i].Effect.Attribute == attribute)
                { buffer[i] = entry; replaced = true; break; }
            if (!replaced) buffer.Add(entry);
            AttributeRuntime.Recompute(context, entity);
            return true;
        }
    }
}
