using System;
using System.Collections.Generic;

namespace MarblesECS
{
    internal static class GameBalanceValidation
    {
        internal static void Validate(GameBalance b)
        {
            if (b.Marble == null || b.Campaign == null || b.Board == null)
                throw new ArgumentException("Marble, Campaign and Board configurations are required.");
            b.Marble.Validate(); b.Campaign.Validate(); b.Board.Validate();
            CheckRows(b.Stages, 1, 100, x => x.Id, x => x.Validate(), "Stages");
            CheckRows(b.Devices, 2, 2, x => x.Id, x => x.Validate(), "Devices");
            CheckRows(b.Drugs, 2, 2, x => x.Id, x => x.Validate(), "Drugs");
            CheckRows(b.Zones, 3, 32, x => x.Id, x => x.Validate(), "Zones");
            ValidateStages(b); ValidateInventory(b); ValidateZones(b);
            if (b.Marble.MaxRadius >= b.Board.LauncherHeight || b.Marble.MaxRadius * 2 >= b.Board.WallHeight)
                throw new ArgumentException("Launcher height and walls must clear the largest marble.");
        }

        private static void ValidateStages(GameBalance b)
        {
            long previous = 0;
            foreach (var stage in b.Stages)
            {
                if (stage.TargetScore <= previous || stage.TargetScore > b.Campaign.MaxTotalScore)
                    throw new ArgumentException("Stage targets must strictly increase within MaxTotalScore.");
                previous = stage.TargetScore;
            }
        }

        private static void ValidateInventory(GameBalance b)
        {
            int deviceCount = 0, drugCount = 0, scoreTypes = 0;
            foreach (var device in b.Devices)
            {
                deviceCount += device.StartingCount;
                if (device.ScoreMultiplier == 2) scoreTypes++;
            }
            foreach (var drug in b.Drugs) drugCount += drug.StartingCount;
            if (scoreTypes != 1) throw new ArgumentException("Exactly one score pin and one Rush pin are required.");
            if (deviceCount > b.Campaign.MaxOwnedDevices || drugCount > b.Campaign.MaxDrugInventory)
                throw new ArgumentException("Starting inventory exceeds capacity.");
        }

        private static void ValidateZones(GameBalance b)
        {
            var kinds = new bool[3];
            foreach (var zone in b.Zones)
            {
                kinds[zone.Kind] = true;
                if (Math.Abs(zone.CenterX) + zone.Width / 2 > b.Board.Width / 2 ||
                    Math.Abs(zone.CenterZ) + zone.Depth / 2 > b.Board.Length / 2)
                    throw new ArgumentException("Zone " + zone.Id + " extends beyond the board.");
                if (zone.CenterZ + zone.Depth / 2 > b.Board.PlacementMinZ)
                    throw new ArgumentException("Terminal zones must be below the placement area.");
            }
            if (!kinds[0] || !kinds[1] || !kinds[2])
                throw new ArgumentException("Fixed, gambling and drain zones are all required.");
        }

        private static void CheckRows<T>(T[] rows, int min, int max, Func<T, uint> id, Action<T> validate, string name) where T : class
        {
            if (rows == null || rows.Length < min || rows.Length > max)
                throw new ArgumentException(name + " row count must be " + min + " .. " + max + ".");
            var seen = new HashSet<uint>();
            foreach (var row in rows)
            {
                if (row == null) throw new ArgumentException(name + " contains an empty row.");
                validate(row);
                if (!seen.Add(id(row))) throw new ArgumentException(name + " contains duplicate Id " + id(row) + ".");
            }
        }
    }
}
