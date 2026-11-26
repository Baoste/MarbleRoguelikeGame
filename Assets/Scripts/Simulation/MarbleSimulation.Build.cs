using System;
using Unity.Entities;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        private bool CanEditDevices => CanCampaignAct(RoundPhase.Build) &&
            context.HasProductionFeature(ProductionFeature.DevicePlacement);
        public bool CanInstallDevices => started && !insidePhysicsStep && ProductionCommandsAvailable &&
            context.Campaign.UsesDailyLoadout &&
            context.HasProductionFeature(ProductionFeature.DevicePlacement) &&
            (context.Round.Phase == (byte)RoundPhase.Build || context.Round.Phase == (byte)RoundPhase.Playing);

        internal DeviceDefinition InstallableDefinition(uint id)
        {
            return CanInstallDevices && DailyLoadoutSystem.IsSelected(context, id)
                ? CampaignStateUtility.Device(context, id) : null;
        }

        internal bool CanInstallDevice(uint id, float x, float z, float clearance, float angle)
        {
            var definition = InstallableDefinition(id);
            return definition != null && context.Campaign.NextDeviceId < int.MaxValue - 100000 &&
                context.DeviceQuery.CalculateEntityCount() < context.Balance.Campaign.MaxOwnedDevices &&
                CampaignWalletSystem.CanSpend(context, GameCurrency.BiomassScrap, definition.InstallPrice) &&
                DevicePlacementSystem.CanPlaceDefinition(context, definition, x, z, clearance, angle);
        }

        internal bool TryInstallDevice(uint id, float x, float z, float clearance, float angle, out int instanceId,
            Action projectLayout = null)
        {
            instanceId = 0;
            if (!CanInstallDevice(id, x, z, clearance, angle)) return false;
            var definition = CampaignStateUtility.Device(context, id);
            var placed = DeviceDurabilitySystem.WithLimit(new OwnedDeviceData
            {
                Placed = true, X = x, Z = z, Angle = definition.ResolvePlacementAngle(angle % 360),
                PurchasePrice = definition.InstallPrice, DurabilityRound = context.Round.RoundId
            }, definition.Durability);
            var entity = Entity.Null;
            bool committed = false;
            try
            {
                entity = CampaignStateUtility.CreateSingleDevice(context, definition, placed);
                if (!context.Manager.Exists(entity)) return false;
                projectLayout?.Invoke();
                int installed = context.Manager.GetComponentData<OwnedDeviceData>(entity).InstanceId;
                if (context.IsRunning) DeviceScriptSystem.InstanceEvent(context, installed, "OnRoundStart");
                if (!context.Manager.Exists(entity) ||
                    !CampaignWalletSystem.TrySpend(context, GameCurrency.BiomassScrap, definition.InstallPrice)) return false;
                instanceId = installed;
                committed = true;
                return true;
            }
            finally
            {
                if (!committed && entity != Entity.Null)
                {
                    CampaignStateUtility.DestroyDevice(context, entity);
                    projectLayout?.Invoke();
                }
            }
        }

        internal bool CanPlaceDevice(int instanceId, float x, float z, float clearance, float? angle)
        {
            return CanEditDevices && DevicePlacementSystem.CanPlace(context, instanceId, x, z, clearance, angle);
        }

        /// <summary>Checks device spacing after the scene has accepted the placement plane and obstacle checks.</summary>
        public bool PlaceDevice(int instanceId, float x, float z, float clearance = 0.15f, float? angle = null)
        {
            return CanEditDevices && DevicePlacementSystem.Place(context, instanceId, x, z, clearance, angle);
        }

        public bool RemoveDevicePlacement(int instanceId) =>
            CanEditDevices && !context.Campaign.UsesDailyLoadout && DevicePlacementSystem.ReturnToInventory(context, instanceId);
        public bool RotateDevice(int instanceId, float degrees)
        {
            if (!CanEditDevices || !MarbleRules.IsFinite(degrees) ||
                !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (CampaignStateUtility.Device(context, device.DefinitionId).RotationLocked) return false;
            device.Angle = (device.Angle + degrees) % 360; context.Manager.SetComponentData(entity, device);
            var campaign = context.Campaign; campaign.LayoutRevision++; context.Campaign = campaign; return true;
        }
    }
}
