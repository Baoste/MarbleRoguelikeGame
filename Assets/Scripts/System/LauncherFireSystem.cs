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
                    RushSystem.FireInterval(c);
                launcher.HeldSeconds = Math.Min(duration, after);
                // A stall can make at most one held shot due, never a catch-up burst.
                launcher.HeldShotProgress = Math.Min(1, launcher.HeldShotProgress + progress);
            }

            bool due = pressed || (launcher.FireHeld != 0 && launcher.HeldShotProgress >= 1 - 1e-7);
            bool accepted = due && c.Round.Time + 1e-7 >= launcher.NextFireAt;
            if (accepted) launcher.HeldShotProgress = 0;
            c.Launcher = launcher;
            return accepted;
        }

        private static double RampArea(double seconds, double duration) => seconds < duration
            ? seconds * seconds / (2 * duration) : seconds - duration * .5;
    }
}
