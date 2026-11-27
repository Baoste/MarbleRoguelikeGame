using System;
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
            var content = JsonUtility.FromJson<GameContent>(oldJson);
            content.Devices = DeviceResourceCatalog.Load(oldJson).Definitions();
            content.Validate();
            Check(!JsonUtility.ToJson(content).Contains("\"Drugs\""), "old JSON cannot restore removed drug definitions");
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
            Debug.Log("DRUG_REMOVAL_VALIDATION_PASSED: " + (assertions - start) + " assertions; obsolete input, spawn, settlement and generic attributes.");
        }
    }
}
