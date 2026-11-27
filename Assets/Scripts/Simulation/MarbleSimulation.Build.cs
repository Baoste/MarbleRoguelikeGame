using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        private bool CanEditDevices => CanCampaignAct(RoundPhase.Build) &&
            context.HasProductionFeature(ProductionFeature.DevicePlacement);
        public bool CanInstallDevices => started && !insidePhysicsStep && ProductionCommandsAvailable &&
            context.Balance != null &&
            context.HasProductionFeature(ProductionFeature.DevicePlacement) &&
            (context.Round.Phase == (byte)RoundPhase.Build ||
             (context.Campaign.Production.InstallDuringProduction && context.Round.Phase == (byte)RoundPhase.Playing));

        internal DeviceDefinition InstallableDefinition(uint id)
        {
            return CanInstallDevices && context.Campaign.Production.Supply == DeviceSupply.SelectedTypes &&
                DailyLoadoutSystem.IsSelected(context, id)
                ? CampaignStateUtility.Device(context, id) : null;
        }

        internal bool CanSelectInstallation(uint id) => TryPlanInstallation(id, out _, out _, out _);

        internal bool CanInstallDevice(uint id, float x, float z, float clearance, float angle)
        {
            return TryPlanInstallation(id, out var definition, out _, out _) &&
                DevicePlacementSystem.CanPlaceDefinition(context, definition, x, z, clearance, angle);
        }

        private bool TryPlanInstallation(uint id, out DeviceDefinition definition, out Entity reserved, out long price)
        {
            definition = InstallableDefinition(id);
            reserved = Entity.Null;
            price = 0;
            if (definition == null) return false;
            reserved = UnplacedInstallation(definition);
            if (reserved != Entity.Null) return true;
            price = definition.InstallPrice;
            return context.Campaign.NextDeviceId <= int.MaxValue - 100000 - definition.PurchaseQuantity &&
                context.DeviceQuery.CalculateEntityCount() + definition.PurchaseQuantity <= context.Balance.Campaign.MaxOwnedDevices &&
                CampaignWalletSystem.CanSpend(context, GameCurrency.BiomassScrap, price);
        }

        private Entity UnplacedInstallation(DeviceDefinition definition)
        {
            if (definition.PurchaseQuantity == 1 && !context.Campaign.Production.AllowDeviceReturn) return Entity.Null;
            using var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
                if (device.DefinitionId == definition.Id && !device.Placed) return entity;
            }
            return Entity.Null;
        }

        internal bool TryInstallDevice(uint id, float x, float z, float clearance, float angle, out int instanceId)
        {
            instanceId = 0;
            if (!TryPlanInstallation(id, out var definition, out var entity, out long price) ||
                !DevicePlacementSystem.CanPlaceDefinition(context, definition, x, z, clearance, angle)) return false;
            bool createPack = entity == Entity.Null;
            var before = createPack ? default : context.Manager.GetComponentData<OwnedDeviceData>(entity);
            var placed = createPack ? DeviceDurabilitySystem.WithLimit(before, definition.Durability) : before;
            placed = DeviceDurabilitySystem.Restored(context, placed);
            placed.Placed = true;
            placed.X = x;
            placed.Z = z;
            placed.Angle = definition.ResolvePlacementAngle(angle % 360);
            int installed = createPack ? checked(context.Campaign.NextDeviceId + 1) : before.InstanceId;
            int previousRevision = context.Campaign.LayoutRevision;
            ulong previousStableId = context.NextStableId;
            bool committed = false;
            try
            {
                if (createPack)
                {
                    if (!DevicePlacementSystem.TrySetPhysicsPlacement(context, installed, definition, placed)) return false;
                    entity = CampaignStateUtility.CreateDevice(context, definition, price, placed);
                    DeviceDurabilitySystem.Refresh(context);
                }
                else if (!DevicePlacementSystem.TryCommitPlacement(context, entity, placed)) return false;
                if (!context.Manager.Exists(entity)) return false;
                if (context.IsRunning) DeviceScriptSystem.InstanceEvent(context, installed, "OnRoundStart");
                if (!context.Manager.Exists(entity) ||
                    !CampaignWalletSystem.TrySpend(context, GameCurrency.BiomassScrap, price)) return false;
                instanceId = installed;
                committed = true;
                return true;
            }
            finally
            {
                if (!committed && createPack)
                {
                    if (entity != Entity.Null) CampaignStateUtility.DestroyDevice(context, entity);
                    DevicePlacementSystem.RemovePhysicsPlacement(context, installed);
                    context.DeviceScripts.Remove(installed);
                    var campaign = context.Campaign;
                    campaign.NextDeviceId = installed - 1;
                    campaign.LayoutRevision = previousRevision;
                    context.Campaign = campaign;
                    context.NextStableId = previousStableId;
                }
                else if (!committed && context.Manager.Exists(entity))
                {
                    DevicePlacementSystem.RemovePhysicsPlacement(context, installed);
                    context.Manager.SetComponentData(entity, before);
                    var campaign = context.Campaign;
                    campaign.LayoutRevision = previousRevision;
                    context.Campaign = campaign;
                }
            }
        }

        internal bool CanPlaceDevice(int instanceId, float x, float z, float clearance, float? angle)
        {
            return CanPlaceInstance(instanceId) && DevicePlacementSystem.CanPlace(context, instanceId, x, z, clearance, angle);
        }

        public bool PlaceDevice(int instanceId, float x, float z, float clearance = 0.15f, float? angle = null)
        {
            if (!CanPlaceInstance(instanceId) || !DevicePlacementSystem.Place(context, instanceId, x, z, clearance, angle))
                return false;
            if (context.IsRunning)
                DeviceScriptSystem.InstanceEvent(context, instanceId, "OnRoundStart");
            return true;
        }

        private bool CanPlaceInstance(int instanceId)
        {
            if (!CanInstallDevices || !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            return context.Manager.GetComponentData<OwnedDeviceData>(entity).Placed
                ? CanEditDevices : context.Campaign.Production.Supply == DeviceSupply.OwnedInventory;
        }

        public bool RemoveDevicePlacement(int instanceId) =>
            CanEditDevices && context.Campaign.Production.AllowDeviceReturn && DevicePlacementSystem.ReturnToInventory(context, instanceId);
        public bool RotateDevice(int instanceId, float degrees) => RotateDevice(instanceId, degrees, 0.15f);

        internal bool RotateDevice(int instanceId, float degrees, float clearance) =>
            CanEditDevices && MarbleRules.IsFinite(degrees) &&
            DevicePlacementSystem.Rotate(context, instanceId, degrees, clearance);
    }
}
