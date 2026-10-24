using System;
using Unity.Collections;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Restart the entire run, including wallet, score, inventory and placements.</summary>
        public void StartSession(IMarblePhysics physics) => StartSession(physics, 0);

        /// <summary>Initialize the one shared stream, then draw starters. Null uses Marble.RandomSeed.</summary>
        public void StartSession(IMarblePhysics physics, int randomStarterTypeCount, uint? randomSeed = null)
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
            context.Campaign = new CampaignData
            {
                Coins = context.Balance.Campaign.StartingCoins
            };
            context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity).Clear();
            CreateStartingDevices(randomStarterTypeCount);
            var round = context.Round;
            round.Phase = (byte)RoundPhase.Build;
            round.TargetScore = context.Balance.Stages[0].TargetScore;
            context.Round = round;
            FillBlood();
        }

        public bool BeginRound()
        {
            if (!CanCampaignAct(RoundPhase.Build)) return false;
            ResetRound(context.Physics, resetRandom: false);
            var round = context.Round;
            round.TargetScore = context.Balance.Stages[context.Campaign.StageIndex].TargetScore;
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
