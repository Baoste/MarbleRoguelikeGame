using System;
using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    /// <summary>In-memory checkpoint of a quiescent Build boundary in the current World.</summary>
    internal sealed class DayCheckpoint
    {
        private readonly RoundData round;
        private readonly CampaignData campaign;
        private readonly PlayerData player;
        private readonly LauncherData launcher;
        private readonly LauncherMovementConfig movementConfig;
        private readonly LauncherMovementState movementState;
        private readonly LauncherMovementInput movementInput;
        private readonly ShopOfferData[] offers;
        private readonly AttributeValue[] playerValues;
        private readonly AttributeModifierData[] playerModifiers;
        private readonly Device[] devices;
        private readonly ulong spawnSequence, stableId;
        private readonly float stepSeconds;
        private readonly IMarbleDayPhysicsCheckpoint physics;
        internal DayRuleData[] Rules { get; }
        internal uint RandomState => round.RandomState;

        internal DayCheckpoint(SimulationContext context, DayRuleData[] rules)
        {
            round = context.Round;
            if (round.Phase != (byte)RoundPhase.Build || round.ActiveMarbleCount != 0 || context.Marbles.Count != 0 ||
                !context.MarbleQuery.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("A Day checkpoint requires Build with no active marbles.");

            var manager = context.Manager;
            campaign = context.Campaign;
            player = context.Player;
            launcher = context.Launcher;
            movementConfig = manager.GetComponentData<LauncherMovementConfig>(context.LauncherEntity);
            movementState = manager.GetComponentData<LauncherMovementState>(context.LauncherEntity);
            movementInput = manager.GetComponentData<LauncherMovementInput>(context.LauncherEntity);
            offers = CopyBuffer<ShopOfferData>(manager, context.RoundEntity);
            playerValues = CopyBuffer<AttributeValue>(manager, context.PlayerEntity);
            playerModifiers = CopyBuffer<AttributeModifierData>(manager, context.PlayerEntity);
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
            {
                devices = new Device[entities.Length];
                for (int i = 0; i < entities.Length; i++) devices[i] = new Device(context, entities[i]);
            }
            spawnSequence = context.NextSpawnSequence;
            stableId = context.NextStableId;
            stepSeconds = context.StepSeconds;
            Rules = rules;
            physics = ((IMarbleDayPhysics)context.Physics).CaptureDayCheckpoint() ??
                throw new InvalidOperationException("The physics adapter returned no Day checkpoint.");
        }

        internal void Restore(SimulationContext context, uint randomState)
        {
            var manager = context.Manager;
            // Keep physical/contact identities fresh even when replaying the gameplay random stream.
            int nextRoundId = checked(context.Round.RoundId + 1);
            int nextRevision = checked(context.Campaign.LayoutRevision + 1);
            context.ClearFireInput();
            context.RemoveAllMarbles();
            context.Contacts.Clear();
            context.ScoreEvents.Clear();
            context.FloatingText?.Reset();
            context.CloverFamilies.Clear();
            context.RefundedFamilies.Clear();
            physics.Restore();
            context.DeviceScripts.Clear();
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                manager.DestroyEntity(entities);

            var restored = round;
            restored.RoundId = nextRoundId;
            restored.RandomState = randomState;
            context.Round = restored;
            var session = campaign;
            session.LayoutRevision = nextRevision;
            context.Campaign = session;
            context.Player = player;
            context.Launcher = launcher;
            manager.SetComponentData(context.LauncherEntity, movementConfig);
            manager.SetComponentData(context.LauncherEntity, movementState);
            manager.SetComponentData(context.LauncherEntity, movementInput);
            context.NextSpawnSequence = spawnSequence;
            context.NextStableId = stableId;
            context.StepSeconds = stepSeconds;
            WriteBuffer(manager, context.RoundEntity, offers);
            WriteBuffer(manager, context.PlayerEntity, playerValues);
            WriteBuffer(manager, context.PlayerEntity, playerModifiers);
            foreach (var device in devices)
                device.Restore(context, checked(nextRoundId - round.RoundId));
        }

        private sealed class Device
        {
            private readonly OwnedDeviceData data;
            private readonly StableIdentity identity;
            private readonly AttributeValue[] values;
            private readonly AttributeModifierData[] effects;
            private readonly DeviceInstanceValue[] instanceValues;
            private readonly DeviceRoundValue[] roundValues;
            private readonly DeviceUseKey[] uses;
            private readonly DeviceEffectContinuation? continuation;
            private readonly string status;

            internal Device(SimulationContext context, Entity entity)
            {
                var manager = context.Manager;
                data = manager.GetComponentData<OwnedDeviceData>(entity);
                identity = manager.GetComponentData<StableIdentity>(entity);
                values = CopyBuffer<AttributeValue>(manager, entity);
                effects = CopyBuffer<AttributeModifierData>(manager, entity);
                instanceValues = CopyBuffer<DeviceInstanceValue>(manager, entity);
                roundValues = CopyBuffer<DeviceRoundValue>(manager, entity);
                uses = CopyBuffer<DeviceUseKey>(manager, entity);
                if (manager.HasComponent<DeviceEffectContinuation>(entity))
                    continuation = manager.GetComponentData<DeviceEffectContinuation>(entity);
                status = context.DeviceScripts.Status(data.InstanceId, DeviceRoundState.Round(context));
            }

            internal void Restore(SimulationContext context, int roundDelta)
            {
                var restored = data;
                if (restored.DurabilityRound != 0) restored.DurabilityRound = checked(restored.DurabilityRound + roundDelta);
                var entity = CampaignStateUtility.CreateDeviceEntity(context, restored, identity);
                var manager = context.Manager;
                WriteBuffer(manager, entity, values);
                WriteBuffer(manager, entity, effects);
                WriteBuffer(manager, entity, instanceValues);
                if (roundValues != null)
                {
                    var buffer = manager.AddBuffer<DeviceRoundValue>(entity);
                    foreach (var value in roundValues)
                    {
                        var entry = value;
                        entry.Round = checked(entry.Round + roundDelta);
                        buffer.Add(entry);
                    }
                }
                if (uses != null)
                {
                    var buffer = manager.AddBuffer<DeviceUseKey>(entity);
                    foreach (var value in uses)
                    {
                        var entry = value;
                        entry.Round = checked(entry.Round + roundDelta);
                        buffer.Add(entry);
                    }
                }
                if (continuation.HasValue)
                {
                    var entry = continuation.Value;
                    entry.Round = checked(entry.Round + roundDelta);
                    manager.AddComponentData(entity, entry);
                }
                if (!string.IsNullOrEmpty(status))
                    context.DeviceScripts.SetStatus(data.InstanceId, DeviceRoundState.Round(context), status);
            }
        }

        private static T[] CopyBuffer<T>(EntityManager manager, Entity entity) where T : unmanaged, IBufferElementData =>
            manager.HasBuffer<T>(entity) ? manager.GetBuffer<T>(entity).AsNativeArray().ToArray() : null;

        private static void WriteBuffer<T>(EntityManager manager, Entity entity, T[] source) where T : unmanaged, IBufferElementData
        {
            if (source == null) return;
            var buffer = manager.HasBuffer<T>(entity) ? manager.GetBuffer<T>(entity) : manager.AddBuffer<T>(entity);
            buffer.Clear();
            foreach (var value in source) buffer.Add(value);
        }
    }
}
