using System;
using System.Collections.Generic;

namespace MarblesECS
{
    internal static class GameBalanceValidation
    {
        internal static void Validate(GameBalance b)
        {
            if (b.Marble == null || b.Campaign == null)
                throw new ArgumentException("Marble and Campaign configurations are required.");
            b.Marble.Validate(); b.Campaign.Validate();
            CheckRows(b.Stages, 1, 100, x => x.Id, x => x.Validate(), "Stages");
            b.Content?.Validate();
            CheckRows(b.Devices, b.Content == null ? 0 : 1, 200, x => x.Id, x => x.Validate(), "Devices");
            CheckRows(b.Drugs, b.Content == null ? 0 : 1, 200, x => x.Id, x => x.Validate(), "Drugs");
            ValidateStages(b); ValidateInventory(b);
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
            int deviceCount = 0, drugCount = 0;
            foreach (var device in b.Devices)
            {
                deviceCount += device.StartingCount * (device.Kind == DeviceKind.Portal ? 2 : 1);
            }
            foreach (var drug in b.Drugs) drugCount += drug.StartingCount;
            if (deviceCount > b.Campaign.MaxOwnedDevices || drugCount > b.Campaign.MaxDrugInventory)
                throw new ArgumentException("Starting inventory exceeds capacity.");
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
