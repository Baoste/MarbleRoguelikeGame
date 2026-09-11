using System;
using UnityEngine;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>The rising edge retains one shot even if released before the next tick.</summary>
        public void SetFireHeld(bool held)
        {
            ThrowIfDisposed();
            if (!CanAcceptInput) return;
            var launcher = context.Launcher;
            if (held && launcher.FireHeld == 0 && launcher.PendingSingleShots < 32)
                launcher.PendingSingleShots++;
            launcher.FireHeld = (byte)(held ? 1 : 0);
            context.Launcher = launcher;
        }

        /// <summary>Use for a separate single-shot button; do not also call for a held-button edge.</summary>
        public void FireOnce()
        {
            ThrowIfDisposed();
            if (!CanAcceptInput) return;
            var launcher = context.Launcher;
            if (launcher.PendingSingleShots < 32) launcher.PendingSingleShots++;
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

        /// <summary>One score-drug type: use again to replace strength and refresh duration.</summary>
        public bool ApplyScoreDrug(double multiplier, float durationSeconds)
        {
            ThrowIfDisposed();
            MarbleRules.CheckMultiplier(multiplier);
            if (!MarbleRules.IsFinite(durationSeconds) || durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (!CanAcceptInput) return false;
            var effects = context.Manager.GetBuffer<ActiveScoreEffectData>(context.PlayerEntity);
            effects.Clear();
            effects.Add(new ActiveScoreEffectData
            {
                EffectId = 1, Multiplier = multiplier, EndsAt = context.Round.Time + durationSeconds
            });
            // UI observes the newly applied effect immediately; existing balls are recomputed pre-physics.
            var player = context.Player;
            player.DrugScoreMultiplier = multiplier;
            context.Player = player;
            return true;
        }

    }
}
