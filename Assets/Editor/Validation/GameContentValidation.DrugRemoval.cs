using System;
using System.Linq;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void LegacyDrugRemoval()
        {
            int start = assertions;
            var json = Resources.Load<TextAsset>("GameContent").text;
            Check(!json.Contains("\"Drugs\""), "runtime configuration contains no legacy drugs");
            var oldJson = json.Replace("\"Attributes\":", "\"Drugs\":[{\"Id\":1,\"StartingCount\":999}],\"Attributes\":");
            var content = JsonUtility.FromJson<GameContent>(oldJson); content.Validate();
            Check(!JsonUtility.ToJson(content).Contains("\"Drugs\""), "old JSON cannot restore removed drug definitions");
            var imported = GameContentImporter.Read(GameContentImporter.Workbook, content);
            Check(!JsonUtility.ToJson(imported).Contains("\"Drugs\""), "legacy workbook cannot restore drugs");
            using (var f = new Fixture())
            {
                var first = f.Launch(); double bounce = f.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BOUNCE);
                var key = f.Launch();
                Near(998, f.C.Player.Blood, "ordinary shots cost one each without hidden drug multipliers");
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "fresh ball retains configured value");
                Near(bounce, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BOUNCE), "no hidden bounce dose");
                f.Step(new MarbleContact { Key = key, TargetId = 890, Kind = MarbleContactKind.Pin, Multiplier = 1 });
                Check(f.C.Marbles.Count == 2, "pin collision no longer runs removed replication drug");
                f.Step(new MarbleContact { Key = key, TargetId = 891, Kind = MarbleContactKind.Score, Multiplier = 1 });
                Near(10, f.C.Round.Score, "plain score has no drug multiplier layer");
                f.Simulation.ApplyGlobalAttribute(8000, GameAttribute.BALL_BASE_VALUE, AttributeOperation.Add, 3);
                var fresh = f.Launch();
                Near(13, f.Simulation.GetMarbleAttribute(fresh, GameAttribute.BALL_BASE_VALUE), "generic spawn attribute API remains available");
                Near(10, f.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE), "existing balls retain their original snapshot");
            }
            using (var f = new Fixture())
            {
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Shop; f.C.Round = round;
                var campaign = f.C.Campaign; campaign.Coins = 1000; f.C.Campaign = campaign;
                CampaignShopSystem.Generate(f.C);
                Check(f.Simulation.GetShopOffers().All(o => o.Kind == ShopItemKind.Device), "shop exposes only device offers");
                var offers = f.C.Manager.GetBuffer<ShopOfferData>(f.C.RoundEntity);
                var valid = offers[0]; var obsolete = valid; obsolete.Kind = (ShopItemKind)1; offers[0] = obsolete;
                Check(!f.Simulation.BuyOffer(0), "obsolete drug-kind offer is rejected safely");
                Near(1000, f.C.Campaign.Coins, "rejected obsolete offer cannot consume coins");
                offers[0] = valid; int before = f.Simulation.GetOwnedDevices().Length;
                var definition = f.C.Balance.Devices.Single(d => d.Id == valid.DefinitionId);
                Check(f.Simulation.BuyOffer(0), "real device purchase still succeeds");
                Near(before + definition.PurchaseQuantity, f.Simulation.GetOwnedDevices().Length, "purchase grants complete device quantity");
                Near(1000 - valid.Price, f.C.Campaign.Coins, "device purchase charges correct price");
                Check(!f.Simulation.BuyOffer(0), "sold offer cannot be purchased twice");
            }
            Debug.Log("DRUG_REMOVAL_VALIDATION_PASSED: " + (assertions - start) + " assertions; obsolete input, spawn, settlement, generic attributes and device-only shop.");
        }
    }
}
