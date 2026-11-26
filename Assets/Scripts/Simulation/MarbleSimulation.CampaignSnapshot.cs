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
                return new SessionSnapshot
                {
                    IsCampaign = context.Balance != null, Phase = (RoundPhase)round.Phase,
                    TotalScore = campaign.TotalScore, ConfirmedScore = round.Score,
                    RoundScore = round.Score, TargetScore = round.TargetScore, Coins = campaign.Coins,
                    UsesDailyLoadout = campaign.UsesDailyLoadout, CompanyScripBalance = campaign.CompanyScripBalance,
                    BiomassScrapBalance = context.Player.BiomassScrapBalance, LastCompanyScripReward = campaign.LastCompanyScripReward,
                    LastRewardCoins = campaign.LastRewardCoins,
                    LastCashoutCoins = campaign.LastCashoutCoins, ShopRefreshCount = campaign.ShopRefreshCount,
                    LayoutRevision = campaign.LayoutRevision,
                    CanCashOut = ProductionCommandsAvailable && CampaignStateUtility.CanCashOut(context)
                };
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
                    int remaining = DeviceDurabilitySystem.Remaining(context, item);
                    definition.PlacementCenter(item.X, item.Z, item.Angle, out float centerX, out float centerZ);
                    result[i] = new OwnedDeviceSnapshot
                    {
                        InstanceId = item.InstanceId, DefinitionId = item.DefinitionId, Name = definition.Name,
                        Placed = item.Placed, X = item.X, Z = item.Z, Radius = definition.PlacementRadius,
                        PlacementX = centerX, PlacementZ = centerZ,
                        PlacementOffsetX = definition.PlacementOffsetX, PlacementOffsetY = definition.PlacementOffsetY,
                        PlacementOffsetZ = definition.PlacementOffsetZ,
                        PurchasePrice = item.PurchasePrice,
                        SellPrice = CampaignStateUtility.SellPrice(definition, item),
                        Rarity = definition.Rarity, Durability = DeviceDurabilitySystem.Limit(context, item),
                        RemainingDurability = remaining,
                        CompletingEffect = remaining == 0 && item.Placed &&
                            DeviceDurabilitySystem.HasPendingEffect(context, entities[i], item.InstanceId),
                        ScoreMultiplier = definition.ScoreMultiplier, RushChanceAdd = definition.RushChanceAdd,
                        Kind = definition.Kind, Description = definition.Description, RotationLocked = definition.RotationLocked,
                        Angle = definition.ResolvePlacementAngle(item.Angle),
                        PairId = item.PairId, Range = definition.Range * (context.Content?.CentimetersToUnits ?? .01f) / .01f, Strength = definition.Strength
                    };
                }
                Array.Sort(result, (a, b) => a.InstanceId.CompareTo(b.InstanceId));
                return result;
            }
        }
    }
}
