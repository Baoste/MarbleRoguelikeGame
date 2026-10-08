using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static class BalanceBatch
    {
        private static string SettingsPath => Path.GetFullPath("UserSettings/BalanceTools.json");
        [Serializable]
        private sealed class LocalSettings
        {
            public string PythonExecutable = "python";
        }

        private static LocalSettings settings;
        internal static string PythonExecutable
        {
            get
            {
                if (settings == null)
                {
                    settings = File.Exists(SettingsPath)
                        ? JsonUtility.FromJson<LocalSettings>(File.ReadAllText(SettingsPath))
                        : new LocalSettings { PythonExecutable = EditorPrefs.GetString(
                            "Pachinko.Balance.Python." + Application.dataPath, "python") };
                    if (settings == null) settings = new LocalSettings();
                }
                return string.IsNullOrWhiteSpace(settings.PythonExecutable) ? "python" : settings.PythonExecutable;
            }
            set
            {
                settings = new LocalSettings { PythonExecutable = string.IsNullOrWhiteSpace(value) ? "python" : value.Trim() };
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
            }
        }

        [MenuItem("Tools/Pachinko/Balance/Run Physical Sampling")]
        public static void RunPhysicalSampling()
        {
            int samples = EnvironmentInt("BALANCE_PHYSICAL_SAMPLES", 1800);
            int seed = EnvironmentInt("BALANCE_SEED", 20260930);
            BalancePhysicalSampler.Report report = BalancePhysicalSampler.Run(
                BalancePhysicalSampler.DefaultScene, samples, seed,
                BalancePhysicalSampler.DefaultJson, BalancePhysicalSampler.DefaultCsv);
            UnityEngine.Debug.Log("Balance physical sampling complete: " + report.SampleCount +
                " samples -> " + BalancePhysicalSampler.DefaultJson);
        }

        [MenuItem("Tools/Pachinko/Balance/Run Economy Simulation")]
        public static void RunEconomySimulation()
        {
            int runs = EnvironmentInt("BALANCE_SIMULATION_COUNT", 10000);
            int seed = EnvironmentInt("BALANCE_SEED", 20260930);
            string arguments = Quote(Path.GetFullPath("Tools/Balance/balance_pipeline.py")) +
                " simulate --root " + Quote(Path.GetFullPath(".")) +
                " --runs " + runs.ToString(CultureInfo.InvariantCulture) +
                " --seed " + seed.ToString(CultureInfo.InvariantCulture);
            RunPython(arguments);
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Pachinko/Balance/Run Full Pipeline")]
        public static void RunFullPipeline()
        {
            RunPhysicalSampling();
            RunEconomySimulation();
        }

        internal static string RunPython(string arguments)
        {
            string executable = Environment.GetEnvironmentVariable("BALANCE_PYTHON");
            if (string.IsNullOrWhiteSpace(executable)) executable = PythonExecutable;
            var start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = Path.GetFullPath("."),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            string temporary = Path.GetFullPath(".utmp/balance-python");
            Directory.CreateDirectory(temporary);
            start.EnvironmentVariables["TEMP"] = temporary;
            start.EnvironmentVariables["TMP"] = temporary;
            start.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"] = "1";
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Could not start balance Python process.");
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException(
                    "Balance pipeline failed with exit code " + process.ExitCode + "\n" + output + "\n" + error);
                if (!string.IsNullOrWhiteSpace(error)) UnityEngine.Debug.LogWarning(error);
                UnityEngine.Debug.Log(output);
                return output;
            }
        }

        private static int EnvironmentInt(string name, int fallback)
        {
            string raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
                ? value : fallback;
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
