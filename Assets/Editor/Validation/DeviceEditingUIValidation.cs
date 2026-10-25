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
    public static class DeviceEditingUIValidation
    {
        private const string Pending = "DeviceEditingUIValidation.Pending";
        private static readonly List<string> Report = new List<string>(), Errors = new List<string>();
        private static IEnumerator sequence;
        private static double deadline;
        private static bool originalBackground;
        private static MarbleGameController game;
        private static DiegeticMachineUI rig;
        private static DevicePlacementUI placement;
        private static DeviceSellAreaUI sale;
        private static SimulationContext context;
        public static bool Succeeded { get; private set; }
        private static string Output => Environment.GetEnvironmentVariable("MARBLE_DEVICE_EDIT_VALIDATION_OUTPUT") ??
            Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/device-move-sell"));

        static DeviceEditingUIValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            };
        }
        [MenuItem("Marbles ECS/UI/验证装置移动与售出")]
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
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += RecordError;
            deadline = EditorApplication.timeSinceStartup + 180;
            sequence = Scenario(); EditorApplication.update += Tick;
        }
        private static void RecordError(string text, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(text); }
        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("Device editing validation stopped or timed out.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (sequence.MoveNext()) return;
                Check(Errors.Count == 0, "no runtime errors: " + string.Join("; ", Errors));
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
            Report.Add(Succeeded ? "DEVICE_EDITING_UI_PASSED" : error.ToString());
            File.WriteAllLines(Path.Combine(Output, "play-report.txt"), Report);
            if (Succeeded) Debug.Log("DEVICE_EDITING_UI_PASSED: " + (Report.Count - 1) + " checks.");
            else Debug.LogException(error);
            EditorApplication.ExitPlaymode();
        }
        private static void Check(bool value, string message)
        { if (!value) throw new Exception(message); Report.Add("PASS " + message); }
        private static OwnedDeviceSnapshot Device(int id) => game.GetOwnedDevices().Single(d => d.InstanceId == id);
        private static Vector2 Point(OwnedDeviceSnapshot device) => rig.ViewCamera.WorldToScreenPoint(
            placement.BoardRoot.TransformPoint(new Vector3(device.X, placement.DevicePlaneDistance, device.Z)));

        private static void Click(GameObject target, PointerEventData.InputButton button = PointerEventData.InputButton.Left)
        {
            var canvas = target.GetComponentInParent<Canvas>();
            Vector2 point = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                ((RectTransform)target.transform).TransformPoint(((RectTransform)target.transform).rect.center));
            var hits = new List<RaycastResult>();
            var e = new PointerEventData(EventSystem.current) { position = point, pressPosition = point, button = button };
            EventSystem.current.RaycastAll(e, hits);
            Check(hits.Count > 0 && (hits[0].gameObject == target || hits[0].gameObject.transform.IsChildOf(target.transform)), "pointer reaches " + target.name);
            e.pointerCurrentRaycast = e.pointerPressRaycast = hits[0];
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, e, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, e, ExecuteEvents.pointerClickHandler);
        }
        private static void PlaceFree(int id, bool different = false)
        {
            var old = Device(id);
            if (placement.SelectedInstanceId != id) placement.BeginPlacement(id);
            for (float x = -2.7f; x <= 2.7f; x += .6f)
                for (float z = -3; z <= 3; z += .6f)
                {
                    if (different && new Vector2(x - old.X, z - old.Z).sqrMagnitude < 1) continue;
                    Vector2 pointer = rig.ViewCamera.WorldToScreenPoint(placement.BoardRoot.TransformPoint(new Vector3(x, placement.DevicePlaneDistance, z)));
                    placement.UpdatePlacementPointer(pointer);
                    if (!placement.HandleBoardClick(pointer)) continue;
                    Check(Device(id).Placed, "real placement accepts clear position for instance " + id);
                    return;
                }
            throw new Exception("No free device position found.");
        }

        private static IEnumerator Scenario()
        {
            game = UnityEngine.Object.FindObjectOfType<MarbleGameController>();
            while (!game || !game.IsReady) yield return null;
            rig = UnityEngine.Object.FindObjectOfType<DiegeticMachineUI>();
            placement = rig.Placement; sale = UnityEngine.Object.FindObjectOfType<DeviceSellAreaUI>();
            context = ((MarbleSimulation)typeof(MarbleGameController).GetField("simulation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game)).Context;
            Check(sale && sale.Placement == placement && placement.SellArea == sale, "saved scene wires the sell target to the current placement UI");
            sale.Refresh(); Check(sale.Group.alpha == 0 && !sale.Group.blocksRaycasts, "sale area is hidden during Playing");
            var round = context.Round; round.Score = Math.Max(0, round.TargetScore - context.Campaign.TotalScore); context.Round = round;
            Check(game.CashOut(), "real round settlement enters the shop");
            while (game.Session.Phase != RoundPhase.Shop) yield return null;
            var wallet = context.Campaign; wallet.Coins = 100; context.Campaign = wallet;
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity); offers.Clear();
            offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = 9002, Price = 5 });
            offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = 103, Price = 5 });
            Check(game.BuyOffer(0) && game.BuyOffer(1), "real purchases supply one reflector and two paired portals");
            Check(game.EnterBuild(), "enter the real placement phase");
            while (!rig.IsBoardOpen || !rig.ItemGroup.interactable) yield return null;
            int id = game.GetOwnedDevices().Single(d => d.DefinitionId == 9002).InstanceId;
            int[] portals = game.GetOwnedDevices().Where(d => d.DefinitionId == 103).Select(d => d.InstanceId).ToArray();
            int pairId = 0;
            Check(portals.Length == 2, "purchased portals retain two independent instances");

            using (var viewport = new Viewport(rig.ViewCamera, 1920, 1080))
            {
                yield return null; viewport.Render();
                rig.RefreshInventory();
                int slot = Array.IndexOf((int[])typeof(DiegeticMachineUI).GetField("deviceIds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig), id);
                Click(rig.DeviceButtons[slot].gameObject);
                Check(placement.SelectedInstanceId == id, "real inventory card selects its instance");
                PlaceFree(id); yield return null;
                var old = Device(id);
                Check(placement.HandleBoardClick(Point(old)) && placement.IsMovingPlacedDevice, "board click picks the MRes through the cover");
                placement.RotateSelected(45); sale.Refresh();
                Check(Device(id).X == old.X && Device(id).Angle == old.Angle, "move preview leaves the saved pose unchanged");
                viewport.Render(); Click(sale.gameObject, PointerEventData.InputButton.Right);
                Check(placement.SelectedInstanceId == 0 && Device(id).X == old.X && Device(id).Angle == old.Angle, "right click restores original position and rotation");
                Check(placement.HandleBoardClick(Point(Device(id))), "cancelled device can be selected again");
                placement.RotateSelected(45); PlaceFree(id, true); yield return null;
                Check(Device(id).Angle == (old.Angle + 45) % 360 && new Vector2(Device(id).X - old.X, Device(id).Z - old.Z).sqrMagnitude > .1f,
                    "confirmed move updates both position and rotation");
                PlaceFree(portals[0]); PlaceFree(portals[1]); yield return null;
                var partner = Device(portals[1]); pairId = Device(portals[0]).PairId;
                PlaceFree(portals[0], true); yield return null;
                Check(pairId != 0 && Device(portals[0]).PairId == pairId && Device(portals[1]).PairId == pairId &&
                    Device(portals[1]).X == partner.X && Device(portals[1]).Z == partner.Z, "moving one portal preserves its pairing and its partner pose");
                Check(placement.HandleBoardClick(Point(Device(id))), "select a device for sale");
            }
            foreach (var size in new[] { new Vector2Int(1280,720), new Vector2Int(1920,1080), new Vector2Int(3840,2160), new Vector2Int(1280,960), new Vector2Int(2560,1080) })
            using (var viewport = new Viewport(rig.ViewCamera, size.x, size.y))
            {
                yield return null; sale.Refresh(); viewport.Render(); viewport.CheckLayout(sale);
                long coins = game.Session.Coins;
                placement.UpdatePlacementPointer(viewport.Center((RectTransform)sale.transform));
                Check(Device(id).Placed && game.Session.Coins == coins, size + " hover never sells or removes placement");
                Check(sale.Title.text.Contains(Device(id).SellPrice.ToString()) && sale.Title.text.Contains(Device(id).Name), size + " sale label shows the selected name and metadata price");
                if (size.x == 1920) viewport.Capture(Path.Combine(Output, "sale-1920x1080.png"));
            }
            using (var viewport = new Viewport(rig.ViewCamera, 1920,1080))
            {
                yield return null; viewport.Render();
                long coins = game.Session.Coins, price = Device(id).SellPrice;
                Click(sale.gameObject); yield return null;
                Check(game.GetOwnedDevices().All(d => d.InstanceId != id) && game.Session.Coins == coins + price, "real sale pointer event removes one instance and credits its value");
                Click(sale.gameObject); Check(game.Session.Coins == coins + price, "second sale click cannot pay twice");
                placement.BeginPlacement(portals[0]); sale.Refresh(); viewport.Render();
                Click(sale.gameObject); yield return null;
                Check(game.GetOwnedDevices().Length == 1 && Device(portals[1]).PairId == pairId, "selling one portal retains its partner");
                placement.BeginPlacement(portals[1]);
                var library = UnityEngine.Object.FindObjectOfType<ItemLibraryUI>(); library.Open(); yield return null;
                sale.Refresh(); Check(placement.SelectedInstanceId == 0 && sale.Group.alpha == 0, "opening the item library cancels editing and hides the sale target");
                library.Close(); while (library.InputBlocked) yield return null;
                sale.Refresh(); viewport.Render(); Click(rig.Continue.gameObject); yield return null;
                sale.Refresh(); Check(game.Session.Phase == RoundPhase.Playing && sale.Group.alpha == 0 && !sale.Group.blocksRaycasts, "starting the round removes editing UI and preserves the device");
            }
        }

        private sealed class Viewport : IDisposable
        {
            private readonly Camera camera; private readonly RenderTexture target, previousTarget;
            private readonly float previousAspect, previousFov; private readonly Rect previousRect;
            private readonly Canvas[] overlays; private readonly Camera[] cameras; private readonly float[] distances;
            internal Viewport(Camera camera, int width, int height)
            {
                this.camera = camera; previousTarget = camera.targetTexture; previousAspect = camera.aspect; previousFov = camera.fieldOfView; previousRect = camera.rect;
                overlays = UnityEngine.Object.FindObjectsOfType<Canvas>(true).Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
                cameras = overlays.Select(c => c.worldCamera).ToArray(); distances = overlays.Select(c => c.planeDistance).ToArray();
                target = new RenderTexture(width, height, 24); target.Create();
                camera.targetTexture = target; camera.rect = new Rect(0,0,1,1); camera.aspect = (float)width / height;
                foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = camera; c.planeDistance = 1; }
                Canvas.ForceUpdateCanvases();
            }
            internal void Render() { Canvas.ForceUpdateCanvases(); camera.Render(); Canvas.ForceUpdateCanvases(); }
            internal Vector2 Center(RectTransform r) => camera.WorldToScreenPoint(r.TransformPoint(r.rect.center));
            internal void CheckLayout(DeviceSellAreaUI view)
            {
                var points = new Vector3[4]; ((RectTransform)view.transform).GetWorldCorners(points);
                var screen = points.Select(p => (Vector2)camera.WorldToScreenPoint(p)).ToArray();
                Check(screen.All(p => p.x >= 0 && p.x <= target.width && p.y >= 0 && p.y < target.height * .25f), target.width + "x" + target.height + " red area fits the bottom of the screen");
                Check(Mathf.Abs(Center((RectTransform)view.transform).x-target.width*.5f)<1, "sale area is horizontally centered");
                foreach (var label in new[] { view.Title, view.Hint })
                {
                    label.ForceMeshUpdate(); Check(!label.isTextOverflowing, "sale text fits: " + label.name);
                    Check(label.text.All(c => char.IsWhiteSpace(c) || label.font.HasCharacter(c, true, true)), "font covers sale text: " + label.name);
                }
            }
            internal void Capture(string path)
            {
                Render(); var previous = RenderTexture.active; var texture = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try { RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG()); }
                finally { RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture); }
            }
            public void Dispose()
            {
                for (int i=0;i<overlays.Length;i++) if(overlays[i]) { overlays[i].renderMode=RenderMode.ScreenSpaceOverlay;overlays[i].worldCamera=cameras[i];overlays[i].planeDistance=distances[i]; }
                camera.targetTexture=previousTarget;camera.rect=previousRect;camera.aspect=previousAspect;camera.fieldOfView=previousFov;
                target.Release();UnityEngine.Object.DestroyImmediate(target);Canvas.ForceUpdateCanvases();
            }
        }
    }
}
