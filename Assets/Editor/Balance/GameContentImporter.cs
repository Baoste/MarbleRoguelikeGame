using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            Debug.Log("Imported " + result.Attributes.Length + " attributes, " + result.Devices.Length + " devices and " + result.Drugs.Length + " drugs. Restart Play to apply.");
        }
        public static GameContent Read(string path, GameContent recipes)
        {
            var sheets = XlsxWorkbookReader.Read(path, true);
            var result = recipes.Copy();
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
            // Migration authorized with this implementation: the older open workbook still
            // has three probabilities totalling .95. Preserve the confirmed four-way rule.
            if (!attributes.Any(x => x.Id == nameof(GameAttribute.GAMBLE_QUADRUPLE_CHANCE)))
            {
                foreach (var attribute in attributes)
                    if (attribute.Id == nameof(GameAttribute.GAMBLE_LOSS_CHANCE) || attribute.Id == nameof(GameAttribute.GAMBLE_RETURN_CHANCE) || attribute.Id == nameof(GameAttribute.GAMBLE_WIN_CHANCE)) attribute.DefaultValue = .25;
                attributes.Add(recipes.Definition(GameAttribute.GAMBLE_QUADRUPLE_CHANCE).Copy());
                Debug.LogWarning("Workbook has the previous three-outcome schema. Applying the confirmed four outcomes at 25%; add GAMBLE_QUADRUPLE_CHANCE to the workbook to author probabilities directly.");
            }
            result.Attributes = attributes.ToArray();
            var devices = new List<DeviceDefinition>();
            var drugs = new List<DrugDefinition>();
            foreach (var row in sheets["设计总表"])
            {
                string kind = Cell(row, 2), name = Cell(row, 1);
                if (kind == "装置")
                {
                    var recipe = recipes.Devices.SingleOrDefault(x => x.Name == name);
                    if (recipe == null) throw new InvalidDataException("Device behavior is not registered: " + name);
                    if (recipe.Description != Cell(row, 7)) throw new InvalidDataException(name + ": behavior prose changed. Review its numeric recipe and DeviceEffectSystem before importing.");
                    var device = recipe.Copy(); device.SourceId = Cell(row, 0); devices.Add(device);
                }
                else if (kind == "药物")
                {
                    var recipe = recipes.Drugs.SingleOrDefault(x => x.Name == name);
                    if (recipe == null) throw new InvalidDataException("Drug behavior is not registered: " + name);
                    if (recipe.Description != Cell(row, 7)) throw new InvalidDataException(name + ": behavior prose changed. Review its AttributeEffect recipe before importing.");
                    var drug = recipe.Copy(); drug.SourceId = Cell(row, 0);
                    var match = Regex.Match(Cell(row, 10), @"(\d+(?:\.\d+)?)秒");
                    if (!match.Success) throw new InvalidDataException("Drug window must specify seconds: " + name);
                    drug.DurationSeconds = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    drug.Stackable = Cell(row, 12) == "不限次数";
                    drugs.Add(drug);
                }
            }
            // Legacy definitions were migrated from GameBalance and are not authored in this workbook.
            devices.AddRange(recipes.Devices.Where(x => x.Kind == DeviceKind.LegacyPin).Select(x => x.Copy()));
            drugs.AddRange(recipes.Drugs.Where(x => x.Kind == DrugKind.LegacyBounce).Select(x => x.Copy()));
            result.Devices = devices.ToArray(); result.Drugs = drugs.ToArray(); result.Validate();
            double probabilities = result.Definition(GameAttribute.GAMBLE_LOSS_CHANCE).DefaultValue + result.Definition(GameAttribute.GAMBLE_RETURN_CHANCE).DefaultValue +
                result.Definition(GameAttribute.GAMBLE_WIN_CHANCE).DefaultValue + result.Definition(GameAttribute.GAMBLE_QUADRUPLE_CHANCE).DefaultValue;
            if (Math.Abs(probabilities - 1) > .000001) throw new InvalidDataException("Base gambling probabilities must sum to 1.");
            return result;
        }
        private static string Cell(string[] row, int index) => index < row.Length ? row[index] ?? "" : "";
        private static double Number(string[] row, int index) => double.Parse(Cell(row, index), CultureInfo.InvariantCulture);
    }
}
