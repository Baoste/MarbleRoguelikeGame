using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Assigns stable, scene-unique gameplay IDs while authoring zones and pins.</summary>
    internal static class TargetIdAllocator
    {
        internal const int FirstPinId = 100000;
        internal const int FirstZoneId = 1;
        private static bool assigning;

        internal static int ForZone(MarbleZone owner, int current) => Assign(owner, current, FirstZoneId);
        internal static int ForPin(MarblePin owner, int current) => Assign(owner, current, FirstPinId);

        internal static void EnsureUniqueUnder(Transform root)
        {
            var used = new HashSet<int>();
            int nextZone = FirstZoneId;
            foreach (var zone in root.GetComponentsInChildren<MarbleZone>(true))
                if (zone.TargetId <= 0 || !used.Add(zone.TargetId))
                    zone.TargetId = TakeNext(used, ref nextZone);

            int nextPin = FirstPinId;
            foreach (var pin in root.GetComponentsInChildren<MarblePin>(true))
                if (pin.TargetId <= 0 || !used.Add(pin.TargetId))
                    pin.TargetId = TakeNext(used, ref nextPin);
        }

        internal static int TakeNext(HashSet<int> used, ref int candidate)
        {
            while (!used.Add(candidate)) candidate = checked(candidate + 1);
            return candidate;
        }

        private static int Assign(Component owner, int current, int firstCandidate)
        {
            if (assigning || Application.isPlaying)
                return current;

            assigning = true;
            try
            {
                var used = new HashSet<int>();
                foreach (var zone in Object.FindObjectsOfType<MarbleZone>(true))
                    if (zone != null && zone != owner && zone.TargetId > 0)
                        used.Add(zone.TargetId);
                foreach (var pin in Object.FindObjectsOfType<MarblePin>(true))
                    if (pin != null && pin != owner && pin.TargetId > 0)
                        used.Add(pin.TargetId);

                if (current > 0 && !used.Contains(current))
                    return current;

                return TakeNext(used, ref firstCandidate);
            }
            finally
            {
                assigning = false;
            }
        }
    }
}
