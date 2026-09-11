using System;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Add one timed dose. Existing marbles keep the restitution captured at launch.</summary>
        public bool ApplyBounceDrug(float multiplier, float durationSeconds)
        {
            ThrowIfDisposed();
            if (insidePhysicsStep) throw new InvalidOperationException("Use drugs outside the PhysX step.");
            if (!MarbleRules.IsFinite(multiplier) || multiplier <= 0 || multiplier > 10 ||
                !MarbleRules.IsFinite(durationSeconds) || durationSeconds <= 0)
                throw new ArgumentOutOfRangeException();
            if (!CanAcceptInput) return false;
            BounceDrugSystem.Expire(context);
            var doses = context.Manager.GetBuffer<ActiveBounceDrugData>(context.PlayerEntity);
            if (doses.Length >= context.Tuning.MaxActiveBounceDrugs) return false;
            doses.Add(new ActiveBounceDrugData { Multiplier = multiplier,
                EndsAt = context.Round.Time + durationSeconds });
            return true;
        }
    }
}
