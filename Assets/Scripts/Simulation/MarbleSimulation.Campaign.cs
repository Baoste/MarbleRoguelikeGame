using System;
using Unity.Collections;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Restart the entire run, including wallet, score, inventory and placements.</summary>
        public void StartSession(IMarblePhysics physics) => StartSession(physics, 0, 0);

        /// <summary>Draw starters once per run. A zero shop seed creates a fresh shop sequence, independently of gameplay RNG.</summary>
        public void StartSession(IMarblePhysics physics, int randomStarterTypeCount, uint randomStarterSeed, uint shopSeed = 0)
        {
            ThrowIfDisposed();
            if (context.Balance == null) throw new InvalidOperationException("Construct with GameBalance to start a campaign.");
            if (randomStarterTypeCount < 0 || randomStarterTypeCount > 200)
                throw new ArgumentOutOfRangeException(nameof(randomStarterTypeCount));
            ResetRound(physics);
            context.DeviceScripts.Clear();
            if (AttributeRuntime.Enabled(context))
            {
                context.Manager.GetBuffer<AttributeModifierData>(context.PlayerEntity).Clear();
                AttributeRuntime.Initialize(context, context.PlayerEntity);
            }
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                context.Manager.DestroyEntity(entities);
            context.Campaign = new CampaignData
            {
                Coins = context.Balance.Campaign.StartingCoins,
                RandomState = context.Tuning.RandomSeed,
                ShopRandomState = shopSeed == 0 ? CreateShopSeed() : shopSeed
            };
            context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity).Clear();
            var inventory = context.Manager.GetBuffer<DrugInventoryData>(context.PlayerEntity);
            inventory.Clear();
            foreach (var drug in context.Balance.Drugs)
                inventory.Add(new DrugInventoryData { DefinitionId = drug.Id, Count = drug.StartingCount });
            CreateStartingDevices(randomStarterTypeCount, randomStarterSeed);
            var round = context.Round;
            round.Phase = (byte)RoundPhase.Build;
            round.TargetScore = context.Balance.Stages[0].TargetScore;
            context.Round = round;
            FillBlood();
        }

        private static uint CreateShopSeed()
        {
            // Fresh even on quick restarts or when Unity domain reload is disabled.
            uint seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
            return seed == 0 ? 0x6D2B79F5u : seed;
        }

        public bool BeginRound()
        {
            if (!CanCampaignAct(RoundPhase.Build)) return false;
            ResetRound(context.Physics);
            var round = context.Round;
            round.TargetScore = context.Balance.Stages[context.Campaign.StageIndex].TargetScore;
            round.RandomState = context.Campaign.RandomState;
            context.Round = round;
            FillBlood();
            var campaign = context.Campaign;
            campaign.LastCashoutCoins = 0;
            context.Campaign = campaign;
            DeviceScriptSystem.RoundStart(context);
            return true;
        }

        public bool EnterBuild()
        {
            if (!CanCampaignAct(RoundPhase.Shop)) return false;
            var round = context.Round;
            round.Phase = (byte)RoundPhase.Build;
            round.TargetScore = context.Balance.Stages[context.Campaign.StageIndex].TargetScore;
            context.Round = round;
            return true;
        }

        public bool CashOut()
        {
            if (!CanCampaignAct(RoundPhase.Playing) || !CampaignStateUtility.CanCashOut(context)) return false;
            var campaign = context.Campaign;
            var stage = context.Balance.Stages[campaign.StageIndex];
            double dividend = AttributeRuntime.Enabled(context)
                ? AttributeRuntime.Global(context, GameAttribute.BLOOD_DIVIDEND_RATE) * AttributeRuntime.Global(context, GameAttribute.COIN_GAIN_RATE)
                : context.Balance.Campaign.CashoutCoinsPerBlood;
            campaign.LastCashoutCoins = MarbleRules.RoundScore(context.Player.Blood * stage.CashoutRate * dividend,
                CampaignStateUtility.MaxCoins);
            campaign.Coins = MarbleRules.AddScore(campaign.Coins, campaign.LastCashoutCoins, CampaignStateUtility.MaxCoins);
            context.Campaign = campaign;
            var player = context.Player;
            player.Blood = 0;
            context.Player = player;
            context.BeginDraining();
            var cashoutRound = context.Round; cashoutRound.CashoutRequested = true; context.Round = cashoutRound;
            if (!AttributeRuntime.Enabled(context)) context.RemoveAllMarbles();
            RoundFlowSystem.Execute(context);
            return true;
        }

        public bool CompleteRandomScoreReveal()
        {
            ThrowIfDisposed();
            if (context.Balance == null || insidePhysicsStep ||
                context.Round.Phase != (byte)RoundPhase.Draining) return false;
            return CampaignSettlementSystem.CompleteReveal(context);
        }

        private bool CanCampaignAct(RoundPhase phase)
        {
            ThrowIfDisposed();
            return started && !insidePhysicsStep && context.Balance != null && context.Round.Phase == (byte)phase;
        }

        private void FillBlood()
        {
            var player = context.Player;
            player.Blood = context.Tuning.BloodCapacity;
            context.Player = player;
        }
    }
}
