using System;
using System.Collections.Generic;

namespace MarblesECS
{
    /// <summary>Draws complete device packs by advancing the caller's shared random stream.</summary>
    public static class RandomStartingDevices
    {
        public static uint[] Select(DeviceDefinition[] catalog, int typeCount, int capacity, ref uint randomState)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (typeCount < 0) throw new ArgumentOutOfRangeException(nameof(typeCount));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (typeCount == 0 || capacity == 0) return Array.Empty<uint>();

            var available = new List<DeviceDefinition>(catalog);
            // Stable ID ordering makes a recorded seed reproducible after asset enumeration changes.
            available.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = available.Count - 1; i > 0; i--)
            {
                int selected = (int)(MarbleRules.NextRandom(ref randomState) * (i + 1));
                DeviceDefinition swap = available[i];
                available[i] = available[selected];
                available[selected] = swap;
            }

            var result = new List<uint>();
            foreach (DeviceDefinition definition in available)
            {
                if (result.Count >= typeCount) break;
                if (definition.PurchaseQuantity > capacity) continue;
                result.Add(definition.Id);
                capacity -= definition.PurchaseQuantity;
            }
            return result.ToArray();
        }
    }
}
