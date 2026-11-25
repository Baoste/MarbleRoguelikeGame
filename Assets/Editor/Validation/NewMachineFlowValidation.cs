using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MarblesECS.PhysX;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MarblesECS.Editor
{
    /// <summary>Opt-in, runtime-only regression check for the migrated playable machine.</summary>
    [InitializeOnLoad]
    public static class NewMachineFlowValidation
    {
        private const string PendingKey = "NewMachineFlowValidation.Pending";
        private const string ScenePath = "Assets/Scenes/TestScene.unity";
        private static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../.utmp/playable_migration_20261020/flow_validation.txt"));
        private static readonly List<string> Report = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static IEnumerator sequence;
        private static double deadline;
        private static DiegeticMachineUI rig;
        private static MarbleGameController game;
        private static SimulationContext context;
        private static bool hostSettingsCaptured, originalRunInBackground;

        public static bool IsRunning => sequence != null || SessionState.GetBool(PendingKey, false);
        public static bool Succeeded { get; private set; }

        static NewMachineFlowValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                EditorApplication.delayCall += Start;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && sequence != null)
                Finish(new OperationCanceledException("Play Mode was stopped before validation completed."));
        }

        [MenuItem("Marbles ECS/新机子/验证新机台完整流程")]
        public static void Run()
        {
            if (IsRunning) throw new InvalidOperationException("New-machine validation is already running.");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Load TestScene first. This check never opens or saves a scene.");
            File.WriteAllText(ReportPath, "RUNNING: new-machine play-mode validation\n");
            Succeeded = false;
            if (EditorApplication.isPlaying) { Start(); return; }
            SessionState.SetBool(PendingKey, true);
            LegacyMachineValidationStartup.EnterPlayMode();
        }

        private static void Start()
        {
            if (sequence != null) return;
            Report.Clear();
            Errors.Clear();
            originalRunInBackground = Application.runInBackground;
            hostSettingsCaptured = true;
            Report.Add("HOST runInBackground before validation=" + originalRunInBackground +
                ", appFocused=" + Application.isFocused);
            // Keep actual runtime frames advancing while the Editor command bridge is unfocused.
            // This changes only the validation's Play Mode state, never PlayerSettings.
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 150;
            Application.logMessageReceived += RecordError;
            sequence = Scenario();
            EditorApplication.update += Tick;
        }

        private static void RecordError(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Errors.Add(message);
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying)
                    throw new OperationCanceledException("Play Mode ended during validation.");
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("New-machine flow timed out. Release mouse, Space and Enter.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (sequence.MoveNext()) return;
                Check(Errors.Count == 0, "No runtime errors: " + string.Join("; ", Errors));
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= RecordError;
            var previous = sequence;
            sequence = null;
            SessionState.SetBool(PendingKey, false);
            try { (previous as IDisposable)?.Dispose(); }
            catch (Exception cleanupError) { if (error == null) error = cleanupError; }
            if (hostSettingsCaptured)
            {
                Application.runInBackground = originalRunInBackground;
                hostSettingsCaptured = false;
                Report.Add("HOST restored runInBackground=" + Application.runInBackground);
            }
            Succeeded = error == null;
            Report.Add(error == null ? "NEW_MACHINE_FLOW_VALIDATION_PASSED" : "FAILED\n" + error);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllLines(ReportPath, Report);
            if (error == null) Debug.Log("NEW_MACHINE_FLOW_VALIDATION_PASSED: " + Report.Count + " checks.");
            else Debug.LogError("NEW_MACHINE_FLOW_VALIDATION_FAILED: " + error.Message + "\n" + ReportPath);
            // Keep the Editor and its command bridge alive. All temporary mutations disappear on stop.
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Report.Add("PASS " + description);
        }

        private static IEnumerator Wait(double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }

        private static IEnumerator WaitFor(Func<bool> condition, string description, double seconds = 15)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup > until)
                    throw new TimeoutException(description);
                yield return null;
            }
            Check(true, description);
        }

        private static string ObjectPath(Transform t) => t.parent ? ObjectPath(t.parent) + "/" + t.name : t.name;

        private static List<RaycastResult> Hits(Vector2 point)
        {
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            return hits;
        }

        private static Vector2 ScreenPoint(GameObject target)
        {
            var rect = target.transform as RectTransform;
            Vector3 center = rect ? rect.TransformPoint(rect.rect.center) : target.transform.position;
            var canvas = target.GetComponentInParent<Canvas>();
            var camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(camera, center);
        }

        /// <summary>Tests an actual target-sized Canvas/raycaster without changing saved scene UI.</summary>
        private sealed class TargetResolutionScope : IDisposable
        {
            private readonly Camera camera;
            private readonly RenderTexture previousTarget;
            private readonly Rect previousRect;
            private readonly float previousAspect, previousFieldOfView;
            private readonly Canvas[] overlays;
            private readonly Camera[] previousCameras;
            private readonly float[] previousDistances;
            private RenderTexture target;

            public TargetResolutionScope(Camera camera, int width)
            {
                this.camera = camera;
                previousTarget = camera.targetTexture;
                previousRect = camera.rect;
                previousAspect = camera.aspect;
                previousFieldOfView = camera.fieldOfView;
                overlays = UnityEngine.Object.FindObjectsOfType<Canvas>(true)
                    .Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
                previousCameras = overlays.Select(c => c.worldCamera).ToArray();
                previousDistances = overlays.Select(c => c.planeDistance).ToArray();
                try
                {
                    target = new RenderTexture(width, width * 9 / 16, 24);
                    target.Create();
                    camera.targetTexture = target;
                    camera.rect = new Rect(0, 0, 1, 1);
                    camera.aspect = 16f / 9f;
                    foreach (var canvas in overlays)
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = Mathf.Max(1f, camera.nearClipPlane + .1f);
                    }
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    Canvas.ForceUpdateCanvases();
                    Report.Add("TARGET_RESOLUTION " + width + "x" + target.height +
                        " (batch window " + Screen.width + "x" + Screen.height + ")");
                }
                catch { Dispose(); throw; }
            }

            public void Validate(Canvas canvas)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
                Canvas.ForceUpdateCanvases();
                Check(Mathf.Abs(canvas.pixelRect.width - target.width) < 1 &&
                    Mathf.Abs(canvas.pixelRect.height - target.height) < 1,
                    "Shop Canvas uses actual " + target.width + "x" + target.height + " pixel extent");
                var rect = (RectTransform)canvas.transform;
                Check(Mathf.Abs(rect.rect.width * canvas.scaleFactor - target.width) < 2 &&
                    Mathf.Abs(rect.rect.height * canvas.scaleFactor - target.height) < 2,
                    "Shop layout units and scale agree with target resolution");
            }

            public void Dispose()
            {
                if (camera)
                {
                    camera.targetTexture = previousTarget;
                    camera.rect = previousRect;
                    camera.aspect = previousAspect;
                    camera.fieldOfView = previousFieldOfView;
                }
                for (int i = 0; i < overlays.Length; i++)
                {
                    if (!overlays[i]) continue;
                    overlays[i].renderMode = RenderMode.ScreenSpaceOverlay;
                    overlays[i].worldCamera = previousCameras[i];
                    overlays[i].planeDistance = previousDistances[i];
                }
                Canvas.ForceUpdateCanvases();
                if (target)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                    target = null;
                }
            }
        }

        private static PointerEventData ClickVisible(GameObject target)
        {
            Vector2 point = ScreenPoint(target);
            var canvas = target.GetComponentInParent<Canvas>();
            var eventCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var renderTarget = eventCamera ? eventCamera.targetTexture : null;
            int width = renderTarget ? renderTarget.width : Screen.width;
            int height = renderTarget ? renderTarget.height : Screen.height;
            Check(point.x >= 0 && point.x < width && point.y >= 0 && point.y < height,
                "Control is on screen: " + target.name + " at " + point);
            var hits = Hits(point);
            Report.Add("RAYCAST " + target.name + ": " +
                string.Join(" > ", hits.Select(h => ObjectPath(h.gameObject.transform))));
            Check(hits.Count > 0 && (hits[0].gameObject == target ||
                hits[0].gameObject.transform.IsChildOf(target.transform)),
                "Top pointer hit reaches " + target.name);
            var e = new PointerEventData(EventSystem.current)
            {
                position = point, pressPosition = point, button = PointerEventData.InputButton.Left,
                pointerCurrentRaycast = hits[0], pointerPressRaycast = hits[0]
            };
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, e, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, e, ExecuteEvents.pointerClickHandler);
            return e;
        }

        private static void ValidateGeometry()
        {
            var surface = game.PlacementSurface;
            Check(surface && rig.Placement.BoardRoot == surface.transform &&
                rig.Placement.PlacementPlane == surface.transform, "Placement uses the new trapezoid board frame");
            Check((surface.transform.lossyScale - Vector3.one).sqrMagnitude < .000001f,
                "New board frame has unit world scale");
            Check(surface.Length > 0 && surface.TopWidth > 0 && surface.BottomWidth > surface.TopWidth,
                "Trapezoid is narrower at the top");
            Func<Vector3, float, bool> inside = (local, radius) =>
                surface.ContainsWorldPoint(surface.transform.TransformPoint(local), radius);
            Check(inside(Vector3.zero, .1f), "Board center accepts an ordinary radius");
            Check(!inside(new Vector3(surface.Length * .5f, 0, 0), 0), "Top launch margin rejects placement");
            Check(!inside(new Vector3(-surface.Length * .5f, 0, 0), 0), "Bottom exit margin rejects placement");
            Check(!inside(new Vector3(0, 0, surface.BottomWidth), .1f), "Outside side edge rejects placement");
            float x = surface.Length * .5f - surface.TopClearance - .2f;
            float halfWidth = Mathf.Lerp(surface.BottomWidth, surface.TopWidth,
                (x + surface.Length * .5f) / surface.Length) * .5f;
            Check(!inside(new Vector3(x, 0, halfWidth - .05f), .2f),
                "Radius is rejected at the sloped side even when its center is inside");
            var pinsRoot = game.PhysicsRoot.Find("MarblePins");
            Check(pinsRoot, "Original pin root remains referenced");
            var pins = pinsRoot.GetComponentsInChildren<MarblePin>(true);
            Check(pins.Length == 16 && pins.All(p => p.gameObject.activeInHierarchy),
                "All 16 fixed pins remain active");
            Check(pins.All(p => (p.transform.localScale - Vector3.one * .2f).sqrMagnitude < .000001f),
                "All 16 fixed pins retain scale 0.2");
        }

        private static Button CardFor(int instanceId)
        {
            for (int page = 0; page <= game.GetOwnedDevices().Length / 6; page++)
            {
                rig.RefreshInventory();
                var ids = (int[])typeof(DiegeticMachineUI).GetField("deviceIds",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rig);
                int index = Array.IndexOf(ids, instanceId);
                if (index >= 0)
                {
                    var button = rig.DeviceButtons[index];
                    Check(button.gameObject.activeInHierarchy && button.IsInteractable(),
                        "Inventory card is active and interactable for #" + instanceId);
                    return button;
                }
                rig.ChangeDevicePage(1);
            }
            throw new InvalidOperationException("No physical inventory card for #" + instanceId);
        }

        private static bool FindFreePoint(OwnedDeviceSnapshot device, out Vector2 pointer)
        {
            var surface = game.PlacementSurface;
            var frame = rig.Placement.BoardRoot;
            var overlaps = new Collider[256];
            float radius = device.Radius + game.PlacementClearance;
            var owned = game.GetOwnedDevices();
            var definition = game.Balance.Devices.Single(d => d.Id == device.DefinitionId);
            Physics.SyncTransforms();
            for (float x = -surface.Length * .5f + surface.BottomClearance + radius + .1f;
                 x < surface.Length * .5f - surface.TopClearance - radius; x += .28f)
            for (float z = -surface.BottomWidth * .5f + radius; z < surface.BottomWidth * .5f - radius; z += .28f)
            {
                Vector3 world = frame.TransformPoint(new Vector3(x, rig.Placement.DevicePlaneDistance, z));
                if (!surface.ContainsWorldPoint(world, radius)) continue;
                if (owned.Any(d => {
                    if (!d.Placed || d.InstanceId == device.InstanceId) return false;
                    var other = game.Balance.Devices.Single(otherDefinition => otherDefinition.Id == d.DefinitionId);
                    float clearance = !string.IsNullOrEmpty(definition.LinkGroup) &&
                        other.LinkGroup == definition.LinkGroup ? definition.GroupClearance : game.PlacementClearance;
                    return Vector2.Distance(new Vector2(x, z), new Vector2(d.X, d.Z)) <
                        device.Radius + d.Radius + clearance + .12f;
                })) continue;
                int count = frame.gameObject.scene.GetPhysicsScene().OverlapSphere(world, radius, overlaps,
                    ~0, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length || overlaps.Take(count).Any(c => game.PlacementObstacleRoots.Any(
                    root => root && c.transform.IsChildOf(root)))) continue;
                Vector3 screen = rig.ViewCamera.WorldToScreenPoint(world - frame.up * rig.Placement.DevicePlaneDistance);
                if (screen.z <= 0 || screen.x < 12 || screen.x > Screen.width - 12 ||
                    screen.y < 12 || screen.y > Screen.height - 12 || Hits(screen).Count != 0) continue;
                pointer = screen;
                return true;
            }
            pointer = default;
            return false;
        }

        private static IEnumerator PlaceFromCard(int id)
        {
            var device = game.GetOwnedDevices().Single(d => d.InstanceId == id);
            var card = CardFor(id);
            var e = ClickVisible(card.gameObject);
            Check(rig.Placement.SelectedInstanceId == 0, "Actual card click leaves placement idle");
            var invalid = rig.Placement.BoardRoot.TransformPoint(new Vector3(0, 0, game.PlacementSurface.BottomWidth));
            Check(!game.PlaceDevice(id, 0, game.PlacementSurface.BottomWidth, invalid),
                "Controller refuses a placement outside the new trapezoid");
            Check(FindFreePoint(device, out var point), "Found a visible, unobstructed new-board point for #" + id);
            // Dispatch the same drag handlers as the EventSystem; this exercises screen-to-board conversion.
            ExecuteEvents.Execute(card.gameObject, e, ExecuteEvents.beginDragHandler);
            Check(rig.Placement.IsDraggingInventory && rig.Placement.SelectedInstanceId == id, "Real card starts inventory drag for #" + id);
            e.position = point;
            e.pointerCurrentRaycast = default;
            ExecuteEvents.Execute(card.gameObject, e, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(card.gameObject, e, ExecuteEvents.endDragHandler);
            Check(game.GetOwnedDevices().Single(d => d.InstanceId == id).Placed,
                "Actual UI drag/drop places instance #" + id + ": " + rig.Placement.LastFeedback);
            Check(rig.Placement.SelectedInstanceId == 0 && !rig.Placement.IsDraggingInventory,
                "Placement preview releases input after the drop");
            var wait = WaitFor(() => UnityEngine.Object.FindObjectsOfType<DeviceBody>().Any(
                body => body.InstanceId == id && body.isActiveAndEnabled && body.transform.IsChildOf(rig.Placement.BoardRoot)),
                "Placed instance #" + id + " has its active device gameplay component");
            while (wait.MoveNext()) yield return null;
            rig.RefreshInventory();
        }

        private static void ValidateRunSeedOption()
        {
            bool original = game.RefreshRandomSeedOnNewRun;
            try
            {
                game.RefreshRandomSeedOnNewRun = false;
                game.RestartRound();
                Check(game.LastRunRandomSeed == game.Balance.Marble.RandomSeed,
                    "Unchecked seed option uses the configured run seed");
                game.RestartRound();
                Check(game.LastRunRandomSeed == game.Balance.Marble.RandomSeed &&
                    game.LastRandomStarterDeviceIds.Length == 0 && game.GetOwnedDevices().Length == 0,
                    "Unchecked option preserves empty inventory on restart");
                game.RefreshRandomSeedOnNewRun = true;
                game.RestartRound();
                uint first = game.LastRunRandomSeed;
                game.RestartRound();
                Check(first != 0 && game.LastRunRandomSeed != 0 && first != game.LastRunRandomSeed,
                    "Checked seed option generates a different nonzero seed on each new run");
                var simulation = (MarbleSimulation)typeof(MarbleGameController).GetField("simulation",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);
                Check(game.LastRandomStarterDeviceIds.Length == 0 && game.GetOwnedDevices().Length == 0 &&
                    simulation.Context.Round.RandomState == game.LastRunRandomSeed,
                    "Empty inventory consumes no starter draws from the shared stream");
                Check(game.Session.Phase == RoundPhase.Playing,
                    "Restart with empty inventory skips placement and begins Playing");
            }
            finally
            {
                game.RefreshRandomSeedOnNewRun = original;
                game.RestartRound();
            }
        }

        private static IEnumerator Scenario()
        {
            game = UnityEngine.Object.FindObjectsOfType<MarbleGameController>()
                .Single(g => g.gameObject.scene.path == ScenePath);
            var wait = WaitFor(() => game && game.IsReady, "Migrated controller initializes");
            while (wait.MoveNext()) yield return null;
            rig = UnityEngine.Object.FindObjectsOfType<DiegeticMachineUI>().Single(r => r.Controller == game);
            Check(rig.Placement && rig.Placement.Controller == game && rig.ViewCamera &&
                rig.Placement.ViewCamera == rig.ViewCamera, "Controller, physical UI and camera references agree");
            Check(EventSystem.current, "Scene supplies its own active EventSystem");
            game.EnableRandomStartingDevices = false;
            game.RestartRound();
            Check(game.Session.Phase == RoundPhase.Playing && !game.Paused,
                "Empty starting inventory enters unpaused Playing");
            Check(game.Balance.Devices.All(d => d.StartingCount == 0),
                "The empty-inventory scenario has no fixed starters");
            Check(game.GetOwnedDevices().Length == 0 && game.LastRandomStarterDeviceIds.Length == 0,
                "Initial inventory contains no gifted devices");
            ValidateGeometry();
            ValidateRunSeedOption();
            uint runSeed = game.LastRunRandomSeed;
            uint[] starterIds = game.LastRandomStarterDeviceIds;
            Check(runSeed != 0 && starterIds.Length == 0 && game.GetOwnedDevices().Length == 0,
                "Restoring the seed option still starts with empty inventory");
            var library = UnityEngine.Object.FindObjectsOfType<ItemLibraryUI>().Single(l => l.Controller == game);
            var libraryCheck = ItemLibraryValidation.Validate(library, message => Report.Add(message));
            try { while (libraryCheck.MoveNext()) yield return null; }
            finally { (libraryCheck as IDisposable)?.Dispose(); }
            Check(!library.InputBlocked && !game.Paused && rig.Placement.enabled,
                "Item-library subvalidation restores playable input");
            Check(rig.LeverPivot, "Physical lever pivot is assigned");
            wait = Wait(.4);
            while (wait.MoveNext()) yield return null;
            Quaternion playingLeverRotation = rig.LeverPivot.localRotation;
            Check(!rig.GetComponentsInChildren<DiegeticUIHit>(true).Any(hit => hit.Kind == 1 || hit.Kind == 3),
                "No legacy drug interactions remain");
            var simulation = (MarbleSimulation)typeof(MarbleGameController).GetField("simulation",
                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);
            context = simulation.Context;
            var result = UnityEngine.Object.FindObjectsOfType<GamblingWheelDisplay>().Single(r => r.Controller == game);
            var shop = UnityEngine.Object.FindObjectsOfType<ShopUI>().Single(s => s.Controller == game);
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                wait = WaitFor(() => !rig.ItemGroup.blocksRaycasts && rig.ItemGroup.alpha == 0,
                    "Playing releases item-board UI input");
                while (wait.MoveNext()) yield return null;
                var round = context.Round;
                long target = round.TargetScore;
                round.Score = target;
                // A zero-value entry exercises the draw while keeping the confirmed score unchanged.
                context.Round = round;
                GamblingWheelSystem.Deposit(context, 0);
                round = context.Round;
                Report.Add("SETTLEMENT_INPUT cycle=" + cycle + " total=" + context.Campaign.TotalScore +
                    " confirmed=" + round.Score + " pending=" + round.PendingScore + " target=" + round.TargetScore);
                Check(rig.CanCashOut(), "Physical lever permits settlement " + cycle);
                var lever = rig.LeverAnchor.GetComponentsInChildren<DiegeticUIHit>(true).First(h => h.Kind == 0);
                ClickVisible(lever.gameObject);
                wait = WaitFor(() => game.Session.Phase == RoundPhase.Build,
                    "Real settlement reaches Build " + cycle, 25);
                while (wait.MoveNext()) yield return null;
                Check(game.Session.TargetScore == target, "The next machine round uses the same target");
                wait = Wait(.4);
                while (wait.MoveNext()) yield return null;
                Quaternion buildLeverRotation = rig.LeverPivot.localRotation;
                Check(Quaternion.Angle(buildLeverRotation, playingLeverRotation) > 1f,
                    "CashOut rotates the physical lever back to its OFF position");
                Check(result.ResultText && !result.ResultText.raycastTarget,
                    "Settlement result cannot intercept card clicks");
                var device = game.Balance.Devices.Single(d => d.Id == 9002 && d.SourceId == "Reflector");
                int instanceId = context.Campaign.NextDeviceId + 1;
                CampaignStateUtility.CreateDevice(context, device, device.Price);
                wait = WaitFor(() => rig.IsBoardOpen && rig.ItemGroup.interactable &&
                    !shop.ShopCanvasGroup.blocksRaycasts, "Physical item board is interactive after settlement");
                while (wait.MoveNext()) yield return null;
                Check(result.DisplayGroup.alpha == 0, "Wheel is hidden during new-round placement");
                using (var resolution = new TargetResolutionScope(rig.ViewCamera, cycle == 1 ? 1920 : 3840))
                {
                    yield return null;
                    resolution.Validate(shop.ShopCanvasGroup.GetComponentInParent<Canvas>());
                    var placement = PlaceFromCard(instanceId);
                    while (placement.MoveNext()) yield return null;
                    ClickVisible(rig.Continue.gameObject);
                }
                Check(game.Session.Phase == RoundPhase.Playing && !game.Paused,
                    "Physical Continue starts round " + (cycle + 1));
                Check(game.GetOwnedDevices().Single(d => d.InstanceId == instanceId).Placed,
                    "Placed device survives round start: " + device.Name);
                Check(game.LastRunRandomSeed == runSeed && game.LastRandomStarterDeviceIds.SequenceEqual(starterIds),
                    "Next round preserves the run seed and does not grant starters");
                wait = Wait(.4);
                while (wait.MoveNext()) yield return null;
                Check(Quaternion.Angle(buildLeverRotation, rig.LeverPivot.localRotation) > 1f &&
                    Quaternion.Angle(playingLeverRotation, rig.LeverPivot.localRotation) < .5f,
                    "Build-to-Playing rotates the physical lever back to its ON position");
            }
            ValidateGeometry();
            wait = WaitFor(() => !rig.ItemGroup.blocksRaycasts && rig.ItemGroup.alpha == 0,
                "Final Playing state has no item-board input mask");
            while (wait.MoveNext()) yield return null;
            game.RestartRound();
            Check(game.GetOwnedDevices().Length == 0 && game.Session.Phase == RoundPhase.Playing,
                "Restart clears placed devices and starts empty");
        }
    }
}
