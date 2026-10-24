using System;

namespace MarblesECS
{
    /// <summary>Samples one-shot input and a held-button ramp without accumulating future shots.</summary>
    internal static class LauncherFireSystem
    {
        internal static bool TryConsumeInput(SimulationContext c)
        {
            if (c.Round.Phase != (byte)RoundPhase.Playing)
            {
                c.ClearFireInput();
                return false;
            }

            var launcher = c.Launcher;
            bool pressed = launcher.FireRequested != 0;
            // Consume before checking cooldown, capacity, blood or the physics adapter.
            launcher.FireRequested = 0;
            if (launcher.FireHeld != 0)
            {
                double duration = c.Tuning.HoldAccelerationSeconds;
                double before = launcher.HeldSeconds;
                double after = before + c.StepSeconds;
                // Integrate a linear 0..1 ramp, then the constant maximum rate.
                double progress = (RampArea(after, duration) - RampArea(before, duration)) /
                    RushSystem.FireInterval(c, c.Tuning.FireIntervalSeconds);
                launcher.HeldSeconds = Math.Min(duration, after);
                // A stall can make at most one held shot due, never a catch-up burst.
                launcher.HeldShotProgress = Math.Min(1, launcher.HeldShotProgress + progress);
            }

            bool due = pressed || (launcher.FireHeld != 0 && launcher.HeldShotProgress >= 1 - 1e-7);
            double interval = RushSystem.FireInterval(c, pressed
                ? c.Tuning.MaxFireIntervalSeconds : c.Tuning.FireIntervalSeconds);
            bool accepted = due && (launcher.HasFired == 0 || c.Round.Time + 1e-7 >= launcher.LastFireAt + interval);
            if (accepted) launcher.HeldShotProgress = 0;
            c.Launcher = launcher;
            return accepted;
        }

        internal static void RecordShot(SimulationContext c)
        {
            var launcher = c.Launcher;
            launcher.LastFireAt = c.Round.Time;
            launcher.HasFired = 1;
            c.Launcher = launcher;
        }

        private static double RampArea(double seconds, double duration) => seconds < duration
            ? seconds * seconds / (2 * duration) : seconds - duration * .5;
    }
}
