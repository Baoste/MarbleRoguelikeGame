using System;
using Unity.Collections;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public SessionSnapshot Session
        {
            get
            {
                ThrowIfDisposed();
                var round = context.Round;
                var campaign = context.Campaign;
                var result = new SessionSnapshot
                {
                    IsCampaign = context.Balance != null, Phase = (RoundPhase)round.Phase,
                    StageIndex = campaign.StageIndex, TotalScore = campaign.TotalScore, PendingScore = round.PendingScore,
                    RoundScore = round.Score, TargetScore = round.TargetScore, Coins = campaign.Coins,
                    GamblingPayout = round.GamblingPayout, GamblingResolved = round.SettlementComplete,
                    GamblingWon = round.GamblingWon, LastRewardCoins = campaign.LastRewardCoins,
                    LastCashoutCoins = campaign.LastCashoutCoins, StagesCleared = campaign.StagesCleared,
                    LastSkippedStages = campaign.LastSkippedStages, ShopRefreshCount = campaign.ShopRefreshCount,
                    LayoutRevision = campaign.LayoutRevision, CanCashOut = CampaignStateUtility.CanCashOut(context)
                };
                if (context.Balance != null)
                {
                    var stage = context.Balance.Stages[campaign.StageIndex];
                    result.StageId = stage.Id;
                    result.StageCount = context.Balance.Stages.Length;
                    result.TargetScore = stage.TargetScore;
                    result.ConfirmedScore = round.SettlementComplete ? campaign.TotalScore :
                        MarbleRules.AddScore(campaign.TotalScore, round.Score, context.Balance.Campaign.MaxTotalScore);
                }
                return result;
            }
        }

        public OwnedDeviceSnapshot[] GetOwnedDevices()
        {
            ThrowIfDisposed();
            if (context.Balance == null) return Array.Empty<OwnedDeviceSnapshot>();
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
            {
                var result = new OwnedDeviceSnapshot[entities.Length];
                for (int i = 0; i < entities.Length; i++)
                {
                    var item = context.Manager.GetComponentData<OwnedDeviceData>(entities[i]);
                    var definition = CampaignStateUtility.Device(context, item.DefinitionId);
                    result[i] = new OwnedDeviceSnapshot
                    {
                        InstanceId = item.InstanceId, DefinitionId = item.DefinitionId, Name = definition.Name,
                        Placed = item.Placed, X = item.X, Z = item.Z, Radius = definition.Radius,
                        PurchasePrice = item.PurchasePrice,
                        SellPrice = MarbleRules.RoundScore(item.PurchasePrice * context.Balance.Campaign.SellRatio, CampaignStateUtility.MaxCoins),
                        ScoreMultiplier = definition.ScoreMultiplier, RushChanceAdd = definition.RushChanceAdd
                    };
                }
                Array.Sort(result, (a, b) => a.InstanceId.CompareTo(b.InstanceId));
                return result;
            }
        }
    }
}
