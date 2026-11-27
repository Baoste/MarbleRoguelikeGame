using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static class BalanceBatch
    {
        [MenuItem("Tools/Pachinko/Balance/Run Physical Sampling")]
        public static void RunPhysicalSampling()
        {
            int samples = EnvironmentInt("BALANCE_PHYSICAL_SAMPLES", 1800);
            int seed = EnvironmentInt("BALANCE_SEED", 20260930);
            RunPhysicalSampling(samples, seed);
        }

        internal static void RunPhysicalSampling(int samples, int seed)
        {
            BalancePhysicalSampler.Report report = BalancePhysicalSampler.Run(
                BalancePhysicalSampler.DefaultScene, samples, seed,
                BalancePhysicalSampler.DefaultJson, BalancePhysicalSampler.DefaultCsv);
            Debug.Log("Balance physical sampling complete: " + report.SampleCount +
                " samples -> " + BalancePhysicalSampler.DefaultJson);
        }

        private static int EnvironmentInt(string name, int fallback)
        {
            string raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
                ? value : fallback;
        }
    }
}
