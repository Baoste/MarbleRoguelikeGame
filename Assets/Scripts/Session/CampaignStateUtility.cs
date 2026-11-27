using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    internal static class CampaignStateUtility
    {
        internal const long MaxCoins = 9000000000000000L;

        internal static DeviceDefinition Device(SimulationContext context, uint id)
        {
            foreach (var definition in context.Balance.Devices)
                if (definition.Id == id) return definition;
            return null;
        }



        internal static bool FindDevice(SimulationContext context, int instanceId, out Entity entity)
        {
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                    if (context.Manager.GetComponentData<OwnedDeviceData>(entities[i]).InstanceId == instanceId)
                    { entity = entities[i]; return true; }
            entity = Entity.Null;
            return false;
        }

        internal static Entity CreateDevice(SimulationContext context, DeviceDefinition definition, long price,
            OwnedDeviceData firstPlacement = default)
        {
            int quantity = definition.PurchaseQuantity;
            int pair = quantity > 1 ? context.Campaign.NextDeviceId + 1 : 0;
            var first = Entity.Null;
            for (int i = 0; i < quantity; i++)
            {
                var device = i == 0 ? firstPlacement : default;
                device.PurchasePrice = price / quantity + (i < price % quantity ? 1 : 0);
                device.PairId = pair;
                var entity = CreateSingleDevice(context, definition, device);
                if (i == 0) first = entity;
            }
            return first;
        }

        internal static long SalePrice(SimulationContext context, OwnedDeviceData device)
        {
            long paidPrice = device.PurchasePrice;
            if (device.PairId != 0)
            {
                paidPrice = 0;
                using var devices = context.DeviceQuery.ToComponentDataArray<OwnedDeviceData>(Allocator.Temp);
                foreach (var member in devices)
                    if (member.PairId == device.PairId) paidPrice += member.PurchasePrice;
            }
            return MarbleRules.RoundScore(paidPrice * context.Balance.Campaign.SellRatio, MaxCoins);
        }

        internal static void DestroyDevice(SimulationContext context, Entity entity)
        {
            var manager = context.Manager;
            if (!manager.Exists(entity)) return;
            var device = manager.GetComponentData<OwnedDeviceData>(entity);
            using var removed = new NativeList<int>(2, Allocator.Temp);
            if (device.PairId == 0)
            {
                removed.Add(device.InstanceId);
                manager.DestroyEntity(entity);
            }
            else
            {
                using var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp);
                foreach (var member in entities)
                {
                    var data = manager.GetComponentData<OwnedDeviceData>(member);
                    if (data.PairId != device.PairId) continue;
                    removed.Add(data.InstanceId);
                    manager.DestroyEntity(member);
                }
                removed.AsArray().Sort();
            }

            var campaign = context.Campaign;
            campaign.LayoutRevision++;
            context.Campaign = campaign;
            DeviceDurabilitySystem.Refresh(context);
            // The whole pair is gone before its Lua teardown callbacks can observe the inventory.
            foreach (int id in removed)
            {
                context.Transients.RemoveSource(context, id);
                context.DeviceScripts.Remove(id);
            }
        }

        internal static int CountPlacedDevices(SimulationContext context, int excludedInstanceId)
        {
            if (excludedInstanceId < 0) throw new System.ArgumentOutOfRangeException(nameof(excludedInstanceId));
            int count = 0;
            using (var devices = context.DeviceQuery.ToComponentDataArray<OwnedDeviceData>(Allocator.Temp))
                foreach (var device in devices)
                    if (device.Placed && device.InstanceId != excludedInstanceId) count++;
            return count;
        }

        internal static int CountPlacedDevicesOfDefinition(SimulationContext context, uint definitionId)
        {
            if (definitionId == 0) throw new System.ArgumentOutOfRangeException(nameof(definitionId));
            int count = 0;
            using (var devices = context.DeviceQuery.ToComponentDataArray<OwnedDeviceData>(Allocator.Temp))
                foreach (var device in devices)
                    if (device.Placed && device.DefinitionId == definitionId) count++;
            return count;
        }

        internal static Entity CreateSingleDevice(SimulationContext context, DeviceDefinition definition, OwnedDeviceData device)
        {
            var campaign = context.Campaign;
            device.InstanceId = checked(campaign.NextDeviceId + 1);
            device.DefinitionId = definition.Id;
            var entity = CreateDeviceEntity(context, device, new StableIdentity { StableId = ++context.NextStableId });
            campaign.NextDeviceId = device.InstanceId;
            campaign.LayoutRevision++;
            context.Campaign = campaign;
            try
            {
                if (AttributeRuntime.Enabled(context))
                {
                    AttributeRuntime.Initialize(context, entity);
                    AttributeRuntime.SetBase(context, entity, GameAttribute.DEVICE_COOLDOWN, definition.Cooldown);
                    AttributeRuntime.SetBase(context, entity, GameAttribute.DEVICE_TRIGGER_RATE, 1);
                    AttributeRuntime.SetBase(context, entity, GameAttribute.MACHINE_MULT, 1);
                }
                DeviceScriptSystem.InstanceEvent(context, device.InstanceId, "OnCreated");
                return entity;
            }
            catch
            {
                DestroyDevice(context, entity);
                throw;
            }
        }

        internal static Entity CreateDeviceEntity(SimulationContext context, OwnedDeviceData device, StableIdentity identity)
        {
            var manager = context.Manager;
            var entity = manager.CreateEntity(typeof(OwnedDeviceData), typeof(StableIdentity),
                typeof(Owner), typeof(OnBoard), typeof(DefinitionRef));
            manager.SetComponentData(entity, device);
            manager.SetComponentData(entity, identity);
            manager.SetComponentData(entity, new Owner { Player = context.PlayerEntity });
            manager.SetComponentData(entity, new OnBoard { Board = context.BoardEntity });
            manager.SetComponentData(entity, new DefinitionRef { DefinitionId = device.DefinitionId });
            return entity;
        }

        internal static bool CanCashOut(SimulationContext context)
        {
            if (context.Balance == null || context.Round.Phase != (byte)RoundPhase.Playing || context.Player.Blood <= 0)
                return false;
            return context.Round.Score >= context.Round.TargetScore;
        }
    }
}
