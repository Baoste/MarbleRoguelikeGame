using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MarblesECS.PhysX;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MarblesECS.Editor
{
    [InitializeOnLoad]
    public static class StatusHudValidation
    {
        private const string Pending = "StatusHudValidation.Pending";
        private static readonly List<string> Report = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static IEnumerator sequence;
        private static double deadline;
        private static bool originalBackground;
        private static MarbleGameController game;
        private static MarbleSimulation simulation;
        private static SimulationContext context;
        private static ScoreListener hud;
        public static bool Succeeded { get; private set; }
        private static string Output => Environment.GetEnvironmentVariable("MARBLE_STATUS_HUD_VALIDATION_OUTPUT") ??
            Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/status-hud-bars"));

        static StatusHudValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            };
        }

        [MenuItem("Marbles ECS/UI/验证右上角状态进度条")]
        public static void Run()
        {
            if (sequence != null) throw new InvalidOperationException("Validation already running.");
            Succeeded = false;
            if (EditorApplication.isPlaying) Start();
            else { SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode(); }
        }
        private static void Start()
        {
            Report.Clear(); Errors.Clear(); Directory.CreateDirectory(Output);
            originalBackground = Application.runInBackground;
            Application.runInBackground = true;
            Application.logMessageReceived += RecordError;
            deadline = EditorApplication.timeSinceStartup + 120;
            sequence = Scenario(); EditorApplication.update += Tick;
        }
        private static void RecordError(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message); }
        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Status HUD validation timed out");
                EditorApplication.QueuePlayerLoopUpdate();
                if (sequence.MoveNext()) return;
                Check(Errors.Count == 0, "No runtime errors: " + string.Join("; ", Errors));
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= RecordError;
            (sequence as IDisposable)?.Dispose(); sequence = null;
            Application.runInBackground = originalBackground;
            Succeeded = error == null;
            Report.Add(Succeeded ? "STATUS_HUD_VALIDATION_PASSED" : error.ToString());
            File.WriteAllLines(Path.Combine(Output, "play-report.txt"), Report);
            if (Succeeded) Debug.Log("STATUS_HUD_VALIDATION_PASSED: " + (Report.Count - 1) + " checks.");
            else Debug.LogException(error);
            EditorApplication.ExitPlaymode();
        }
        private static void Check(bool value, string message)
        { if (!value) throw new Exception(message); Report.Add("PASS " + message); }
        private static IEnumerator WaitSimulation(double seconds)
        {
            double until = context.Round.Time + seconds;
            while (context.Round.Time < until) yield return null;
            yield return null;
        }
        private static IEnumerator Scenario()
        {
            game = UnityEngine.Object.FindObjectsOfType<MarbleGameController>().Single();
            hud = UnityEngine.Object.FindObjectsOfType<ScoreListener>().Single();
            while (!game.IsReady) yield return null;
            simulation = (MarbleSimulation)typeof(MarbleGameController).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            context = simulation.Context;
            long tick = context.Round.Tick;
            var wait = WaitSimulation(.1); while (wait.MoveNext()) yield return null;
            Check(context.Round.Tick > tick, "Real scene simulation advances");
            Check(hud.StatusHud && hud.RushSlider && hud.ScoreStackSlider && hud.RushText && hud.ScoreStackText, "Both HUD rows are wired");
            Check(hud.StatusHud.GetComponentInParent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "HUD is a screen overlay");
            var round = context.Round; round.RushMeter = 50; context.Round = round;
            yield return null; yield return null;
            Check(Math.Abs(hud.RushSlider.normalizedValue - .5f) < .001, "Inactive RUSH displays charging progress");
            Check(hud.ScoreStackSlider.value == 0, "Idle stack timer is empty");
            game.FireOnce(); while (context.Marbles.Count == 0) yield return null;
            var key = context.Marbles.Keys.Single(); var ball = context.Marbles[key];
            var rush = context.Manager.GetComponentData<MarbleRush>(ball); rush.DurationBonus = 5; context.Manager.SetComponentData(ball, rush);
            round = context.Round; round.RushMeter = 99; context.Round = round;
            simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 990, Multiplier = 1, ScoreStackEnabled = true });
            while (game.Snapshot.RushSecondsRemaining == 0) yield return null;
            game.SetPaused(true); yield return null; yield return null;
            Check(game.Snapshot.RushDurationSeconds == 7, "Extended RUSH records seven seconds");
            Check(hud.RushText.text.Contains("锁血") && hud.RushSlider.value > .95f, "RUSH switches from charging to a full countdown");
            Check(hud.ScoreStackText.text.Contains("+0.05") && hud.ScoreStackSlider.value > .9f, "First stack shows bonus and a full timer");
            float rushStart = hud.RushSlider.value, stackStart = hud.ScoreStackSlider.value;
            double wall = EditorApplication.timeSinceStartup + .2;
            while (EditorApplication.timeSinceStartup < wall) yield return null;
            Check(hud.RushSlider.value == rushStart && hud.ScoreStackSlider.value == stackStart, "Pause freezes both progress bars");
            hud.enabled = false; yield return null;
            Check(hud.StatusHud.alpha == 0, "Disabling the listener hides stale HUD");
            hud.enabled = true; yield return null;
            Check(hud.StatusHud.alpha == 1 && Math.Abs(hud.RushSlider.value - rushStart) < .001, "Re-enabled HUD reads the real remaining duration");
            game.SetPaused(false);
            wait = WaitSimulation(.2); while (wait.MoveNext()) yield return null;
            Check(hud.RushSlider.value < rushStart && hud.ScoreStackSlider.value < stackStart, "Both bars shrink while time advances");
            game.FireOnce(); while (context.Marbles.Count == 0) yield return null;
            key = context.Marbles.Keys.Single();
            simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 991, Multiplier = 1, ScoreStackEnabled = true });
            while (game.Snapshot.ScoreStackBonus < .1 - 1e-6) yield return null;
            game.SetPaused(true); yield return null; yield return null;
            Check(Math.Abs(game.Snapshot.ScoreStackWindowSeconds - 1 / 1.05) < .0001 && hud.ScoreStackSlider.value > .9f,
                "New score refills the bar using its shorter window");
            Check(Math.Abs(hud.RushSlider.value - game.Snapshot.RushSecondsRemaining / 7) < .001, "RUSH progress divides by actual activation length");
            Check(Math.Abs(hud.ScoreStackSlider.value - game.Snapshot.ScoreStackSecondsRemaining / game.Snapshot.ScoreStackWindowSeconds) < .001,
                "Score progress divides by the current refreshed window");
            foreach (var size in new[] { new Vector2Int(1280,720), new Vector2Int(1920,1080), new Vector2Int(3840,2160), new Vector2Int(1280,960), new Vector2Int(2560,1080) })
                using (var view = new ResolutionView(Camera.main, size.x, size.y))
                {
                    yield return null;
                    view.CheckLayout(hud);
                    if (size.x == 1920) view.Capture(Path.Combine(Output, "status-bars-1920x1080.png"));
                }
            game.SetPaused(false);
            wait = WaitSimulation(1.1); while (wait.MoveNext()) yield return null;
            Check(hud.ScoreStackSlider.value == 0 && hud.ScoreStackText.text.Contains("+0.00"), "Expired stack returns to empty");
            Check(hud.RushSlider.value > 0 && hud.RushText.text.Contains("锁血"), "RUSH continues independently after score expiry");
            round = context.Round; round.RushRemaining = .02; context.Round = round;
            wait = WaitSimulation(.1); while (wait.MoveNext()) yield return null;
            Check(hud.RushSlider.value == 0 && hud.RushText.text.Contains("/ 100"), "RUSH expiry returns to the accumulation meter");
            game.RestartRound(); yield return null; yield return null;
            Check(hud.RushSlider.value == 0 && hud.ScoreStackSlider.value == 0, "Restart clears both bars");
            round = context.Round; round.Phase = (byte)RoundPhase.Build; context.Round = round;
            yield return null; yield return null;
            Check(hud.StatusHud.alpha == 0, "Non-playing phases hide the status panel");
            hud.Controller = null; yield return null;
            Check(hud.StatusHud.alpha == 0 && hud.RushSlider.value == 0 && hud.ScoreStackSlider.value == 0, "Missing controller leaves no stale progress");
            hud.Controller = game;
        }

        private sealed class ResolutionView : IDisposable
        {
            private readonly Camera camera;
            private readonly RenderTexture previousTarget, target;
            private readonly float previousAspect, previousFov;
            private readonly Rect previousRect;
            private readonly Canvas[] overlays;
            private readonly Camera[] cameras;
            private readonly float[] distances;
            internal ResolutionView(Camera camera, int width, int height)
            {
                this.camera = camera;
                previousTarget = camera.targetTexture; previousAspect = camera.aspect; previousFov = camera.fieldOfView; previousRect = camera.rect;
                overlays = UnityEngine.Object.FindObjectsOfType<Canvas>(true).Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
                cameras = overlays.Select(c => c.worldCamera).ToArray(); distances = overlays.Select(c => c.planeDistance).ToArray();
                target = new RenderTexture(width, height, 24); target.Create();
                camera.targetTexture = target; camera.rect = new Rect(0,0,1,1); camera.aspect = (float)width / height;
                foreach (var canvas in overlays) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                Canvas.ForceUpdateCanvases();
            }
            internal void CheckLayout(ScoreListener listener)
            {
                Canvas.ForceUpdateCanvases(); camera.Render(); Canvas.ForceUpdateCanvases();
                string size = target.width + "x" + target.height;
                var panel = (RectTransform)listener.StatusHud.transform;
                var rect = Bounds(panel);
                Check(rect.xMin > target.width / 2 && rect.yMin > target.height / 2 && rect.xMax <= target.width + 1 && rect.yMax <= target.height + 1,
                    size + " panel is inside the top-right corner");
                var button = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>(true).First(b => b.name == "打开道具库");
                Check(!rect.Overlaps(Bounds((RectTransform)button.transform)), size + " panel avoids the item-library button");
                Check(listener.RushSlider.transform.position.y > listener.ScoreStackSlider.transform.position.y, size + " RUSH row sits above score row");
                foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.ForceMeshUpdate();
                    Check(!text.isTextOverflowing, size + " text fits: " + text.name);
                    Check(text.text.All(c => char.IsWhiteSpace(c) || text.font.HasCharacter(c, true, true)), size + " font covers all label characters");
                }
                Check(panel.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g => !g.raycastTarget), size + " HUD does not capture input");
            }
            private Rect Bounds(RectTransform rect)
            {
                var points = new Vector3[4]; rect.GetWorldCorners(points);
                var screen = points.Select(p => (Vector2)camera.WorldToScreenPoint(p)).ToArray();
                return Rect.MinMaxRect(screen.Min(p => p.x), screen.Min(p => p.y), screen.Max(p => p.x), screen.Max(p => p.y));
            }
            internal void Capture(string path)
            {
                camera.Render(); var previous = RenderTexture.active;
                var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                try { RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
            }
            public void Dispose()
            {
                for (int i=0;i<overlays.Length;i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = distances[i]; }
                camera.targetTexture = previousTarget; camera.rect = previousRect; camera.aspect = previousAspect; camera.fieldOfView = previousFov;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
