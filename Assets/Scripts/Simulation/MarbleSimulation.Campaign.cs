using System;
using Unity.Collections;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Restart the entire run, including wallet, score, inventory and placements.</summary>
        public void StartSession(IMarblePhysics physics)
        {
            ThrowIfDisposed();
            if (context.Balance == null) throw new InvalidOperationException("Construct with GameBalance to start a campaign.");
            ResetRound(physics);
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                context.Manager.DestroyEntity(entities);
            context.Campaign = new CampaignData
            {
                Coins = context.Balance.Campaign.StartingCoins,
                RandomState = context.Tuning.RandomSeed
            };
            context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity).Clear();
            var inventory = context.Manager.GetBuffer<DrugInventoryData>(context.PlayerEntity);
            inventory.Clear();
            foreach (var drug in context.Balance.Drugs)
                inventory.Add(new DrugInventoryData { DefinitionId = drug.Id, Count = drug.StartingCount });
            foreach (var device in context.Balance.Devices)
                for (int i = 0; i < device.StartingCount; i++)
                    CampaignStateUtility.CreateDevice(context, device, device.Price);
            var round = context.Round;
            round.Phase = (byte)RoundPhase.Build;
            round.TargetScore = context.Balance.Stages[0].TargetScore;
            context.Round = round;
            FillBlood();
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
            campaign.LastCashoutCoins = MarbleRules.RoundScore(context.Player.Blood *
                context.Balance.Campaign.CashoutCoinsPerBlood * stage.CashoutRate, CampaignStateUtility.MaxCoins);
            campaign.Coins = MarbleRules.AddScore(campaign.Coins, campaign.LastCashoutCoins, CampaignStateUtility.MaxCoins);
            context.Campaign = campaign;
            var player = context.Player;
            player.Blood = 0;
            context.Player = player;
            context.BeginDraining();
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
