using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
            else
            {
                if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity");
                SessionState.SetBool(Pending, true);
                LegacyMachineValidationStartup.EnterPlayMode();
            }
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
            if (Application.isBatchMode) EditorApplication.Exit(Succeeded ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
        private static void Check(bool value, string message)
        { if (!value) throw new Exception(message); Report.Add("PASS " + message); }
        private static OwnedDeviceSnapshot Device(int id) => game.GetOwnedDevices().Single(d => d.InstanceId == id);
        private static Vector2 Point(OwnedDeviceSnapshot device) => rig.ViewCamera.WorldToScreenPoint(
            placement.BoardRoot.TransformPoint(new Vector3(device.X, placement.DevicePlaneDistance, device.Z)));

        private static Vector2 CheckPlacedHover(int id, string phase)
        {
            var snapshot = Device(id);
            var root = placement.GetPlacedDeviceRoot(id);
            Vector2 pointer = rig.ViewCamera.WorldToScreenPoint(root.GetComponent<DeviceBody>().PlacementCenter);
            rig.UpdateTooltip(pointer);
            var fill = root.GetComponentsInChildren<MeshRenderer>().Where(renderer => renderer.enabled)
                .SelectMany(renderer => renderer.sharedMaterials)
                .FirstOrDefault(material => material && material.shader.name == "Marbles/QuickOutline/Fill");
            Check(fill && fill.GetColor("_OutlineColor") == QuickOutline.HoverColor, phase + " shows the placed device's blue outline");
            Check(rig.Tooltip.gameObject.activeSelf &&
                rig.TooltipText.text.Contains(snapshot.Name) &&
                rig.TooltipText.text.Contains(snapshot.Description) &&
                rig.TooltipText.text.Contains(DeviceDisplayText.Durability(snapshot)),
                phase + " tooltip shows the name, description and durability");
            rig.TooltipText.ForceMeshUpdate();
            Check(!rig.TooltipText.isTextOverflowing, phase + " description fits its tooltip");
            return pointer;
        }

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
        private static Vector2 FindClearDrop(int id, bool different = false)
        {
            var old = Device(id);
            for (float x = -2.7f; x <= 2.7f; x += .6f)
                for (float z = -3; z <= 3; z += .6f)
                {
                    if (different && new Vector2(x - old.X, z - old.Z).sqrMagnitude < 1) continue;
                    Vector2 pointer = rig.ViewCamera.WorldToScreenPoint(placement.BoardRoot.TransformPoint(new Vector3(x, placement.DevicePlaneDistance, z)));
                    placement.UpdatePlacementPointer(pointer);
                    if (!string.IsNullOrEmpty(placement.LastFeedback)) continue;
                    var outline = placement.BoardRoot.GetComponentsInChildren<MeshRenderer>()
                        .SelectMany(renderer => renderer.sharedMaterials)
                        .FirstOrDefault(material => material && material.shader.name == "Marbles/QuickOutline/Fill");
                    if (outline && outline.GetColor("_OutlineColor").g > outline.GetColor("_OutlineColor").r)
                        return pointer;
                }
            throw new Exception("No free device position found.");
        }

        private static void PlaceFree(int id, bool different = false)
        {
            var old = Device(id);
            if (placement.SelectedInstanceId != id)
                Check(old.Placed ? placement.HandleBoardPointerDown(Point(old)) : placement.BeginInventoryDrag(id, Point(old)),
                    "start dragging instance " + id);
            Vector2 pointer = FindClearDrop(id, different);
            bool placed = old.Placed ? placement.HandleBoardPointerUp(pointer) : placement.EndInventoryDrag(pointer);
            Check(placed && Device(id).Placed, "releasing places instance " + id + " at a clear position");
        }

        private static IEnumerator Scenario()
        {
            game = UnityEngine.Object.FindObjectOfType<MarbleGameController>();
            while (!game || !game.IsReady) yield return null;
            rig = UnityEngine.Object.FindObjectOfType<DiegeticMachineUI>();
            placement = rig.Placement; sale = UnityEngine.Object.FindObjectOfType<DeviceSellAreaUI>();
            context = ((MarbleSimulation)typeof(MarbleGameController).GetField("simulation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game)).Context;
            Check(sale && sale.Placement == placement && placement.SellArea == sale, "saved scene wires the sell target to the current placement UI");
            if (game.Session.Phase == RoundPhase.Build)
            {
                Check(game.BeginRound(), "start the round when the scene opens with starter devices");
                yield return null;
            }
            sale.Refresh(); Check(sale.Group.alpha == 0 && !sale.Group.blocksRaycasts, "sale area is hidden during Playing");
            var round = context.Round; round.Score = round.TargetScore; context.Round = round;
            Check(game.CashOut(), "real round settlement begins");
            while (game.Session.Phase == RoundPhase.Draining) yield return null;
            Check(game.Session.Phase == RoundPhase.Build, "settlement returns to placement");
            foreach (var starter in game.GetOwnedDevices())
                Check(game.SellDevice(starter.InstanceId), "clear starter gear from the test inventory");
            var wallet = context.Campaign; wallet.Coins = 100; context.Campaign = wallet;
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity); offers.Clear();
            offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = 9002, Price = 5 });
            offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = 103, Price = 5 });
            var shop = UnityEngine.Object.FindObjectsOfType<ShopUI>().Single(item => item.Controller == game);
            var shopCard = UnityEngine.Object.Instantiate(shop.OfferPrefab, shop.OfferContainer);
            shopCard.gameObject.SetActive(true);
            shopCard.Bind(game.GetShopOffers()[0], wallet.Coins, null);
            shopCard.OnPointerEnter(new PointerEventData(EventSystem.current));
            var shopBorder = shopCard.GetComponent<Outline>();
            Check(shopCard.GetComponent<Image>().raycastTarget && shopBorder && shopBorder.enabled &&
                shopBorder.effectColor == QuickOutline.HoverColor && !string.IsNullOrWhiteSpace(shopCard.DescriptionText.text),
                "hovering the shop card highlights its border next to the description");
            shopCard.OnPointerExit(new PointerEventData(EventSystem.current));
            Check(!shopBorder.enabled, "leaving the shop card restores its border");
            Check(CampaignShopSystem.Buy(context, 0) && CampaignShopSystem.Buy(context, 1),
                "purchase rules supply one reflector and two paired portals");
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
                var card = rig.DeviceButtons[slot];
                var cardBorder = card.GetComponent<Outline>();
                Color originalBorder = cardBorder.effectColor;
                var hit = card.GetComponent<DiegeticUIHit>();
                var pointer = new PointerEventData(EventSystem.current) { position = viewport.Center((RectTransform)card.transform) };
                hit.OnPointerEnter(pointer); rig.UpdateTooltip(pointer.position);
                Check(cardBorder.effectColor == QuickOutline.HoverColor && rig.Tooltip.gameObject.activeSelf &&
                    rig.TooltipText.text.Contains(Device(id).Description), "inventory hover shows a blue border and description");
                hit.OnPointerExit(pointer);
                Check(cardBorder.effectColor == originalBorder, "leaving the inventory card restores its border");
                Click(rig.DeviceButtons[slot].gameObject);
                Check(placement.SelectedInstanceId == 0, "clicking the inventory card leaves placement idle");
                pointer.button = PointerEventData.InputButton.Left;
                pointer.pressPosition = pointer.position;
                hit.OnPointerDown(pointer); hit.OnBeginDrag(pointer);
                Check(placement.SelectedInstanceId == id && placement.IsDraggingInventory, "dragging the real inventory card picks up its instance");
                pointer.position = FindClearDrop(id);
                hit.OnDrag(pointer); hit.OnEndDrag(pointer);
                Check(Device(id).Placed && placement.SelectedInstanceId == 0, "releasing the real inventory card places the device");
                yield return null;
                var old = Device(id);
                rig.ClearHover();
                Vector2 hoverPoint = CheckPlacedHover(id, "Build");
                viewport.Capture(Path.Combine(Output, "hover-build-1920x1080.png"));
                var shopPanel = shopCard.GetComponentInParent<CanvasGroup>();
                shopPanel.alpha = 1;
                shopPanel.blocksRaycasts = true;
                try
                {
                    Canvas.ForceUpdateCanvases();
                    rig.UpdateTooltip(hoverPoint);
                    Check(!rig.Tooltip.gameObject.activeSelf, "the shop panel blocks hover on the covered board");
                }
                finally
                {
                    shopPanel.alpha = 0;
                    shopPanel.blocksRaycasts = false;
                    UnityEngine.Object.Destroy(shopCard.gameObject);
                }
                rig.UpdateTooltip(hoverPoint);
                rig.UpdateTooltip(new Vector2(-1, -1));
                Check(!rig.Tooltip.gameObject.activeSelf, "leaving the board hides the device tooltip");
                Check(placement.HandleBoardPointerDown(Point(old)) && placement.IsMovingPlacedDevice, "left press picks the device up through the cover");
                placement.RotateSelected(45); sale.Refresh();
                Check(Device(id).X == old.X && Device(id).Angle == old.Angle, "move preview leaves the saved pose unchanged");
                viewport.Render(); Click(sale.gameObject, PointerEventData.InputButton.Right);
                Check(placement.SelectedInstanceId == 0 && Device(id).X == old.X && Device(id).Angle == old.Angle, "right click restores original position and rotation");
                Check(placement.HandleBoardPointerDown(Point(Device(id))), "cancelled device can be picked up again");
                placement.RotateSelected(45); PlaceFree(id, true); yield return null;
                Check(Device(id).Angle == (old.Angle + 45) % 360 && new Vector2(Device(id).X - old.X, Device(id).Z - old.Z).sqrMagnitude > .1f,
                    "confirmed move updates both position and rotation");
                PlaceFree(portals[0]); PlaceFree(portals[1]); yield return null;
                var partner = Device(portals[1]); pairId = Device(portals[0]).PairId;
                PlaceFree(portals[0], true); yield return null;
                Check(pairId != 0 && Device(portals[0]).PairId == pairId && Device(portals[1]).PairId == pairId &&
                    Device(portals[1]).X == partner.X && Device(portals[1]).Z == partner.Z, "moving one portal preserves its pairing and its partner pose");

                rig.RefreshInventory();
                Check(rig.VisibleDeviceCount == 0, "the empty backpack is visible after placing all devices");
                Vector2 backpack = viewport.Center((RectTransform)rig.DeviceButtons[0].transform);
                var toStore = Device(id);
                Vector2 pickup = Point(toStore);
                Check(placement.HandleBoardPointerDown(pickup) && !placement.HandleBoardPointerUp(pickup) && placement.SelectedInstanceId == 0 &&
                    !placement.HandleBoardPointerUp(backpack) && Device(id).Placed, "an unrelated release after a click does not return the device");
                placement.CancelPlacement();

                var script = game.DeviceScript(toStore);
                long walletBeforeReturn = game.Session.Coins;
                int layoutBeforeReturn = game.Session.LayoutRevision;
                Check(placement.HandleBoardPointerDown(pickup), "left press picks up the placed device");
                placement.UpdatePlacementPointer(backpack);
                rig.UpdateTooltip(backpack);
                Check(rig.Tooltip.gameObject.activeSelf && rig.TooltipText.text.Contains("松开收回背包") &&
                    Device(id).Placed, "hover over an empty backpack slot prompts a return while preserving placement");
                Check(placement.HandleBoardPointerUp(backpack), "releasing over the empty slot returns the device");
                var stored = Device(id);
                Check(!stored.Placed && placement.SelectedInstanceId == 0 && !placement.GetPlacedDeviceRoot(id) &&
                    game.Session.LayoutRevision == layoutBeforeReturn + 1, "return clears the preview and removes the physical device");
                Check(game.Session.Coins == walletBeforeReturn && stored.Angle == toStore.Angle &&
                    stored.RemainingDurability == toStore.RemainingDurability && ReferenceEquals(game.DeviceScript(stored), script),
                    "return preserves coins, rotation, durability and the same Lua instance");
                rig.RefreshInventory();
                Check(rig.VisibleDeviceCount == 1 && rig.DeviceButtons[0].gameObject.activeSelf, "the returned item is listed in the backpack");
                Check(rig.BeginDeviceDrag(0, backpack) && placement.SelectedInstanceId == id, "the returned backpack card drags the original device");
                PlaceFree(id); yield return null;
                Check(placement.HandleBoardPointerDown(Point(Device(id))), "pick up a board device for sale");
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
                Vector2 salePoint = viewport.Center((RectTransform)sale.transform);
                Check(placement.HandleBoardPointerUp(salePoint), "release the board device in the sale area");
                yield return null;
                Check(game.GetOwnedDevices().All(d => d.InstanceId != id) && game.Session.Coins == coins + price, "the release removes one instance and credits its value");
                Check(!placement.HandleBoardPointerUp(salePoint) && game.Session.Coins == coins + price, "a second release cannot pay twice");
                long portalCoins = game.Session.Coins;
                long portalPrice = Device(portals[0]).SellPrice;
                Check(placement.HandleBoardPointerDown(Point(Device(portals[0]))), "pick up one paired portal");
                sale.Refresh();
                Check(sale.Title.text.Contains("整对") && sale.Title.text.Contains(portalPrice.ToString()),
                    "the sale preview shows a pair and one payout");
                Check(placement.HandleBoardPointerUp(salePoint), "release the portal to sell it");
                yield return null;
                Check(game.GetOwnedDevices().All(d => d.PairId != pairId) && !placement.GetPlacedDeviceRoot(portals[0]) &&
                    !placement.GetPlacedDeviceRoot(portals[1]), "selling one portal clears both views and inventory entries");
                Check(game.Session.Coins == portalCoins + portalPrice, "selling the portal pair credits one value");

                var bagDefinition = context.Balance.Devices.Single(d => d.Id == 9026);
                var foodDefinition = context.Balance.Devices.Single(d => d.Id == 103);
                CampaignStateUtility.CreateDevice(context, bagDefinition, bagDefinition.Price);
                int bag = context.Campaign.NextDeviceId;
                CampaignStateUtility.CreateDevice(context, foodDefinition, foodDefinition.Price);
                int food = context.Campaign.NextDeviceId;
                int foodPartner = food - 1;
                PlaceFree(bag);
                PlaceFree(food);
                PlaceFree(foodPartner);
                yield return null;
                var originalFood = Device(food);
                Vector2 pickup = Point(originalFood);
                Vector2 receiver = rig.ViewCamera.WorldToScreenPoint(placement.GetPlacedDeviceRoot(bag).GetComponent<DeviceBody>().PlacementCenter);
                int offerRevision = game.Session.LayoutRevision;
                long offerCoins = game.Session.Coins;
                int receiverDurability = Device(bag).RemainingDurability;
                Check(placement.HandleBoardPointerDown(pickup) && placement.SelectedInstanceId == food, "pick up placed food for the receiver");
                placement.UpdatePlacementPointer(receiver);
                rig.UpdateTooltip(receiver);
                var outline = placement.BoardRoot.GetComponentsInChildren<MeshRenderer>()
                    .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m && m.shader.name == "Marbles/QuickOutline/Fill");
                Check(outline && outline.GetColor("_OutlineColor").g > outline.GetColor("_OutlineColor").r &&
                    rig.TooltipText.text.Contains("松开投入「大胃袋」") && rig.TooltipText.text.Contains("整对装置"),
                    "eligible drop shows the green preview and pair consumption hint");
                Check(Device(food).Placed && game.Session.LayoutRevision == offerRevision, "hovering over the receiver preserves the food's placement");
                placement.CancelPlacement();
                Check(!placement.HandleBoardPointerUp(receiver) && Device(food).X == originalFood.X &&
                    Device(food).Z == originalFood.Z && placement.GetPlacedDeviceRoot(food).activeInHierarchy,
                    "cancelling the drag restores the food's original view");

                Check(placement.HandleBoardPointerDown(pickup) && placement.HandleBoardPointerUp(receiver),
                    "releasing the placed device on the bag completes the offer");
                Check(game.GetOwnedDevices().All(d => d.PairId != originalFood.PairId) && !placement.GetPlacedDeviceRoot(food) &&
                    !placement.GetPlacedDeviceRoot(foodPartner) &&
                    placement.SelectedInstanceId == 0 && game.Session.LayoutRevision == offerRevision + 1,
                    "the accepted drop consumes the pair and clears both views");
                var growth = bagDefinition.Strength * ((int)originalFood.Rarity + 1);
                rig.UpdateTooltip(receiver);
                Check(game.GetDeviceStatus(bag).Contains("成长 +" + growth) && rig.TooltipText.text.Contains("已投入「大胃袋」") &&
                    game.Session.Coins == offerCoins && Device(bag).RemainingDurability == receiverDurability,
                    "the real drop reports growth without selling or spending the bag's durability");

                Check(placement.HandleBoardPointerDown(Point(Device(bag))), "pick up the remaining device before opening the library");
                var library = UnityEngine.Object.FindObjectOfType<ItemLibraryUI>(); library.Open(); yield return null;
                sale.Refresh(); Check(placement.SelectedInstanceId == 0 && sale.Group.alpha == 0, "opening the item library cancels editing and hides the sale target");
                library.Close(); while (library.InputBlocked) yield return null;
                sale.Refresh(); viewport.Render(); Click(rig.Continue.gameObject); yield return null;
                sale.Refresh(); Check(game.Session.Phase == RoundPhase.Playing && sale.Group.alpha == 0 && !sale.Group.blocksRaycasts, "starting the round removes editing UI and preserves the device");
                CheckPlacedHover(bag, "Playing");
                viewport.Capture(Path.Combine(Output, "hover-playing-1920x1080.png"));
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
