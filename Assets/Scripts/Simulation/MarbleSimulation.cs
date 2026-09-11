using System;
using UnityEngine;

namespace MarblesECS
{
    /// <summary>
    /// Owns an isolated Entities World. A main-thread controller calls BeforePhysics,
    /// simulates PhysX once, then calls AfterPhysics. This World has no PlayerLoop entry.
    /// </summary>
    public sealed partial class MarbleSimulation : IDisposable
    {
        private readonly SimulationContext context;
        private bool disposed;
        private bool insidePhysicsStep;
        private bool started;
        public bool IsAlive => !disposed && context.World.IsCreated;

        public MarbleSimulation(MarbleTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            var ownedTuning = tuning.Copy();
            ownedTuning.Validate();
            context = new SimulationContext(ownedTuning);
        }

        public MarbleSimulation(GameBalance balance)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            var owned = balance.Copy();
            owned.Validate();
            context = new SimulationContext(owned.Marble, owned);
        }

        public void StartRound(IMarblePhysics physics)
        {
            if (context.Balance != null) StartSession(physics);
            else ResetRound(physics);
        }

        private void ResetRound(IMarblePhysics physics)
        {
            ThrowIfDisposed();
            if (physics == null) throw new ArgumentNullException(nameof(physics));
            if (insidePhysicsStep) throw new InvalidOperationException("Cannot reset during a physics step.");
            int newRoundId = checked(context.Round.RoundId + 1);
            context.RemoveAllMarbles();
            context.Physics = physics;
            context.Contacts.Clear();
            context.ScoreEvents.Clear();
            context.Manager.GetBuffer<ActiveScoreEffectData>(context.PlayerEntity).Clear();
            BounceDrugSystem.Clear(context);
            context.Round = new RoundData
            {
                RoundId = newRoundId, Phase = (byte)RoundPhase.Playing,
                TargetScore = context.Tuning.TargetScore, RandomState = context.Tuning.RandomSeed
            };
            context.Player = new PlayerData
            {
                Blood = context.Tuning.InitialBlood,
                BloodCostMultiplier = context.Tuning.BloodCostMultiplier, DrugScoreMultiplier = 1
            };
            context.Launcher = new LauncherData();
            LauncherMovementSystem.Reset(context);
            context.StepSeconds = 0;
            started = true;
        }

        public void BeforePhysics(float dt, IMarblePhysics physics, Vector3 launchPosition, Quaternion launchRotation)
        {
            RequirePhysics(physics);
            if (insidePhysicsStep) throw new InvalidOperationException("AfterPhysics must complete the previous step.");
            if (!MarbleRules.IsFinite(dt) || dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (context.StepSeconds > 0 && dt != context.StepSeconds)
                throw new ArgumentException("Fixed step cannot change within a round; restart to change it.", nameof(dt));
            if (!SimulationContext.IsFinite(launchPosition) || !SimulationContext.IsFinite(launchRotation))
                throw new ArgumentException("Launch pose must be finite and rotation must not be zero.");
            if (context.IsRunning)
            {
                context.StepSeconds = dt;
                var round = context.Round;
                round.Time += dt;
                round.Tick = checked(round.Tick + 1);
                context.Round = round;
                BounceDrugSystem.Expire(context);
                EffectLifecycleSystem.Execute(context);
                LaunchSystem.Execute(context, physics, launchPosition, launchRotation);
            }
            // Set only after pre-physics succeeds, so a failed spawn can be inspected/retried safely.
            insidePhysicsStep = true;
        }

        public void AfterPhysics(IMarblePhysics physics)
        {
            RequirePhysics(physics);
            if (!insidePhysicsStep) throw new InvalidOperationException("BeforePhysics must precede AfterPhysics.");
            try
            {
                if (!context.IsRunning) return;
                PhysicsReadbackSystem.Execute(context, physics);
                ContactResolutionSystem.Execute(context);
                LostMarbleSystem.Execute(context, physics);
                DespawnSystem.Execute(context, physics);
                RoundFlowSystem.Execute(context);
            }
            finally
            {
                context.Contacts.Clear();
                insidePhysicsStep = false;
            }
        }

        /// <summary>Recover the step boundary after a failed PhysX step; restart the round before resuming.</summary>
        public void AbortPhysicsStep()
        {
            ThrowIfDisposed();
            context.Contacts.Clear();
            context.ClearFireInput();
            insidePhysicsStep = false;
        }

        /// <summary>Presentation drains this queue; another fixed step never discards unread scores.</summary>
        public bool TryDequeueScore(out MarbleScoreEvent scoreEvent)
        {
            ThrowIfDisposed();
            if (context.ScoreEvents.Count == 0) { scoreEvent = default; return false; }
            scoreEvent = context.ScoreEvents.Dequeue();
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Entities' global shutdown also disposes manually owned worlds before some MB callbacks.
            if (!context.World.IsCreated)
            {
                foreach (var key in context.Marbles.Keys) context.Physics?.Remove(key);
                context.Marbles.Clear();
                context.Contacts.Clear();
                context.ScoreEvents.Clear();
                return;
            }
            try { context.RemoveAllMarbles(); }
            finally { if (context.World.IsCreated) context.World.Dispose(); }
        }

        private bool CanAcceptInput => started && context.Round.Phase == (byte)RoundPhase.Playing;

        private void RequirePhysics(IMarblePhysics physics)
        {
            ThrowIfDisposed();
            if (!started) throw new InvalidOperationException("Call StartRound before advancing the simulation.");
            if (physics == null || !ReferenceEquals(physics, context.Physics))
                throw new ArgumentException("Use the same physics adapter passed to StartRound.", nameof(physics));
        }

        private void ThrowIfDisposed()
        {
            if (!IsAlive) throw new ObjectDisposedException(nameof(MarbleSimulation));
        }
    }
}
