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
            string json = JsonUtility.ToJson(balance, true) + "\n";
            if (File.Exists(jsonPath) && File.ReadAllText(jsonPath) == json) return;
            BalanceAssetWriter.Write(jsonPath, json);
            if (jsonPath.StartsWith("Assets/", StringComparison.Ordinal))
                AssetDatabase.ImportAsset(jsonPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
