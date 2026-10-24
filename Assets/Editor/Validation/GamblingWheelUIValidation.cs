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
    public static class GamblingWheelUIValidation
    {
        private const string Pending = "GamblingWheelUIValidation.Pending";
        private static readonly List<string> Report = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static IEnumerator sequence;
        private static double deadline;
        private static bool originalBackground;
        private static MarbleGameController game;
        private static MarbleSimulation simulation;
        private static SimulationContext context;
        private static GamblingWheelDisplay view;
        public static bool Succeeded { get; private set; }
        private static string Output => Environment.GetEnvironmentVariable("MARBLE_GAMBLING_WHEEL_VALIDATION_OUTPUT") ??
            Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/gambling-wheel"));

        static GamblingWheelUIValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            };
        }

        [MenuItem("Marbles ECS/UI/验证赌博转盘")]
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
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Gambling wheel validation timed out");
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
            Report.Add(Succeeded ? "GAMBLING_WHEEL_UI_PASSED" : error.ToString());
            File.WriteAllLines(Path.Combine(Output, "play-report.txt"), Report);
            if (Succeeded) Debug.Log("GAMBLING_WHEEL_UI_PASSED: " + (Report.Count - 1) + " checks.");
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
            view = UnityEngine.Object.FindObjectsOfType<GamblingWheelDisplay>().Single();
            while (!game.IsReady) yield return null;
            simulation = (MarbleSimulation)typeof(MarbleGameController).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            context = simulation.Context;
            long tick = context.Round.Tick;
            var rotation = view.Rotor.localRotation;
            uint rng = context.Round.RandomState;
            var wait = WaitSimulation(.2); while (wait.MoveNext()) yield return null;
            Check(context.Round.Tick > tick, "Real scene simulation advances");
            Check(Quaternion.Angle(rotation, view.Rotor.localRotation) > 1, "Wheel model continuously rotates");
            Check(context.Round.RandomState == rng, "Visual spinning does not consume random draws");
            Check(view.ModelImage.texture && view.ModelCamera.targetTexture == view.ModelImage.texture, "Real 3D model is wired to the screen image");
            Check(view.Sectors.Length == 8 && view.Sectors.All(x => x && x.GetComponent<MeshFilter>().sharedMesh), "All eight modeled sectors exist");
            Check(view.Rotor.GetComponentsInChildren<Collider>(true).Length == 0, "Decorative model has no gameplay collisions");
            var labels = Resources.FindObjectsOfTypeAll<TMP_Text>().Where(t => t.gameObject.scene.IsValid()).ToArray();
            Check(!labels.Any(t => t.text.Contains("PENDING /") || t.text == "Pending Score:" || t.name == "RandomScore"), "Old gambling/pending UI removed, including inactive objects");
            Check(!Resources.FindObjectsOfTypeAll<MonoBehaviour>().Any(c => c && c.gameObject.scene.IsValid() && c.GetType().Name == "RandomScoreResultUI"), "Old result presenter is absent");
            game.FireOnce(); while (context.Marbles.Count == 0) yield return null;
            var key = context.Marbles.Keys.Single();
            simulation.EnqueueContact(new MarbleContact { Key=key,Kind=MarbleContactKind.RandomScore,TargetId=990,Multiplier=1,ScoreStackEnabled=true });
            while (game.GamblingWheel.BallCount == 0) yield return null;
            game.SetPaused(true); yield return null; yield return null;
            Check(view.DisplayGroup.alpha == 1 && !view.DisplayGroup.blocksRaycasts, "Wheel display visible without capturing clicks");
            Check(view.PoolText.text.Contains("1 球") && view.TimerBar.value > .9f, "Actual zone entry updates count, pool and timer");
            Check(game.Snapshot.ScoreStackBonus > 0, "Gambling entry still activates the existing score stack");
            double remaining = game.GamblingWheel.SecondsRemaining;
            rotation = view.Rotor.localRotation; rng=context.Round.RandomState;
            double wall = EditorApplication.timeSinceStartup + .2;
            while (EditorApplication.timeSinceStartup < wall) yield return null;
            Check(game.GamblingWheel.SecondsRemaining == remaining && Quaternion.Angle(rotation,view.Rotor.localRotation) < .001, "Pause freezes both countdown and model");
            Check(context.Round.RandomState == rng, "Paused display consumes no randomness");
            view.enabled=false; yield return null;
            Check(view.DisplayGroup.alpha==0 && view.ModelImage.texture==null && view.ModelCamera.targetTexture==null, "Disabling display releases its render target");
            view.enabled=true; yield return null; yield return null;
            Check(view.DisplayGroup.alpha==1 && view.ModelImage.texture && game.GamblingWheel.SecondsRemaining==remaining, "Re-enabling display preserves gameplay state");
            game.SetPaused(false);
            wait=WaitSimulation(.4);while(wait.MoveNext())yield return null;
            game.FireOnce();while(context.Marbles.Count==0)yield return null;
            key=context.Marbles.Keys.Single();
            simulation.EnqueueContact(new MarbleContact {Key=key,Kind=MarbleContactKind.RandomScore,TargetId=991,Multiplier=1});
            while(game.GamblingWheel.BallCount<2)yield return null;
            game.SetPaused(true);yield return null;yield return null;
            Check(Math.Abs(game.GamblingWheel.WindowSeconds-Math.Sqrt(2))<.0001 && view.TimerBar.value>.9f, "Next entry refills timer with shorter duration");
            foreach (var size in new[] { new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(3840,2160),new Vector2Int(1280,960),new Vector2Int(2560,1080) })
                using(var resolution=new ResolutionView(Camera.main,size.x,size.y))
                {
                    yield return null;
                    resolution.CheckLayout(view);
                    if(size.x==1920)resolution.Capture(Path.Combine(Output,"wheel-1920x1080.png"));
                }
            var target=view.ModelCamera.targetTexture;
            var old=RenderTexture.active;
            var preview=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try
            {
                view.ModelCamera.Render(); RenderTexture.active=target;
                preview.ReadPixels(new Rect(0,0,target.width,target.height),0,0);preview.Apply();
                Check(preview.GetPixels32().Select(c => (int)c.r<<16 | (int)c.g<<8 | c.b).Distinct().Count()>20,"Preview contains rendered geometry and text");
                File.WriteAllBytes(Path.Combine(Output,"wheel-model.png"),preview.EncodeToPNG());
            }
            finally { RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(preview); }
            game.SetPaused(false);
            while(game.GamblingWheel.DrawCount==0)yield return null;
            yield return null;
            Check(game.GamblingWheel.BallCount==0 && game.GamblingWheel.PoolValue==0,"Timeout clears current pool in live scene");
            Check(view.ResultText.text.Contains("×"+game.GamblingWheel.LastMultiplier) && view.TimerBar.value==0,"UI displays actual result immediately");
            Check(game.Session.Phase==RoundPhase.Playing,"Drawing does not interrupt firing phase");
            // A zero stake still draws, allowing deterministic transition checks regardless of outcome.
            var round=context.Round; round.Score=round.TargetScore; context.Round=round;
            GamblingWheelSystem.Deposit(context,0);
            long draws=game.GamblingWheel.DrawCount;
            Check(game.CashOut(),"Physical controller can end the round with a pending pool");
            while(game.Session.Phase==RoundPhase.Draining)yield return null;
            yield return null;
            Check(game.Session.Phase==RoundPhase.Shop && game.GamblingWheel.DrawCount==draws+1,"Round end resolves pool before entering shop");
            Check(view.DisplayGroup.alpha==0 && !view.ModelCamera.enabled,"Shop hides wheel and stops preview rendering");
            game.RestartRound();yield return null;yield return null;
            Check(game.GamblingWheel.DrawCount==0 && game.GamblingWheel.BallCount==0 && view.TimerBar.value==0,"Restart clears pool, result and timer");
            Check(view.DisplayGroup.alpha==1 && view.ResultText.text.Contains("50"),"New run shows idle wheel");
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
            internal void CheckLayout(GamblingWheelDisplay view)
            {
                Canvas.ForceUpdateCanvases();view.ModelCamera.Render();camera.Render();Canvas.ForceUpdateCanvases();
                string size=target.width+"x"+target.height;
                var panel=(RectTransform)view.DisplayGroup.transform;
                var rect=Bounds(panel);
                Check(rect.xMin>=0 && rect.xMax<target.width/2 && rect.yMax<=target.height+1 && rect.center.y>target.height/2 && rect.yMin>=0,size+" wheel fits the upper-left corner");
                var bar = Bounds((RectTransform)view.TimerBar.transform);
                var fill = Bounds(view.TimerBar.fillRect);
                Check(fill.xMin>=bar.xMin-1 && fill.xMax<=bar.xMax+1 && fill.yMin>=bar.yMin-1 && fill.yMax<=bar.yMax+1,
                    size+" timer fill stays inside its track");
                var status=UnityEngine.Object.FindObjectOfType<ScoreListener>();
                Check(!rect.Overlaps(Bounds((RectTransform)status.StatusHud.transform)),size+" wheel does not overlap the RUSH/score HUD");
                foreach(var text in panel.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.ForceMeshUpdate();
                    Check(!text.isTextOverflowing,size+" label fits: "+text.name);
                    Check(text.text.All(c=>char.IsWhiteSpace(c)||text.font.HasCharacter(c,true,true)),size+" font covers all characters: "+text.name);
                }
                Check(panel.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g=>!g.raycastTarget),size+" wheel never intercepts pointer input");
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
