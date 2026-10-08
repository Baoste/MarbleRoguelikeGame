using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    public sealed class BalanceDashboardWindow : EditorWindow
    {
        private const string SummaryPath = "Docs/Balance/latest_summary.json";
        private const string ComparisonPath = "Docs/Balance/dashboard_configs.json";
        private int simulationCount = 10000;
        private int physicalSamples = 1800;
        private int seed = 20260930;
        private Vector2 scroll;
        private DashboardSummary summary;
        private DashboardCollection comparisons;
        private string status = "No simulation result loaded.";
        private int compareAIndex;
        private int compareBIndex = 1;
        private bool showComparison;

        [MenuItem("Tools/Pachinko/Balance Dashboard")]
        public static void Open() => GetWindow<BalanceDashboardWindow>("Balance Dashboard");

        private void OnEnable() => LoadSummary();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Pachinko Balance Pipeline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Physical Sampling uses the authored TestScene and gameplay PhysX bridge. Economy Simulation consumes the empirical profile with a fixed seed.", MessageType.Info);
            seed = EditorGUILayout.IntField("Seed", seed);
            physicalSamples = Mathf.Max(90, EditorGUILayout.IntField("Physical Samples", physicalSamples));
            simulationCount = Mathf.Max(100, EditorGUILayout.IntField("Runs / Strategy", simulationCount));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string executable = EditorGUILayout.TextField("Python 3.10+", BalanceBatch.PythonExecutable);
                if (EditorGUI.EndChangeCheck()) BalanceBatch.PythonExecutable = executable;
                if (GUILayout.Button("Browse", GUILayout.Width(65)))
                {
                    string selected = EditorUtility.OpenFilePanel("Select Python executable", "", "");
                    if (!string.IsNullOrEmpty(selected)) BalanceBatch.PythonExecutable = selected;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run Physical Sampling")) RunPhysical();
                if (GUILayout.Button("Run Simulation")) RunEconomy();
                if (GUILayout.Button("Run Full Pipeline")) { RunPhysical(); RunEconomy(); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export CSV Folder")) EditorUtility.RevealInFinder(Path.GetFullPath("Docs/Balance"));
                if (GUILayout.Button("Reload Results")) LoadSummary();
                if (GUILayout.Button("Compare Config")) CompareConfig();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
            DrawComparison();

            if (summary == null || summary.Strategies == null) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Latest Result", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Config", summary.Config);
            EditorGUILayout.LabelField("Runs", summary.Runs.ToString());
            foreach (StrategySummary strategy in summary.Strategies)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(strategy.Strategy, EditorStyles.boldLabel);
                Metric("Win Rate", strategy.WinRate, "P1");
                Metric("Round Clear Rate", strategy.RoundClearRate, "P1");
                Metric("Average Score", strategy.AverageScore, "N1");
                Metric("Average Gold", strategy.AverageGold, "N1");
                Metric("Average Blood Remaining", strategy.AverageBloodRemaining, "N1");
                Metric("Cashout Rate", strategy.CashoutRate, "P1");
                Metric("Dead Run Rate", strategy.DeadRunRate, "P1");
                EditorGUILayout.LabelField("Top EV Items", strategy.TopItems ?? "N/A");
                EditorGUILayout.LabelField("Lowest EV Items", strategy.LowItems ?? "N/A");
            }
            EditorGUILayout.EndScrollView();
        }

        private static void Metric(string label, double value, string format)
        {
            EditorGUILayout.LabelField(label, value.ToString(format));
        }

        private void RunPhysical()
        {
            try
            {
                Environment.SetEnvironmentVariable("BALANCE_PHYSICAL_SAMPLES", physicalSamples.ToString());
                Environment.SetEnvironmentVariable("BALANCE_SEED", seed.ToString());
                BalanceBatch.RunPhysicalSampling();
                status = "Physical profile updated.";
            }
            catch (Exception error) { status = error.Message; UnityEngine.Debug.LogException(error); }
        }

        private void RunEconomy()
        {
            try
            {
                Environment.SetEnvironmentVariable("BALANCE_SIMULATION_COUNT", simulationCount.ToString());
                Environment.SetEnvironmentVariable("BALANCE_SEED", seed.ToString());
                BalanceBatch.RunEconomySimulation();
                LoadSummary();
            }
            catch (Exception error) { status = error.Message; UnityEngine.Debug.LogException(error); }
        }

        private void LoadSummary()
        {
            try
            {
                if (!File.Exists(SummaryPath)) { summary = null; status = "Run the simulation to create " + SummaryPath; return; }
                summary = JsonUtility.FromJson<DashboardSummary>(File.ReadAllText(SummaryPath));
                comparisons = File.Exists(ComparisonPath)
                    ? JsonUtility.FromJson<DashboardCollection>(File.ReadAllText(ComparisonPath))
                    : null;
                if (comparisons != null && comparisons.Configs != null && comparisons.Configs.Length > 1)
                    compareBIndex = Mathf.Clamp(compareBIndex, 1, comparisons.Configs.Length - 1);
                status = summary == null ? "Summary JSON is invalid." : "Loaded " + SummaryPath;
            }
            catch (Exception error) { summary = null; status = error.Message; }
            Repaint();
        }

        private void CompareConfig()
        {
            showComparison = !showComparison;
        }

        private void DrawComparison()
        {
            if (!showComparison) return;
            if (comparisons == null || comparisons.Configs == null || comparisons.Configs.Length < 2)
            {
                EditorGUILayout.HelpBox("Run all configured proposals to compare KPI deltas.", MessageType.Warning);
                return;
            }
            string[] names = Array.ConvertAll(comparisons.Configs, item => item.Config);
            using (new EditorGUILayout.HorizontalScope())
            {
                compareAIndex = EditorGUILayout.Popup("Config A", Mathf.Clamp(compareAIndex, 0, names.Length - 1), names);
                compareBIndex = EditorGUILayout.Popup("Config B", Mathf.Clamp(compareBIndex, 0, names.Length - 1), names);
            }
            DashboardSummary a = comparisons.Configs[compareAIndex];
            DashboardSummary b = comparisons.Configs[compareBIndex];
            EditorGUILayout.LabelField("B - A KPI delta", EditorStyles.boldLabel);
            int count = Mathf.Min(a.Strategies == null ? 0 : a.Strategies.Length,
                b.Strategies == null ? 0 : b.Strategies.Length);
            for (int index = 0; index < count; index++)
            {
                StrategySummary left = a.Strategies[index];
                StrategySummary right = b.Strategies[index];
                EditorGUILayout.LabelField(right.Strategy, EditorStyles.miniBoldLabel);
                Metric("  Win Rate", right.WinRate - left.WinRate, "+0.0%;-0.0%;0.0%");
                Metric("  Round Clear", right.RoundClearRate - left.RoundClearRate, "+0.0%;-0.0%;0.0%");
                Metric("  Average Score", right.AverageScore - left.AverageScore, "+0.0;-0.0;0.0");
                Metric("  Average Gold", right.AverageGold - left.AverageGold, "+0.0;-0.0;0.0");
                Metric("  Cashout Rate", right.CashoutRate - left.CashoutRate, "+0.0%;-0.0%;0.0%");
                Metric("  Dead Run", right.DeadRunRate - left.DeadRunRate, "+0.0%;-0.0%;0.0%");
            }
        }

        [Serializable]
        private sealed class DashboardSummary
        {
            public string Config;
            public int Runs;
            public StrategySummary[] Strategies;
        }

        [Serializable]
        private sealed class DashboardCollection
        {
            public DashboardSummary[] Configs;
        }

        [Serializable]
        private sealed class StrategySummary
        {
            public string Strategy;
            public double WinRate, RoundClearRate, AverageScore, AverageGold,
                AverageBloodRemaining, CashoutRate, DeadRunRate;
            public string TopItems, LowItems;
        }
    }
}
