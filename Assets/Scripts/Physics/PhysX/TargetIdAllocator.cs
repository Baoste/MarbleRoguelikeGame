using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Assigns stable, scene-unique gameplay IDs while authoring zones and pins.</summary>
    internal static class TargetIdAllocator
    {
        private static bool assigning;

        internal static int ForZone(MarbleZone owner, int current) => Assign(owner, current, 1);
        internal static int ForPin(MarblePin owner, int current) => Assign(owner, current, 100000);

        internal static void EnsureUniqueUnder(Transform root)
        {
            var used = new HashSet<int>();
            int nextZone = 1;
            foreach (var zone in root.GetComponentsInChildren<MarbleZone>(true))
            {
                if (zone.TargetId <= 0 || !used.Add(zone.TargetId))
                {
                    while (used.Contains(nextZone))
                        nextZone = checked(nextZone + 1);
                    zone.TargetId = nextZone;
                    used.Add(nextZone);
                }
            }

            int nextPin = 100000;
            foreach (var pin in root.GetComponentsInChildren<MarblePin>(true))
            {
                if (pin.TargetId <= 0 || !used.Add(pin.TargetId))
                {
                    while (used.Contains(nextPin))
                        nextPin = checked(nextPin + 1);
                    pin.TargetId = nextPin;
                    used.Add(nextPin);
                }
            }
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

                int candidate = firstCandidate;
                while (used.Contains(candidate))
                    candidate = checked(candidate + 1);
                return candidate;
            }
            finally
            {
                assigning = false;
            }
        }
    }
}
