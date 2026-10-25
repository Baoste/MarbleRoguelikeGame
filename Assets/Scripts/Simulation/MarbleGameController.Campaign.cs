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

        private bool IsSimulationRunning => Snapshot.Phase == RoundPhase.Playing ||
            Snapshot.Phase == RoundPhase.Draining;

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

        public bool EnterBuild()
        {
            if (simulation == null || !simulation.EnterBuild()) return false;
            // Existing board devices still need a Build phase for moving and selling.
            return simulation.GetOwnedDevices().Length > 0 || BeginRound();
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
        public bool BuyOffer(int index) => simulation != null && simulation.BuyOffer(index);
        public bool RefreshShop() => simulation != null && simulation.RefreshShop();
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
        {
            if (!IsReady || Paused || Session.Phase != RoundPhase.Build || !SimulationContext.IsFinite(worldPosition) ||
                !MarbleRules.IsFinite(radiusScale) || radiusScale <= 0 ||
                !MarbleRules.IsFinite(PlacementClearance) || PlacementClearance < 0) return false;
            foreach (var device in simulation.GetOwnedDevices())
            {
                if (device.InstanceId != id) continue;
                float radius = (device.Radius + PlacementClearance) * radiusScale;
                var rotation = worldRotation ?? ((PhysicsRoot ? PhysicsRoot.rotation : Quaternion.identity) * Quaternion.Euler(0, angle ?? device.Angle, 0));
                if (!SimulationContext.IsFinite(rotation)) return false;
                Vector3 center = worldPosition + rotation * (new Vector3(device.PlacementOffsetX, device.PlacementOffsetY, device.PlacementOffsetZ) * radiusScale);
                if (!SimulationContext.IsFinite(center) || device.Radius <= 0) return false;
                if (PlacementSurface != null && !PlacementSurface.ContainsWorldPoint(center, radius)) return false;
                if (!MarbleRules.IsFinite(radius) ||
                    !placementObstacles.IsClear(localPhysics, PlacementObstacleRoots, center, radius)) return false;
                return simulation.PlaceDevice(id, x, z, PlacementClearance, angle);
            }
            return false;
        }
        public bool RemoveDevicePlacement(int id) => simulation != null && simulation.RemoveDevicePlacement(id);
        public bool RotateDevice(int id, float degrees) => simulation != null && simulation.RotateDevice(id, degrees);
        public ShotQuote QuoteShot(float flow) => simulation != null ? simulation.QuoteShot(flow) : default;
        public long ShopRefreshPrice => IsReady ? CampaignShopSystem.Price(simulation.Context, Balance.Campaign.ShopRefreshCost) : 0;
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
