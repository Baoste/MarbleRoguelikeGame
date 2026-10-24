using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MarblesECS.Editor
{
    /// <summary>Opt-in runtime check. Never opens/saves a scene or exits the Editor.</summary>
    [InitializeOnLoad]
    public static class CameraNozzleValidation
    {
        private const string PendingKey = "CameraNozzleValidation.Pending";
        private const string ScenePath = "Assets/Scenes/TestScene.unity";
        private const string NozzleName = "原模型喷嘴_随发射轨道";
        private static string Work => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../.utmp/camera_nozzle_20261021"));
        private static readonly List<string> Report = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static IEnumerator sequence;
        private static double deadline;
        private static MarbleGameController game;
        private static DiegeticMachineUI rig;
        private static Transform nozzle;
        private static Renderer nozzleRenderer;
        private static Vector3 nozzleLocalPosition, nozzleLocalScale;
        private static Quaternion nozzleLocalRotation;
        private static bool hostSettingsCaptured, originalRunInBackground;
        private static readonly FieldInfo SlideField = typeof(DiegeticMachineUI)
            .GetField("slide", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsRunning => sequence != null || SessionState.GetBool(PendingKey, false);
        public static bool Succeeded { get; private set; }

        static CameraNozzleValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
                {
                    SessionState.SetBool(PendingKey, false);
                    EditorApplication.delayCall += Start;
                }
                else if (state == PlayModeStateChange.ExitingPlayMode && sequence != null)
                    Finish(new OperationCanceledException("Play Mode stopped before camera/nozzle checks completed."));
            };
        }

        [MenuItem("Marbles ECS/新机子/验证左侧板与移动喷嘴")]
        public static void Run()
        {
            if (IsRunning || NewMachineFlowValidation.IsRunning || ItemLibraryValidation.IsRunning)
                throw new InvalidOperationException("Another focused validation is running.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open TestScene before running this validation.");
            Directory.CreateDirectory(Work);
            File.WriteAllText(Path.Combine(Work, "validation.txt"), "RUNNING: camera/nozzle validation\n");
            Succeeded = false;
            if (EditorApplication.isPlaying) { Start(); return; }
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void Start()
        {
            if (sequence != null) return;
            Report.Clear(); Errors.Clear();
            game = null; rig = null; nozzle = null; nozzleRenderer = null;
            originalRunInBackground = Application.runInBackground;
            hostSettingsCaptured = true;
            Report.Add("HOST before validation: " + RuntimeContext());
            // The command bridge continues ticking while an unfocused Editor can stop the
            // player loop. This is test-host state only, restored before leaving Play Mode.
            Application.runInBackground = true;
            Report.Add("HOST validation enables runtime runInBackground; project settings are unchanged.");
            deadline = EditorApplication.timeSinceStartup + 120;
            Application.logMessageReceived += RecordError;
            sequence = Scenario();
            EditorApplication.update += Tick;
        }

        private static void RecordError(string text, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Errors.Add(text);
        }

        private static void Tick()
        {
            try
            {
                Require(EditorApplication.isPlaying, "Play Mode ended during validation.");
                Require(EditorApplication.timeSinceStartup <= deadline, "Camera/nozzle validation timed out.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (sequence.MoveNext()) return;
                Check(Errors.Count == 0, "No runtime errors: " + string.Join("; ", Errors));
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= RecordError;
            Report.Add("FINAL STATE " + RuntimeContext());
            var previous = sequence; sequence = null;
            SessionState.SetBool(PendingKey, false);
            try { (previous as IDisposable)?.Dispose(); }
            catch (Exception cleanup) { if (error == null) error = cleanup; }
            if (hostSettingsCaptured)
            {
                Application.runInBackground = originalRunInBackground;
                hostSettingsCaptured = false;
                Report.Add("HOST restored runInBackground=" + Application.runInBackground);
            }
            Succeeded = error == null;
            Report.Add(error == null ? "CAMERA_NOZZLE_VALIDATION_PASSED" : "FAILED\n" + error);
            Directory.CreateDirectory(Work);
            File.WriteAllLines(Path.Combine(Work, "validation.txt"), Report);
            if (error == null) Debug.Log("CAMERA_NOZZLE_VALIDATION_PASSED: " + Work);
            else Debug.LogError("CAMERA_NOZZLE_VALIDATION_FAILED: " + error.Message + "\n" + Work);
            // Leave the user's Editor and command bridge alive. Play-only mutations are discarded.
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static void Check(bool condition, string message)
        { Require(condition, message); Report.Add("PASS " + message); }
        private static IEnumerator WaitFor(Func<bool> condition, string message, double seconds = 12)
        {
            double started = EditorApplication.timeSinceStartup;
            double end = started + seconds, nextReport = started + 1;
            int firstFrame = Time.frameCount;
            float firstTime = Time.time, firstUnscaled = Time.unscaledTime;
            Report.Add("WAIT START " + message + " | " + RuntimeContext());
            while (!condition())
            {
                double now = EditorApplication.timeSinceStartup;
                string progress = "wall=" + (now - started).ToString("F3") +
                    "s playerFrames=" + (Time.frameCount - firstFrame) +
                    " gameTimeAdvanced=" + (Time.time - firstTime).ToString("F3") +
                    " unscaledTimeAdvanced=" + (Time.unscaledTime - firstUnscaled).ToString("F3");
                if (now > end)
                {
                    Report.Add("WAIT TIMEOUT " + message + " | " + progress + " | " + RuntimeContext());
                    string diagnosis = Time.frameCount == firstFrame || Time.unscaledTime <= firstUnscaled
                        ? " Player-loop stall: the Editor bridge ticked without advancing runtime frames."
                        : " Runtime frames advanced: inspect phase, slide and component activity below.";
                    throw new InvalidOperationException("Timed out: " + message + ". " + progress +
                        diagnosis + "\n" + RuntimeContext());
                }
                if (now >= nextReport)
                {
                    Report.Add("WAIT PROGRESS " + message + " | " + progress + " | " + RuntimeContext());
                    nextReport = now + 1;
                }
                yield return null;
            }
            Report.Add("WAIT COMPLETE " + message + " | wall=" +
                (EditorApplication.timeSinceStartup - started).ToString("F3") +
                "s playerFrames=" + (Time.frameCount - firstFrame) + " | " + RuntimeContext());
            Check(true, message);
        }

        private static string RuntimeContext()
        {
            string state = "phase=" + (game && game.IsReady ? game.Session.Phase.ToString() : "not-ready") +
                " controllerPaused=" + (game ? game.Paused.ToString() : "n/a") +
                " time=" + Time.time.ToString("F4") + " unscaledTime=" + Time.unscaledTime.ToString("F4") +
                " timeScale=" + Time.timeScale.ToString("F3") + " deltaTime=" + Time.deltaTime.ToString("F5") +
                " unscaledDeltaTime=" + Time.unscaledDeltaTime.ToString("F5") + " frame=" + Time.frameCount +
                " runInBackground=" + Application.runInBackground + " appFocused=" + Application.isFocused +
                " editorPaused=" + EditorApplication.isPaused +
                " focusedWindow=" + (EditorWindow.focusedWindow ? EditorWindow.focusedWindow.titleContent.text : "none");
            if (game) state += " gameActive=" + game.isActiveAndEnabled;
            if (rig)
            {
                state += " rigActive=" + rig.isActiveAndEnabled + " slide=" +
                    (SlideField != null ? ((float)SlideField.GetValue(rig)).ToString("F5") : "unknown") +
                    " slideSeconds=" + rig.SlideSeconds.ToString("F3");
                if (rig.ItemBoard) state += " board=" + rig.ItemBoard.localPosition.ToString("F5") +
                    " closed=" + rig.BoardClosedPosition.ToString("F5") +
                    " closedDistance=" + Vector3.Distance(rig.ItemBoard.localPosition, rig.BoardClosedPosition).ToString("F5");
                if (rig.ItemGroup) state += " alpha=" + rig.ItemGroup.alpha.ToString("F4") +
                    " blocks=" + rig.ItemGroup.blocksRaycasts + " interactable=" + rig.ItemGroup.interactable;
                if (rig.Continue) state += " continueInteractable=" + rig.Continue.IsInteractable();
            }
            return state;
        }

        private static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

        private sealed class ResolutionScope : IDisposable
        {
            private readonly Camera camera;
            private readonly RenderTexture previousTarget;
            private readonly Rect previousRect;
            private readonly float previousAspect, previousFov;
            private readonly Canvas[] overlays;
            private readonly Camera[] previousCameras;
            private readonly float[] previousDistances;
            private RenderTexture target;
            public int Width => target.width;
            public int Height => target.height;

            public ResolutionScope(Camera camera, int width)
            {
                this.camera = camera;
                previousTarget = camera.targetTexture; previousRect = camera.rect;
                previousAspect = camera.aspect; previousFov = camera.fieldOfView;
                overlays = UnityEngine.Object.FindObjectsOfType<Canvas>(true)
                    .Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
                previousCameras = overlays.Select(c => c.worldCamera).ToArray();
                previousDistances = overlays.Select(c => c.planeDistance).ToArray();
                try
                {
                    target = new RenderTexture(width, width * 9 / 16, 24) { name = "CameraNozzleValidation_RT" };
                    Require(target.Create(), "Could not create target-sized render texture.");
                    camera.targetTexture = target; camera.rect = new Rect(0, 0, 1, 1);
                    camera.aspect = 16f / 9f;
                    camera.GetComponent<MachineCameraAspect>()?.Apply();
                    foreach (var canvas in overlays)
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = Mathf.Max(1, camera.nearClipPlane + .1f);
                    }
                    Canvas.ForceUpdateCanvases(); camera.Render(); Canvas.ForceUpdateCanvases();
                }
                catch { Dispose(); throw; }
            }

            public void Validate()
            {
                Canvas.ForceUpdateCanvases();
                Check(camera.targetTexture == target && Mathf.Abs(camera.aspect - 16f / 9f) < .001f,
                    Width + "x" + Height + " uses its actual RT and 16:9 camera aspect");
                foreach (var canvas in overlays.Where(c => c && c.isActiveAndEnabled && c.isRootCanvas))
                    Check(Mathf.Abs(canvas.pixelRect.width - Width) < 1 && Mathf.Abs(canvas.pixelRect.height - Height) < 1,
                        "Overlay adapter preserves target pixel extent: " + canvas.name);
            }

            public void Capture(string label)
            {
                var oldActive = RenderTexture.active;
                Texture2D texture = null;
                try
                {
                    camera.GetComponent<MachineCameraAspect>()?.Apply();
                    Canvas.ForceUpdateCanvases(); camera.Render();
                    RenderTexture.active = target;
                    texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply();
                    string path = Path.Combine(Work, label + "_" + Width + ".png");
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                    Report.Add("SCREENSHOT " + path);
                }
                finally
                {
                    RenderTexture.active = oldActive;
                    if (texture) UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            public void Dispose()
            {
                if (camera)
                {
                    camera.targetTexture = previousTarget; camera.rect = previousRect;
                    camera.aspect = previousAspect; camera.fieldOfView = previousFov;
                }
                for (int i = 0; i < overlays.Length; i++)
                    if (overlays[i])
                    {
                        overlays[i].renderMode = RenderMode.ScreenSpaceOverlay;
                        overlays[i].worldCamera = previousCameras[i]; overlays[i].planeDistance = previousDistances[i];
                    }
                Canvas.ForceUpdateCanvases();
                if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null; }
            }
        }

        private static void ValidateContinueHit(ResolutionScope resolution, bool click)
        {
            Require(EventSystem.current, "No active scene EventSystem.");
            var target = rig.Continue.gameObject;
            var rect = (RectTransform)target.transform;
            Vector2 p = RectTransformUtility.WorldToScreenPoint(rig.ViewCamera, rect.TransformPoint(rect.rect.center));
            Check(p.x >= 0 && p.x < resolution.Width && p.y >= 0 && p.y < resolution.Height,
                resolution.Width + "px Continue lies on screen at " + p);
            Check(rig.ItemGroup.blocksRaycasts && rig.ItemGroup.interactable && rig.Continue.IsInteractable(),
                resolution.Width + "px expanded left-board controls are interactive");
            var pointer = new PointerEventData(EventSystem.current)
            { position = p, pressPosition = p, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Report.Add("RAYCAST " + resolution.Width + ": " + string.Join(" > ", hits.Select(h => PathOf(h.gameObject.transform))));
            Check(hits.Count > 0 && (hits[0].gameObject == target || hits[0].gameObject.transform.IsChildOf(target.transform)),
                resolution.Width + "px top UI raycast reaches the actual Continue control");
            if (!click) return;
            pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hits[0];
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Check(game.Session.Phase == RoundPhase.Playing && !game.Paused,
                resolution.Width + "px actual Continue click starts gameplay");
            Report.Add("AFTER CONTINUE " + RuntimeContext());
        }

        private static void ValidateNozzleAttachment()
        {
            Require(nozzle && nozzle.parent == game.LaunchPoint, "Original nozzle detached from the actual launch point.");
            Require((nozzle.localPosition - nozzleLocalPosition).sqrMagnitude < .000001f &&
                Quaternion.Angle(nozzle.localRotation, nozzleLocalRotation) < .01f &&
                (nozzle.localScale - nozzleLocalScale).sqrMagnitude < .000001f,
                "Nozzle authored attachment pose changed during movement.");
            Require((game.LaunchPoint.localPosition - game.LauncherLocalPosition).sqrMagnitude < .000001f,
                "Physics launch Transform is out of sync with production launcher movement.");
            Require(nozzleRenderer && nozzleRenderer.enabled && nozzleRenderer.gameObject.activeInHierarchy,
                "Original nozzle renderer is no longer visible/enabled.");
            Require(!game.RequiresRestart, "Gameplay reported a runtime simulation failure.");
        }

        private static IEnumerator ObserveMovement(double seconds, bool expectMovement, string label)
        {
            Vector3 launchStart = game.LaunchPoint.position;
            Vector3 nozzleStart = nozzle.position;
            Vector3 visibleStart = nozzleRenderer.bounds.center;
            float travelled = 0;
            int samples = 0;
            int firstFrame = Time.frameCount;
            float firstUnscaled = Time.unscaledTime;
            double end = EditorApplication.timeSinceStartup + seconds;
            Report.Add("MOTION START " + label + " | " + RuntimeContext());
            while (EditorApplication.timeSinceStartup < end)
            {
                ValidateNozzleAttachment();
                Vector3 launchDelta = game.LaunchPoint.position - launchStart;
                Require((nozzle.position - nozzleStart - launchDelta).sqrMagnitude < .00001f &&
                    (nozzleRenderer.bounds.center - visibleStart - launchDelta).sqrMagnitude < .00001f,
                    "Original nozzle visual does not travel with the actual physics launcher.");
                travelled = Mathf.Max(travelled, launchDelta.magnitude);
                if (!expectMovement) Require(travelled < .0001f, "Paused launcher/nozzle moved.");
                samples++;
                yield return null;
            }
            Report.Add("MOTION END " + label + " | " + RuntimeContext());
            Check(samples > 1 && Time.frameCount > firstFrame && Time.unscaledTime > firstUnscaled &&
                (expectMovement ? travelled > .05f : travelled < .0001f),
                label + ": samples=" + samples + ", playerFrames=" + (Time.frameCount - firstFrame) +
                ", unscaledTimeAdvanced=" + (Time.unscaledTime - firstUnscaled).ToString("F3") +
                ", max travel=" + travelled.ToString("F5"));
        }

        private static void ValidateBloodDisplay()
        {
            var listener = UnityEngine.Object.FindObjectsOfType<ScoreListener>()
                .Single(s => s.Controller == game);
            Require(rig.BloodBar && listener.BloodSlider == rig.BloodBar,
                "ScoreListener does not drive the physical header blood bar.");
            Require(Mathf.Abs(rig.BloodBar.value - game.Snapshot.Blood) < .001f,
                "Blood display mismatch: slider=" + rig.BloodBar.value.ToString("F4") +
                " snapshot=" + game.Snapshot.Blood.ToString("F4"));
            Require(!game.RequiresRestart, "Actual firing reported a runtime simulation failure.");
        }

        private static IEnumerator FireWhileMoving()
        {
            const int shotCount = 3;
            Require(game.Session.Phase == RoundPhase.Playing && !game.Paused,
                "Actual firing requires unpaused Playing.");
            ValidateBloodDisplay();
            var seen = new HashSet<string>();
            var existing = new HashSet<string>(UnityEngine.Object.FindObjectsOfType<MarbleBody>()
                .Where(b => b.gameObject.scene == game.PhysicsRoot.gameObject.scene).Select(b => b.name));
            float bloodBefore = game.Snapshot.Blood, minimumBlood = bloodBefore, maxTravel = 0;
            Vector3 launchStart = game.LaunchPoint.position, nozzleStart = nozzle.position;
            Vector3 visibleStart = nozzleRenderer.bounds.center;
            long tickBefore = game.Snapshot.Tick;
            int firstFrame = Time.frameCount;
            double end = EditorApplication.timeSinceStartup + 8;
            Report.Add("FIRING START: three production FireOnce requests; blood=" + bloodBefore.ToString("F4") +
                " tick=" + tickBefore + " | " + RuntimeContext());
            // Use the same public single-shot input as gameplay; do not spawn bodies or edit ECS data.
            for (int i = 0; i < shotCount; i++) game.FireOnce();
            do
            {
                yield return null;
                ValidateNozzleAttachment();
                ValidateBloodDisplay();
                minimumBlood = Mathf.Min(minimumBlood, game.Snapshot.Blood);
                Vector3 delta = game.LaunchPoint.position - launchStart;
                maxTravel = Mathf.Max(maxTravel, delta.magnitude);
                Require((nozzle.position - nozzleStart - delta).sqrMagnitude < .00001f &&
                    (nozzleRenderer.bounds.center - visibleStart - delta).sqrMagnitude < .00001f,
                    "The original nozzle desynchronized from its launcher while firing real marbles.");
                foreach (var body in UnityEngine.Object.FindObjectsOfType<MarbleBody>()
                    .Where(b => b.gameObject.scene == game.PhysicsRoot.gameObject.scene))
                {
                    var rigidbody = body.GetComponent<Rigidbody>();
                    Require(rigidbody && !float.IsNaN(rigidbody.position.sqrMagnitude) &&
                        !float.IsInfinity(rigidbody.position.sqrMagnitude), "Actual marble has no finite physical body.");
                    // Production names include round and sequence, and remain unique when pooled objects are reused.
                    if (!existing.Contains(body.name) && seen.Add(body.name))
                        Report.Add("ACTUAL MARBLE " + body.name + " position=" + body.transform.position.ToString("F4") +
                            " velocity=" + rigidbody.velocity.ToString("F4") + " launcher=" +
                            game.LaunchPoint.position.ToString("F4") + " blood=" + game.Snapshot.Blood.ToString("F4"));
                }
                Require(EditorApplication.timeSinceStartup <= end,
                    "Actual firing timed out: observed=" + seen.Count + "/" + shotCount +
                    " physicsTickAdvance=" + (game.Snapshot.Tick - tickBefore) + " | " + RuntimeContext());
            } while (seen.Count < shotCount || maxTravel <= .05f);
            Check(Time.frameCount > firstFrame && game.Snapshot.Tick > tickBefore && seen.Count >= shotCount &&
                minimumBlood < bloodBefore && maxTravel > .05f && !game.RequiresRestart,
                "Three real shots coexist with automatic nozzle movement: observed=" + seen.Count +
                ", playerFrames=" + (Time.frameCount - firstFrame) + ", physicsTicks=" + (game.Snapshot.Tick - tickBefore) +
                ", launchTravel=" + maxTravel.ToString("F4") + ", bloodBefore=" + bloodBefore.ToString("F4") +
                ", minimumBlood=" + minimumBlood.ToString("F4") + ", slider=" + rig.BloodBar.value.ToString("F4") +
                ", snapshotBlood=" + game.Snapshot.Blood.ToString("F4"));
        }

        private static IEnumerator Scenario()
        {
            game = UnityEngine.Object.FindObjectsOfType<MarbleGameController>()
                .Single(g => g.gameObject.scene.path == ScenePath);
            var wait = WaitFor(() => game.IsReady, "Production controller initializes");
            while (wait.MoveNext()) yield return null;
            rig = UnityEngine.Object.FindObjectsOfType<DiegeticMachineUI>().Single(r => r.Controller == game);
            Check(rig.ViewCamera && rig.ItemBoard && rig.ItemUIAnchor && rig.ItemGroup && rig.Continue,
                "Physical left-board references are assigned");
            Check(game.Session.Phase == RoundPhase.Build && !game.Paused,
                "Validation starts with the new campaign in unpaused Build");
            Check(game.AutoMoveLauncher && game.LauncherMoveHalfWidth > 0 && game.LauncherMoveSpeed > 0,
                "Production automatic launcher motion is enabled");
            nozzle = game.LaunchPoint.Find(NozzleName);
            Check(nozzle && nozzle.parent == game.LaunchPoint, "Original model nozzle is directly bound to LaunchPoint");
            nozzleRenderer = nozzle.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.enabled && r.gameObject.activeInHierarchy);
            Check(nozzleRenderer, "Bound original nozzle has an enabled renderer");
            nozzleLocalPosition = nozzle.localPosition; nozzleLocalRotation = nozzle.localRotation; nozzleLocalScale = nozzle.localScale;
            Vector3 initialLaunchPosition = game.LaunchPoint.localPosition;
            int nozzleInstance = nozzle.GetInstanceID();
            Report.Add("NOZZLE " + PathOf(nozzle) + " local=" + nozzleLocalPosition.ToString("F5") +
                " launcherInitial=" + initialLaunchPosition.ToString("F5"));
            wait = WaitFor(() => rig.IsBoardOpen && rig.ItemGroup.interactable, "Build fully extends the left item board");
            while (wait.MoveNext()) yield return null;
            Check((rig.ItemBoard.localPosition - rig.BoardOpenPosition).sqrMagnitude < .00001f,
                "Expanded board reaches its authored open position");

            foreach (int width in new[] { 1920, 3840 })
                using (var resolution = new ResolutionScope(rig.ViewCamera, width))
                {
                    yield return null;
                    resolution.Validate();
                    Vector3 boardViewport = rig.ViewCamera.WorldToViewportPoint(rig.ItemUIAnchor.position);
                    Check(boardViewport.z > 0 && boardViewport.x >= 0 && boardViewport.x < .35f &&
                        boardViewport.y >= 0 && boardViewport.y <= 1,
                        width + "px expanded item-board anchor is on the left: " + boardViewport.ToString("F3"));
                    ValidateContinueHit(resolution, false);
                    resolution.Capture("03_build_left_board");
                    if (width == 3840) ValidateContinueHit(resolution, true);
                }

            wait = WaitFor(() => (rig.ItemBoard.localPosition - rig.BoardClosedPosition).sqrMagnitude < .00001f &&
                rig.ItemGroup.alpha < .001f && !rig.ItemGroup.blocksRaycasts && !rig.ItemGroup.interactable,
                "Playing retracts the left board and releases its UI raycasts");
            while (wait.MoveNext()) yield return null;
            var observation = ObserveMovement(.9, true, "Automatic movement carries the original nozzle mesh");
            while (observation.MoveNext()) yield return null;
            var firing = FireWhileMoving();
            while (firing.MoveNext()) yield return null;

            game.SetPaused(true);
            observation = ObserveMovement(.6, false, "Pause freezes the launcher and nozzle together");
            while (observation.MoveNext()) yield return null;
            foreach (int width in new[] { 1920, 3840 })
                using (var resolution = new ResolutionScope(rig.ViewCamera, width))
                {
                    yield return null;
                    resolution.Validate(); ValidateNozzleAttachment();
                    resolution.Capture("04_playing_nozzle");
                }
            game.SetPaused(false);
            observation = ObserveMovement(.6, true, "Resume restores actual automatic movement");
            while (observation.MoveNext()) yield return null;

            // The public restart path resets the simulation and its launcher, not just a visual transform.
            game.SetPaused(true);
            game.RestartRound();
            Check(!game.Paused && game.Session.Phase == RoundPhase.Build, "New run restores unpaused Build");
            Check((game.LaunchPoint.localPosition - initialLaunchPosition).sqrMagnitude < .000001f,
                "New run resets the actual launch point to its authored starting position");
            Check(game.LaunchPoint.Find(NozzleName) == nozzle && nozzle.GetInstanceID() == nozzleInstance,
                "New run preserves the single original nozzle attachment");
            ValidateNozzleAttachment();
            wait = WaitFor(() => rig.IsBoardOpen && rig.ItemGroup.interactable, "New run reopens the left item board");
            while (wait.MoveNext()) yield return null;
            using (var resolution = new ResolutionScope(rig.ViewCamera, 1920))
            {
                yield return null;
                resolution.Validate();
                ValidateContinueHit(resolution, true);
            }
            observation = ObserveMovement(.6, true, "New run can start and move its nozzle again");
            while (observation.MoveNext()) yield return null;
        }
    }
}
