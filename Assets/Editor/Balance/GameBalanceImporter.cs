using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static class GameBalanceImporter
    {
        public const string WorkbookPath = "Assets/Config/GameBalance.xlsx";
        public const string JsonPath = "Assets/Resources/GameBalance.json";

        // Attributes and catalogs are authored in GameContent.json.
        // Board geometry, placement settings and zones are authored in the scene.
        [Serializable]
        private sealed class BaseBalanceJson
        {
            public MarbleTuning Marble;
            public CampaignTuning Campaign;
            public StageDefinition[] Stages;
        }

        [MenuItem("Marbles ECS/Import Game Balance Excel")]
        public static void ImportDefault()
        {
            try
            {
                Import(WorkbookPath, JsonPath);
                Debug.Log("Imported GameBalance.xlsx. Restart the game to apply the validated balance.");
            }
            catch (Exception error)
            {
                Debug.LogError("Balance import failed. Invalid workbook values are never published.\n" + error.Message);
            }
        }

        public static void Import(string workbookPath, string jsonPath)
        {
            // Parsing, field completeness and cross-table validation all finish before any write.
            var balance = GameBalanceExcel.Read(workbookPath);
            var exported = new BaseBalanceJson
            {
                Marble = balance.Marble, Campaign = balance.Campaign, Stages = balance.Stages
            };
            string json = JsonUtility.ToJson(exported, true) + "\n";
            if (File.Exists(jsonPath) && File.ReadAllText(jsonPath) == json) return;
            BalanceAssetWriter.Write(jsonPath, json);
            if (jsonPath.StartsWith("Assets/", StringComparison.Ordinal))
                AssetDatabase.ImportAsset(jsonPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
