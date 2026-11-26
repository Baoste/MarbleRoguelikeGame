using System;
using Unity.Collections;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Restart the entire run, including wallet, score, inventory and placements.</summary>
        public void StartSession(IMarblePhysics physics) => StartSession(physics, 0);

        /// <summary>Initialize the one shared stream, then draw starters. Null uses Marble.RandomSeed.</summary>
        public void StartSession(IMarblePhysics physics, int randomStarterTypeCount, uint? randomSeed = null) =>
            StartSession(physics, randomStarterTypeCount, randomSeed, grantAllStartingDevices: false);

        internal void StartSession(IMarblePhysics physics, bool grantAllStartingDevices, uint? randomSeed = null) =>
            StartSession(physics, 0, randomSeed, grantAllStartingDevices);

        private void StartSession(IMarblePhysics physics, int randomStarterTypeCount, uint? randomSeed, bool grantAllStartingDevices)
        {
            if (dayFlow != null || startingDay) throw new InvalidOperationException("The Day campaign owns this production session.");
            StartSessionCore(physics, randomStarterTypeCount, randomSeed, grantAllStartingDevices);
        }

        private void StartSessionCore(IMarblePhysics physics, int randomStarterTypeCount, uint? randomSeed, bool grantAllStartingDevices,
            ProductionFeature features = ProductionFeature.All, bool usesDailyLoadout = false, long startingCompanyScrip = 0)
        {
            ThrowIfDisposed();
            if (context.Balance == null) throw new InvalidOperationException("Construct with GameBalance to start a campaign.");
            if (randomStarterTypeCount < 0 || randomStarterTypeCount > 200)
                throw new ArgumentOutOfRangeException(nameof(randomStarterTypeCount));
            ResetRound(physics, randomSeed: randomSeed);
            context.DeviceScripts.Clear();
            if (AttributeRuntime.Enabled(context))
            {
                context.Manager.GetBuffer<AttributeModifierData>(context.PlayerEntity).Clear();
                AttributeRuntime.Initialize(context, context.PlayerEntity);
            }
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                context.Manager.DestroyEntity(entities);
            var tuning = context.Balance.Campaign;
            long startingCoins = grantAllStartingDevices ? tuning.AllDevicesStartingCoins : tuning.StartingCoins;
            context.Campaign = new CampaignData
            {
                Coins = usesDailyLoadout ? 0 : startingCoins,
                UsesDailyLoadout = usesDailyLoadout,
                RestockStartingDevices = grantAllStartingDevices && !usesDailyLoadout,
                CompanyScripBalance = startingCompanyScrip,
                RepeatRounds = true,
                DisabledFeatures = ProductionFeature.All & ~features
            };
            context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity).Clear();
            randomStartingDeviceIds = Array.Empty<uint>();
            if (!usesDailyLoadout && context.HasProductionFeature(ProductionFeature.DevicePlacement))
                CreateStartingDevices(randomStarterTypeCount, grantAllStartingDevices);
            var round = context.Round;
            round.Phase = (byte)RoundPhase.Build;
            context.Round = round;
            FillBlood();
        }

        public bool BeginRound()
        {
            if (!CanCampaignAct(RoundPhase.Build)) return false;
            var before = context.Round;
            long scrap = context.Player.BiomassScrapBalance;
            ResetRound(context.Physics, resetRandom: false);
            CampaignWalletSystem.Set(context, GameCurrency.BiomassScrap, scrap);
            var round = context.Round;
            round.TargetScore = before.TargetScore;
            context.Round = round;
            FillBlood();
            var campaign = context.Campaign;
            campaign.LastCashoutCoins = 0;
            context.Campaign = campaign;
            if (context.HasProductionFeature(ProductionFeature.DevicePlacement)) DeviceScriptSystem.RoundStart(context);
            dayFlow?.ObserveRound(before, context.Round, stepped: false);
            return true;
        }

        public bool CashOut()
        {
            if (!CanCampaignAct(RoundPhase.Playing) || !CampaignStateUtility.CanCashOut(context)) return false;
            var before = context.Round;
            var campaign = context.Campaign;
            if (!campaign.UsesDailyLoadout)
            {
                double dividend = AttributeRuntime.Enabled(context)
                    ? AttributeRuntime.Global(context, GameAttribute.BLOOD_DIVIDEND_RATE) * AttributeRuntime.Global(context, GameAttribute.COIN_GAIN_RATE)
                    : context.Balance.Campaign.CashoutCoinsPerBlood;
                campaign.LastCashoutCoins = MarbleRules.RoundScore(context.Player.Blood * dividend, CampaignStateUtility.MaxCoins);
                campaign.Coins = MarbleRules.AddScore(campaign.Coins, campaign.LastCashoutCoins, CampaignStateUtility.MaxCoins);
                context.Campaign = campaign;
            }
            var player = context.Player;
            player.Blood = 0;
            context.Player = player;
            context.BeginDraining();
            var cashoutRound = context.Round; cashoutRound.CashoutRequested = true; context.Round = cashoutRound;
            if (!AttributeRuntime.Enabled(context)) context.RemoveAllMarbles();
            RoundFlowSystem.Execute(context);
            dayFlow?.ObserveRound(before, context.Round, stepped: false, cashout: true);
            return true;
        }

        private bool CanCampaignAct(RoundPhase phase)
        {
            ThrowIfDisposed();
            return started && !insidePhysicsStep && context.Balance != null && ProductionCommandsAvailable &&
                context.Round.Phase == (byte)phase;
        }

        private void FillBlood()
        {
            var player = context.Player;
            player.Blood = context.BloodCapacity;
            context.Player = player;
        }
    }
}
