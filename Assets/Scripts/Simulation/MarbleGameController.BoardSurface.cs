using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        [Header("Board Geometry")]
        [Tooltip("Optional trapezoidal board boundary. Its transform must have unit scale.")]
        public BoardPlacementSurface PlacementSurface;
    }
}
