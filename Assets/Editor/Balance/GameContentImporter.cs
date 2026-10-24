using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static class GameContentImporter
    {
        public const string Workbook = "Assets/Docs/血色柏青哥_道具药物装置_规范修订版.xlsx";
        public const string Output = "Assets/Resources/GameContent.json";
        [MenuItem("Marbles ECS/Import Attribute and Content Excel")]
        public static void Import()
        {
            var result = Read(Workbook, JsonUtility.FromJson<GameContent>(File.ReadAllText(Output)));
            string json = JsonUtility.ToJson(result, true) + "\n";
            if (File.ReadAllText(Output) != json) BalanceAssetWriter.Write(Output, json);
            AssetDatabase.ImportAsset(Output);
            Debug.Log("Imported " + result.Attributes.Length + " attributes, " + result.Devices.Length + " devices. Restart Play to apply.");
        }
        public static GameContent Read(string path, GameContent recipes)
        {
            var sheets = XlsxWorkbookReader.Read(path, true);
            var result = JsonUtility.FromJson<GameContent>(JsonUtility.ToJson(recipes));
            result.Devices = DeviceCatalogBuilder.ReadDefinitions();
            var attributes = new List<AttributeDefinition>();
            foreach (var row in sheets["00_ATTRIBUTE_DICTIONARY (2)"])
            {
                if (row.Length < 20 || Cell(row, 16) != "No") continue;
                if (!Enum.TryParse(Cell(row, 0), out GameAttribute id) || id == GameAttribute.Count)
                    throw new InvalidDataException("Attribute has no runtime implementation: " + Cell(row, 0));
                attributes.Add(new AttributeDefinition { Id = Cell(row, 0), Name = Cell(row, 1),
                    Unit = id == GameAttribute.BALL_LAUNCH_SPEED ? "m/s" : Cell(row, 4),
                    DefaultValue = Number(row, 5), MinValue = Number(row, 6), MaxValue = Number(row, 7),
                    Operators = Cell(row, 8), StackRule = Cell(row, 9), AppliesTo = Cell(row, 10), StackOrder = (int)Number(row, 11) });
            }
            // Retired probability attributes keep their stable IDs for existing content files.
            // They no longer configure gameplay; the wheel has eight fixed, equally likely sectors.
            if (!attributes.Any(x => x.Id == nameof(GameAttribute.GAMBLE_QUADRUPLE_CHANCE)))
                attributes.Add(recipes.Definition(GameAttribute.GAMBLE_QUADRUPLE_CHANCE).Copy());
            result.Attributes = attributes.ToArray();
            result.Validate();
            return result;
        }
        private static string Cell(string[] row, int index) => index < row.Length ? row[index] ?? "" : "";
        private static double Number(string[] row, int index) => double.Parse(Cell(row, index), CultureInfo.InvariantCulture);
    }
}
