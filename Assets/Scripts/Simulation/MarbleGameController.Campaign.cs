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
        /// <summary>Commits a pose in the configured device placement frame.</summary>
        public bool PlaceDevice(int id, float x, float z) => PlaceDevice(id, x, z, null);

        public bool PlaceDevice(int id, float x, float z, float? angle) =>
            CanInstallDevices && simulation.PlaceDevice(id, x, z, PlacementClearance, angle);

        /// <summary>Compatibility overload for callers that also have a world-space preview pose.</summary>
        public bool PlaceDevice(int id, float x, float z, Vector3 worldPosition, float radiusScale = 1f, float? angle = null, Quaternion? worldRotation = null)
            => SimulationContext.IsFinite(worldPosition) && MarbleRules.IsFinite(radiusScale) && radiusScale > 0 &&
                (!worldRotation.HasValue || SimulationContext.IsFinite(worldRotation.Value)) && PlaceDevice(id, x, z, angle);

        internal bool CanPlaceDevice(int id, float x, float z, float angle)
            => CanInstallDevices && simulation.CanPlaceDevice(id, x, z, PlacementClearance, angle);

        internal bool CanInstallDevice(uint id, float x, float z, float angle)
            => CanInstallDevices && simulation.CanInstallDevice(id, x, z, PlacementClearance, angle);

        internal DeviceDefinition InstallableDefinition(uint id) => CanInstallDevices ? simulation.InstallableDefinition(id) : null;

        internal bool CanSelectInstallation(uint id) => CanInstallDevices && simulation.CanSelectInstallation(id);

        internal bool TryInstallDevice(uint id, float x, float z, float angle, out int instanceId)
        {
            instanceId = 0;
            return CanInstallDevices && simulation.TryInstallDevice(id, x, z, PlacementClearance, angle, out instanceId);
        }

        private void OnDeviceBroken(DeviceBrokenEvent broken)
        {
            try { DeviceBroken?.Invoke(broken); }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        public bool RemoveDevicePlacement(int id) => simulation != null && simulation.RemoveDevicePlacement(id);
        public bool RotateDevice(int id, float degrees) => simulation != null && simulation.RotateDevice(id, degrees, PlacementClearance);
        public ShotQuote QuoteShot(float flow) => simulation != null ? simulation.QuoteShot(flow) : default;
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
