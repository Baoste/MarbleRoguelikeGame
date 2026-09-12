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
            return true;
        }

        public bool EnterBuild() => simulation != null && simulation.EnterBuild();
        public bool CashOut() => simulation != null && !Paused && simulation.CashOut();
        public bool BuyOffer(int index) => simulation != null && simulation.BuyOffer(index);
        public bool RefreshShop() => simulation != null && simulation.RefreshShop();
        public bool SellDevice(int id) => simulation != null && simulation.SellDevice(id);
        public bool PlaceDevice(int id, float x, float z) => simulation != null && simulation.PlaceDevice(id, x, z);
        public bool RemoveDevicePlacement(int id) => simulation != null && simulation.RemoveDevicePlacement(id);
        public bool UseDrug(uint id) => simulation != null && !Paused && simulation.UseDrug(id);
        public OwnedDeviceSnapshot[] GetOwnedDevices() => simulation?.GetOwnedDevices() ?? Array.Empty<OwnedDeviceSnapshot>();
        public ShopOfferSnapshot[] GetShopOffers() => simulation?.GetShopOffers() ?? Array.Empty<ShopOfferSnapshot>();
        public DrugInventorySnapshot[] GetDrugInventory() => simulation?.GetDrugInventory() ?? Array.Empty<DrugInventorySnapshot>();

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
