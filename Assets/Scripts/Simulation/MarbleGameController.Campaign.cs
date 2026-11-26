using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        [Header("Configuration")]
        public TextAsset BalanceJson;
        [Header("Campaign")]
        public bool EnableCampaign;
        public GameBalance Balance { get; private set; }
        public SessionSnapshot Session => !IsReady ? default : simulation.Session;
        public DailyLoadoutSnapshot DailyLoadout => IsReady ? simulation.DailyLoadout : default;
        public bool LoadoutConfirmed => IsReady && simulation.LoadoutConfirmed;
        public bool CanInstallDevices => IsReady && !Paused && !RequiresRestart && simulation.CanInstallDevices;
        public event Action<DeviceBrokenEvent> DeviceBroken;
        internal event Action DeviceLayoutChanged;

        private bool IsSimulationRunning => simulation.CanSimulateProduction;

        public void ConfigureCampaign(GameBalance balance)
        {
            if (IsReady) throw new InvalidOperationException("Configure before the controller starts.");
            Balance = balance.Copy();
            Balance.Validate();
            EnableCampaign = true;
        }

        public bool BeginRound()
        {
            if (simulation == null || !simulation.BeginRound()) return false;
            SyncLauncherTransform();
            accumulator = 0;
            externalFireHeld = submittedFireHeld = false;
            Paused = false;
            PublishBloodIfChanged();
            return true;
        }

        private bool HasUnplacedDevices()
        {
            if (simulation == null) return false;
            foreach (OwnedDeviceSnapshot device in simulation.GetOwnedDevices())
                if (!device.Placed) return true;
            return false;
        }

        private void BeginRoundWhenInventoryIsEmpty()
        {
            if (EnableCampaign && simulation != null && !HasUnplacedDevices())
                simulation.BeginRound();
        }
        public bool CashOut()
        {
            if (simulation == null || Paused || !simulation.CashOut()) return false;
            PublishBloodIfChanged();
            return true;
        }
        public bool BuyOffer(int index, uint expectedId = 0) => simulation != null && simulation.BuyOffer(index, expectedId);
        public bool RefreshShop() => simulation != null && simulation.RefreshShop();
        public bool CanRefreshDailyShop => IsReady && simulation.CanRefreshDailyShop;
        public bool ShopOpen => IsReady && simulation.ShopOpen;
        public bool ShopCommandsAvailable => IsReady && simulation.ShopCommandsAvailable;
        public bool TryOpenShop(long version) => IsReady && !RequiresRestart && simulation.TryOpenShop(version);
        public bool TryCloseShop(long version) => IsReady && simulation.TryCloseShop(version);
        public bool TryToggleLoadoutPeg(long version, uint id) => IsReady && simulation.TryToggleLoadoutPeg(version, id);
        public bool TryConfirmLoadout(long version) => IsReady && simulation.TryConfirmLoadout(version);
        public DailyPegSnapshot[] GetDailyPegs() => simulation?.GetDailyPegs() ?? Array.Empty<DailyPegSnapshot>();
        public bool SellDevice(int id) => simulation != null && simulation.SellDevice(id);
        public bool CanOfferDevice(int offeredId, int targetId) => IsReady && !Paused && simulation.CanOfferDevice(offeredId, targetId);
        public bool TryOfferDevice(int offeredId, int targetId) => IsReady && !Paused && simulation.TryOfferDevice(offeredId, targetId);
        /// <summary>For placements expressed directly in PhysicsRoot's local XZ plane.</summary>
        public bool PlaceDevice(int id, float x, float z)
        {
            return PhysicsRoot != null && PlaceDevice(id, x, z,
                PhysicsRoot.TransformPoint(new Vector3(x, 0, z)),
                Mathf.Max(Mathf.Abs(PhysicsRoot.lossyScale.x), Mathf.Abs(PhysicsRoot.lossyScale.z)));
        }

        /// <summary>Presentation supplies the actual world pose when its placement plane is offset or tilted.</summary>
        public bool PlaceDevice(int id, float x, float z, Vector3 worldPosition, float radiusScale = 1f, float? angle = null, Quaternion? worldRotation = null)
            => IsDevicePlacementClear(id, worldPosition, radiusScale, angle, worldRotation) &&
                simulation.PlaceDevice(id, x, z, PlacementClearance, angle, DeviceLayoutChanged);

        internal bool CanPlaceDevice(int id, float x, float z, Vector3 worldPosition, float radiusScale, float angle, Quaternion worldRotation)
            => IsDevicePlacementClear(id, worldPosition, radiusScale, angle, worldRotation) &&
                simulation.CanPlaceDevice(id, x, z, PlacementClearance, angle);

        private bool IsDevicePlacementClear(int id, Vector3 worldPosition, float radiusScale, float? angle, Quaternion? worldRotation)
        {
            if (!CanInstallDevices) return false;
            var context = simulation.Context;
            if (!CampaignStateUtility.FindDevice(context, id, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            var definition = CampaignStateUtility.Device(context, device.DefinitionId);
            return IsPlacementPoseClear(definition, worldPosition, radiusScale, angle ?? device.Angle, worldRotation,
                avoidMarbles: Session.Phase == RoundPhase.Playing);
        }

        internal bool CanInstallDevice(uint id, float x, float z, Vector3 worldPosition, float radiusScale,
            float angle, Quaternion worldRotation)
        {
            if (!CanInstallDevices) return false;
            var definition = simulation.InstallableDefinition(id);
            return definition != null && simulation.CanInstallDevice(id, x, z, PlacementClearance, angle) &&
                IsPlacementPoseClear(definition, worldPosition, radiusScale, angle, worldRotation, avoidMarbles: true);
        }

        internal DeviceDefinition InstallableDefinition(uint id) => CanInstallDevices ? simulation.InstallableDefinition(id) : null;

        internal bool CanSelectInstallation(uint id) => CanInstallDevices && simulation.CanSelectInstallation(id);

        internal bool TryInstallDevice(uint id, float x, float z, Vector3 worldPosition, float radiusScale,
            float angle, Quaternion worldRotation, out int instanceId)
        {
            instanceId = 0;
            if (!CanInstallDevice(id, x, z, worldPosition, radiusScale, angle, worldRotation)) return false;
            return simulation.TryInstallDevice(id, x, z, PlacementClearance, angle, out instanceId, DeviceLayoutChanged);
        }

        private void OnDeviceBroken(DeviceBrokenEvent broken)
        {
            DeviceLayoutChanged?.Invoke();
            DeviceBroken?.Invoke(broken);
        }

        private bool IsPlacementPoseClear(DeviceDefinition definition, Vector3 worldPosition, float radiusScale,
            float angle, Quaternion? worldRotation, bool avoidMarbles = false)
        {
            if (!SimulationContext.IsFinite(worldPosition) || !MarbleRules.IsFinite(radiusScale) || radiusScale <= 0 ||
                !MarbleRules.IsFinite(PlacementClearance) || PlacementClearance < 0 || definition.PlacementRadius <= 0) return false;
            float radius = (definition.PlacementRadius + PlacementClearance) * radiusScale;
            float placementAngle = definition.ResolvePlacementAngle(angle);
            var rotation = worldRotation ?? ((PhysicsRoot ? PhysicsRoot.rotation : Quaternion.identity) * Quaternion.Euler(0, placementAngle, 0));
            if (!MarbleRules.IsFinite(radius) || !SimulationContext.IsFinite(rotation)) return false;
            var offset = new Vector3(definition.PlacementOffsetX, definition.PlacementOffsetY, definition.PlacementOffsetZ);
            Vector3 center = worldPosition + rotation * (offset * radiusScale);
            return SimulationContext.IsFinite(center) &&
                (PlacementSurface == null || PlacementSurface.ContainsWorldPoint(center, radius)) &&
                (!avoidMarbles || bridge.IsClearOfMarbles(center, radius)) &&
                placementObstacles.IsClear(localPhysics, PlacementObstacleRoots, center, radius, bridge?.GeneratedBoardLayout);
        }
        public bool RemoveDevicePlacement(int id) => simulation != null && simulation.RemoveDevicePlacement(id);
        public bool RotateDevice(int id, float degrees) => simulation != null && simulation.RotateDevice(id, degrees);
        public ShotQuote QuoteShot(float flow) => simulation != null ? simulation.QuoteShot(flow) : default;
        public long ShopRefreshPrice => !IsReady ? 0 : CurrentDayDefinition?.DailyLoadout is DailyLoadoutDefinition loadout
            ? loadout.Shop?.RefreshCost ?? 0
            : CampaignShopSystem.Price(simulation.Context, Balance.Campaign.ShopRefreshCost);
        public OwnedDeviceSnapshot[] GetOwnedDevices() => simulation?.GetOwnedDevices() ?? Array.Empty<OwnedDeviceSnapshot>();
        public ShopOfferSnapshot[] GetShopOffers() => simulation?.GetShopOffers() ?? Array.Empty<ShopOfferSnapshot>();

        public void SetLauncherPosition(float x)
        {
            if (IsReady && !Paused && MarbleRules.IsFinite(x)) simulation.RequestLauncherPosition(x);
        }

        private static bool SpaceHeld()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.Space);
#else
            return false;
#endif
        }

        private static float HorizontalInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
#else
            return 0;
#endif
        }
    }
}
