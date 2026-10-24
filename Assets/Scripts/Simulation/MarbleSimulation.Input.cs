using System;
using UnityEngine;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Press requests one shot and starts the hold ramp. Release cancels all unconsumed input.</summary>
        public void SetFireHeld(bool held)
        {
            ThrowIfDisposed();
            if (!held) { context.ClearFireInput(); return; }
            if (!CanAcceptInput) return;
            var launcher = context.Launcher;
            if (launcher.FireHeld != 0) return;
            launcher.FireRequested = 1;
            launcher.FireHeld = 1;
            launcher.HeldSeconds = launcher.HeldShotProgress = 0;
            context.Launcher = launcher;
        }

        /// <summary>One-step request. Repeated calls coalesce; cooldown or failed shots are discarded.</summary>
        public void FireOnce()
        {
            ThrowIfDisposed();
            if (!CanAcceptInput) return;
            var launcher = context.Launcher;
            launcher.FireRequested = 1;
            context.Launcher = launcher;
        }

        public void ClearFireInput()
        {
            ThrowIfDisposed();
            context.ClearFireInput();
        }

        public void SetFlow(float normalized)
        {
            ThrowIfDisposed();
            if (!MarbleRules.IsFinite(normalized)) throw new ArgumentOutOfRangeException(nameof(normalized));
            if (!CanAcceptInput) return;
            var launcher = context.Launcher;
            launcher.Flow = Mathf.Clamp01(normalized);
            context.Launcher = launcher;
        }

        public void EndFiring()
        {
            ThrowIfDisposed();
            if (context.Balance != null) { CashOut(); return; }
            if (CanAcceptInput) context.BeginDraining();
        }



    }
}
