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
            b.Content?.Validate();
            CheckRows(b.Devices, b.Content == null ? 0 : 1, 200, x => x.Id, x => x.Validate(), "Devices");
            ValidateInventory(b);
        }

        private static void ValidateInventory(GameBalance b)
        {
            int deviceCount = 0;
            foreach (var device in b.Devices)
            {
                deviceCount += device.StartingCount * device.PurchaseQuantity;
            }
            if (deviceCount > b.Campaign.MaxOwnedDevices)
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
