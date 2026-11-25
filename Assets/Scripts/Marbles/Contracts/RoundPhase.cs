using UnityEngine;

namespace MarblesECS
{
    public enum RoundPhase : byte { Playing, Draining, Won, Lost, Build, Settling = Draining }
}
