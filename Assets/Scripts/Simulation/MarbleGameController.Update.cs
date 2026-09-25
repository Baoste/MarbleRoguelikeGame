using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        private void Update()
        {
            if (!IsReady) return;
            CompleteRandomScoreRevealIfDue();
            UpdateFireInput();
            if (Paused) return;
            if (!IsSimulationRunning) return;
            accumulator += Math.Min(Time.deltaTime, 0.25f);
            var steps = 0;
            while (accumulator >= step && steps < maxSteps)
            {
                accumulator -= step;
                steps++;
                try
                {
                    if (LaunchPoint == null) throw new InvalidOperationException("LaunchPoint在运行中被销毁。");
                    StepLauncher(step);
                    simulation.BeforePhysics(step, bridge, LaunchWorldPosition, LaunchPoint.rotation);
                    PublishBloodIfChanged();
                    Physics.SyncTransforms();
                    bridge.Simulate(step);
                    simulation.AfterPhysics(bridge);
                    PublishRandomScoreResultIfReady();
                }
                catch (Exception error)
                {
                    simulation.AbortPhysicsStep();
                    RequiresRestart = true;
                    SetPaused(true);
                    Debug.LogException(error, this);
                    break;
                }
                if (!IsSimulationRunning)
                {
                    accumulator = 0;
                    break;
                }
            }
            // 超载时丢弃额外积欠时间，整局模拟一起放慢，不用巨大dt追赶导致穿透。
            if (accumulator >= step) accumulator %= step;
            FlushScores();
        }

    }
}
