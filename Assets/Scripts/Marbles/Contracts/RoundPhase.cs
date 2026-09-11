using UnityEngine;

namespace MarblesECS
{
    public enum RoundPhase : byte { Playing, Draining, Won, Lost, Build, Shop, Settling = Draining }
}
