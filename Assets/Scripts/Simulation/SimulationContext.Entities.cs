using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    internal sealed partial class SimulationContext
    {
        private readonly Dictionary<int, Entity> deviceIndex = new Dictionary<int, Entity>();
        private readonly IterationBuffer<KeyValuePair<int, Entity>> orderedDevices = new IterationBuffer<KeyValuePair<int, Entity>>();
        private readonly IterationBuffer<Entity> deviceEntities = new IterationBuffer<Entity>();
        private readonly IterationBuffer<Entity> marbleEntities = new IterationBuffer<Entity>();
        private readonly IterationBuffer<MarbleKey> orderedMarbleKeys = new IterationBuffer<MarbleKey>();
        internal readonly IterationBuffer<MarbleKey> BallDispatch = new IterationBuffer<MarbleKey>();
        internal readonly IterationBuffer<int> DeviceRemoval = new IterationBuffer<int>();
        internal readonly IterationBuffer<AttributeEffect> AttributeEffects = new IterationBuffer<AttributeEffect>();
        private readonly Dictionary<MarbleKey, Entity> marbles = new Dictionary<MarbleKey, Entity>();
        internal IReadOnlyDictionary<MarbleKey, Entity> Marbles => marbles;
        internal Dictionary<MarbleKey, Entity>.KeyCollection MarbleKeys => marbles.Keys;
        internal Dictionary<MarbleKey, Entity>.ValueCollection MarbleEntities => marbles.Values;
        private readonly EntityQueryMask deviceQueryMask;
        private int deviceIndexVersion, marbleMembershipVersion;

        internal bool TryGetDevice(int instanceId, out Entity entity, out OwnedDeviceData device)
        {
            if (World.IsCreated && deviceIndex.TryGetValue(instanceId, out entity))
            {
                if (IsDeviceEntity(entity))
                {
                    device = Manager.GetComponentData<OwnedDeviceData>(entity);
                    if (device.InstanceId == instanceId) return true;
                }
                else if (HasDeviceIdentity(entity, instanceId, out _))
                {
                    entity = Entity.Null;
                    device = default;
                    return false; // Disabled entities retain their IDs.
                }
                ForgetDevice(instanceId);
                DeviceScripts.Remove(instanceId);
            }
            entity = Entity.Null;
            device = default;
            return false;
        }

        internal bool IsDeviceEntity(Entity entity) => World.IsCreated && deviceQueryMask.MatchesIgnoreFilter(entity);

        internal bool HasDeviceIdentity(Entity entity, int instanceId, out OwnedDeviceData device)
        {
            var manager = Manager;
            if (manager.HasComponent<OwnedDeviceData>(entity))
            {
                device = manager.GetComponentData<OwnedDeviceData>(entity);
                return device.InstanceId == instanceId;
            }
            device = default;
            return false;
        }

        internal void CheckDeviceIdAvailable(int instanceId)
        {
            if (instanceId <= 0 || instanceId > MarbleContact.MaxDeviceInstanceId)
                throw new ArgumentOutOfRangeException(nameof(instanceId));
            if (!deviceIndex.TryGetValue(instanceId, out var entity)) return;
            if (HasDeviceIdentity(entity, instanceId, out _))
                throw new InvalidOperationException($"Device instance {instanceId} already exists.");
            ForgetDevice(instanceId);
            DeviceScripts.Remove(instanceId);
        }

        internal void RegisterDevice(int instanceId, Entity entity)
        {
            deviceIndex.Add(instanceId, entity);
            DeviceMembershipChanged();
        }

        internal void DestroyDeviceEntity(int instanceId, Entity entity)
        {
            Manager.DestroyEntity(entity);
            if (deviceIndex.TryGetValue(instanceId, out var registered) && registered == entity)
                ForgetDevice(instanceId);
        }

        private void ForgetDevice(int instanceId)
        {
            deviceIndex.Remove(instanceId);
            DeviceMembershipChanged();
        }

        private void DeviceMembershipChanged()
        {
            deviceIndexVersion++;
            DeviceScripts.InvalidateDispatch();
        }

        internal bool DestroyAllDeviceEntities()
        {
            bool any = !DeviceQuery.IsEmptyIgnoreFilter;
            if (any) Manager.DestroyEntity(DeviceQuery);
            // A disabled entity still owns its ID until inventory teardown.
            foreach (var pair in deviceIndex)
            {
                if (!HasDeviceIdentity(pair.Value, pair.Key, out _)) continue;
                Manager.DestroyEntity(pair.Value);
                any = true;
            }
            ClearDeviceIndex();
            return any;
        }

        internal void ClearDeviceIndex()
        {
            deviceIndex.Clear();
            orderedDevices.Invalidate();
            deviceEntities.Invalidate();
            DeviceMembershipChanged();
        }

        internal IterationBuffer<KeyValuePair<int, Entity>>.Lease OrderedDevices() =>
            orderedDevices.Read(deviceIndexVersion, deviceIndex, static (index, items) =>
            {
                foreach (var pair in index) items.Add(pair);
                if (items.Count > 1) items.Sort((a, b) => a.Key.CompareTo(b.Key));
            });

        // Retain ECS query order where effects already depend on it. Archetype moves can change
        // that order even when no entity is created or destroyed.
        internal IterationBuffer<Entity>.Lease SnapshotDevices() =>
            deviceEntities.Read(Manager.GetComponentOrderVersion<OwnedDeviceData>(), DeviceQuery,
                static (query, items) => CopyQuery(query, items));

        internal IterationBuffer<Entity>.Lease SnapshotMarbles() =>
            marbleEntities.Read(Manager.GetComponentOrderVersion<MarbleTag>(), MarbleQuery,
                static (query, items) => CopyQuery(query, items));

        private static void CopyQuery(EntityQuery query, List<Entity> items)
        {
            if (query.IsEmptyIgnoreFilter) return;
            using var entities = query.ToEntityArray(Allocator.Temp);
            if (items.Capacity < entities.Length) items.Capacity = entities.Length;
            foreach (var entity in entities) items.Add(entity);
        }

        internal void RegisterMarble(MarbleKey key, Entity entity)
        {
            marbles.Add(key, entity);
            marbleMembershipVersion++;
        }

        internal void UnregisterMarble(MarbleKey key, Entity entity)
        {
            if (!marbles.TryGetValue(key, out var registered) || registered != entity) return;
            marbles.Remove(key);
            marbleMembershipVersion++;
        }

        internal void ClearMarbleIndex()
        {
            marbles.Clear();
            orderedMarbleKeys.Invalidate();
            marbleEntities.Invalidate();
            marbleMembershipVersion++;
        }

        internal IterationBuffer<MarbleKey>.Lease OrderedMarbleKeys() =>
            orderedMarbleKeys.Read(marbleMembershipVersion, marbles, static (index, items) =>
            {
                items.AddRange(index.Keys);
                if (items.Count > 1) items.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            });
    }
}
