using System;
using UnityEngine;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public Vector3 LauncherLocalPosition
        {
            get { ThrowIfDisposed(); return context.Manager.GetComponentData<LauncherMovementState>(context.LauncherEntity).LocalPosition; }
        }

        public void ConfigureLauncherMovement(Vector3 initialPosition, float halfWidth, float speed)
        {
            ThrowIfDisposed();
            if (insidePhysicsStep) throw new InvalidOperationException("Cannot configure during a physics step.");
            if (!SimulationContext.IsFinite(initialPosition) || !MarbleRules.IsFinite(halfWidth) || halfWidth < 0 ||
                !MarbleRules.IsFinite(speed) || speed < 0) throw new ArgumentException("Invalid launcher movement configuration.");
            context.Manager.SetComponentData(context.LauncherEntity, new LauncherMovementConfig
                { InitialLocalPosition = initialPosition, HalfWidth = halfWidth, Speed = speed });
            LauncherMovementSystem.Reset(context);
        }

        public void SetLauncherMovementInput(float horizontal, bool automatic)
        {
            ThrowIfDisposed();
            if (!MarbleRules.IsFinite(horizontal)) throw new ArgumentOutOfRangeException(nameof(horizontal));
            var input = context.Manager.GetComponentData<LauncherMovementInput>(context.LauncherEntity);
            input.Horizontal = horizontal;
            input.Automatic = (byte)(automatic ? 1 : 0);
            context.Manager.SetComponentData(context.LauncherEntity, input);
        }

        public void RequestLauncherPosition(float x)
        {
            ThrowIfDisposed();
            if (!MarbleRules.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
            if (!context.IsRunning) return;
            var input = context.Manager.GetComponentData<LauncherMovementInput>(context.LauncherEntity);
            input.RequestedX = x;
            input.HasPositionRequest = 1;
            context.Manager.SetComponentData(context.LauncherEntity, input);
        }

        /// <summary>Run before copying the local pose to the physics launch Transform and calling BeforePhysics.</summary>
        public void StepLauncherMovement(float seconds)
        {
            ThrowIfDisposed();
            if (insidePhysicsStep) throw new InvalidOperationException("Cannot move launcher during a physics step.");
            if (!MarbleRules.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            LauncherMovementSystem.Execute(context, seconds);
        }
    }
}
