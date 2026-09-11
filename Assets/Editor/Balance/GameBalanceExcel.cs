using System.Collections.Generic;
using System.IO;

namespace MarblesECS.Editor
{
    public static class GameBalanceExcel
    {
        public static GameBalance Read(string path)
        {
            var sheets = XlsxWorkbookReader.Read(path);
            var expected = new HashSet<string> { "Marble", "Campaign", "Board", "Stages", "Devices", "Drugs", "Zones" };
            foreach (var name in sheets.Keys)
                if (!expected.Contains(name)) throw new InvalidDataException("Unknown balance worksheet: " + name);
            foreach (var name in expected)
                if (!sheets.ContainsKey(name)) throw new InvalidDataException("Missing balance worksheet: " + name);
            var result = new GameBalance
            {
                Marble = BalanceTableParser.Singleton<MarbleTuning>(sheets["Marble"], "Marble"),
                Campaign = BalanceTableParser.Singleton<CampaignTuning>(sheets["Campaign"], "Campaign"),
                Board = BalanceTableParser.Singleton<BoardTuning>(sheets["Board"], "Board"),
                Stages = BalanceTableParser.Table<StageDefinition>(sheets["Stages"], "Stages"),
                Devices = BalanceTableParser.Table<DeviceDefinition>(sheets["Devices"], "Devices"),
                Drugs = BalanceTableParser.Table<DrugDefinition>(sheets["Drugs"], "Drugs"),
                Zones = BalanceTableParser.Table<ZoneDefinition>(sheets["Zones"], "Zones")
            };
            result.Validate();
            return result;
        }
    }
}
