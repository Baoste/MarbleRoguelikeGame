using System;
using System.Linq;
using System.Reflection;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void DeviceEditingValidation()
        {
            int start = assertions;
            using var f = new Fixture();
            BagBuild(f.C);
            var scene = SceneManager.CreateScene("Device edit validation", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var board = new GameObject("Board"); SceneManager.MoveGameObjectToScene(board, scene);
            var control = new GameObject("Controller");
            var cameraObject = new GameObject("Edit camera");
            var events = new GameObject("Edit events", typeof(EventSystem));
            var canvasObject = new GameObject("Edit UI", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            var controller = control.AddComponent<MarbleGameController>(); controller.enabled = false;
            var simulationField = typeof(MarbleGameController).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic);
            simulationField.SetValue(controller, f.Simulation);
            typeof(MarbleGameController).GetField("localPhysics", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, scene.GetPhysicsScene());
            controller.PhysicsRoot = board.transform;
            var surface = board.AddComponent<BoardPlacementSurface>(); surface.Length = surface.BottomWidth = surface.TopWidth = 8;
            controller.PlacementSurface = surface;
            var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5;
            camera.transform.SetPositionAndRotation(new Vector3(0, 10, 0), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var placement = control.AddComponent<DevicePlacementUI>(); placement.Controller = controller;
            placement.BoardRoot = placement.PlacementPlane = board.transform; placement.ViewCamera = camera;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var panel = new GameObject("Sell", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(CanvasGroup), typeof(DeviceSellAreaUI));
            var rect = (RectTransform)panel.transform; rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0); rect.sizeDelta = new Vector2(600, 90); rect.anchoredPosition = new Vector2(0, 15);
            var sale = panel.GetComponent<DeviceSellAreaUI>(); sale.Placement = placement; sale.Group = panel.GetComponent<CanvasGroup>();
            sale.Background = panel.GetComponent<UnityEngine.UI.Image>(); placement.SellArea = sale;
            Canvas.ForceUpdateCanvases();
            Vector2 salePoint = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Vector2 Point(float x, float z) => camera.WorldToScreenPoint(board.transform.TransformPoint(new Vector3(x, 0, z)));
            OwnedDeviceData Data(int id)
            {
                Check(CampaignStateUtility.FindDevice(f.C, id, out var e), "edited instance still exists");
                return f.C.Manager.GetComponentData<OwnedDeviceData>(e);
            }
            int Create(uint definitionId, float x, float z, bool placed = true)
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == definitionId);
                CampaignStateUtility.CreateDevice(f.C, definition, definition.Price);
                int id = f.C.Campaign.NextDeviceId;
                Check(f.Simulation.PlaceDevice(id, x, z), "prepare owned device position");
                if (!placed) Check(f.Simulation.RemoveDevicePlacement(id), "prepare inventory device");
                return id;
            }
            GameObject View(int id) => ((System.Collections.Generic.Dictionary<int, MRes.MResLoadHandle>)
                typeof(DevicePlacementUI).GetField("placedObjects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(placement))[id].Root;
            void ClickSale(PointerEventData.InputButton button) => sale.OnPointerClick(new PointerEventData(EventSystem.current) { button = button, position = salePoint });
            try
            {
                int id = Create(9008, 0, 0);
                int other = Create(9002, 2, 0);
                var original = Data(id); original.Angle = 45; original.UsesThisRound = 4; original.DurabilityRound = f.C.Round.RoundId; original.StoredValue = 7;
                CampaignStateUtility.FindDevice(f.C, id, out var entity); f.C.Manager.SetComponentData(entity, original);
                var script = f.C.DeviceScripts.Get(id, original.DefinitionId);
                var obstacle = new GameObject("Fixed obstacle"); obstacle.transform.SetParent(board.transform, false);
                obstacle.transform.localPosition = new Vector3(-2, .1f, 0); obstacle.AddComponent<BoxCollider>().size = Vector3.one * .3f;
                controller.PlacementObstacleRoots = new[] { obstacle.transform };
                var glass = new GameObject("Glass cover"); glass.transform.SetParent(board.transform, false);
                glass.layer = 2; var cover = glass.AddComponent<BoxCollider>(); cover.center = new Vector3(0, 1.55f, 0); cover.size = new Vector3(8, .1f, 8);

                Check(placement.HandleBoardClick(Point(0, 0)) && placement.IsMovingPlacedDevice, "left click selects a placed MRes device through the glass cover");
                Check(!View(id).activeSelf && Data(id).Placed, "moving hides only the original view, not the committed instance");
                placement.RotateSelected(45); placement.UpdatePlacementPointer(Point(-1, 1));
                Near(0, Data(id).X, "preview does not mutate committed X"); Near(45, Data(id).Angle, "preview rotation is deferred");
                placement.CancelPlacement();
                Check(View(id).activeSelf && placement.SelectedInstanceId == 0, "cancel restores the original view and clears selection");
                Near(45, Data(id).Angle, "cancel restores original rotation without a rollback command");
                Check(ReferenceEquals(script, f.C.DeviceScripts.Get(id, original.DefinitionId)), "cancel retains the same Lua state owner");

                placement.BeginPlacement(id); placement.RotateSelected(45);
                int revision = f.C.Campaign.LayoutRevision;
                Check(!placement.HandleBoardClick(Point(2, 0)), "other devices block a move");
                Check(!placement.HandleBoardClick(Point(-2, 0)), "fixed colliders block a move");
                Check(!placement.HandleBoardClick(Point(20, 20)), "outside-board move is rejected");
                Near(revision, f.C.Campaign.LayoutRevision, "failed moves do not change layout revision");
                Check(placement.SelectedInstanceId == id && Data(id).Placed, "failed move keeps the selection and original placement");
                Check(placement.HandleBoardClick(Point(-1, 1)), "left click commits a clear destination");
                Near(-1, Data(id).X, "committed X"); Near(1, Data(id).Z, "committed Z"); Near(90, Data(id).Angle, "position and rotation commit together");
                Near(revision + 1, f.C.Campaign.LayoutRevision, "move publishes one layout revision");
                Near(4, Data(id).UsesThisRound, "move preserves durability consumption"); Near(7, Data(id).StoredValue, "move preserves stored gameplay state");
                Check(ReferenceEquals(script, f.C.DeviceScripts.Get(id, original.DefinitionId)), "move preserves Lua state owner");
                Check(View(id).activeSelf && placement.SelectedInstanceId == 0, "committed view is active at its new pose");
                Near(-1, View(id).transform.localPosition.x, "physical view follows moved instance");

                foreach (var c in View(id).GetComponentsInChildren<Collider>()) c.enabled = false;
                Check(placement.HandleBoardClick(Point(-1, 1)), "logical footprint selects an exhausted device with disabled colliders");
                placement.CancelPlacement();
                placement.BeginPlacement(id); placement.RotateSelected(45);
                long coins = f.C.Campaign.Coins; long price = f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == id).SellPrice;
                sale.Refresh(); Check(sale.Group.alpha == 1 && sale.Group.blocksRaycasts, "sale area is visible and receives input during Build");
                placement.UpdatePlacementPointer(salePoint); sale.Refresh();
                Near(coins, f.C.Campaign.Coins, "hovering the red area never sells");
                Check(!placement.HandleBoardClick(salePoint), "sale area cannot also place a device through UI");
                ClickSale(PointerEventData.InputButton.Right);
                Near(coins, f.C.Campaign.Coins, "right click on sale area never sells");
                Check(placement.SelectedInstanceId == 0 && View(id).activeSelf, "right click on sale area cancels the move");
                placement.BeginPlacement(id);
                ClickSale(PointerEventData.InputButton.Left);
                Check(!CampaignStateUtility.FindDevice(f.C, id, out _) && placement.SelectedInstanceId == 0, "left click sells exactly the selected instance");
                Near(coins + price, f.C.Campaign.Coins, "sale credits the metadata value");
                ClickSale(PointerEventData.InputButton.Left); Near(coins + price, f.C.Campaign.Coins, "repeated click cannot pay twice");
                Check(board.GetComponentsInChildren<MResDeviceBody>(true).All(b => b.InstanceId != id || !b.gameObject.activeInHierarchy),
                    "sold view and colliders are inactive immediately, before deferred Unity destruction");

                int stock = Create(9008, -1, 0, false);
                coins = f.C.Campaign.Coins;
                Check(placement.BeginInventoryDrag(stock, Point(-1, 0)), "inventory drag still starts");
                Check(!placement.EndInventoryDrag(salePoint, panel) && placement.SelectedInstanceId == stock, "dropping over sale area waits for a separate confirmation click");
                Near(coins, f.C.Campaign.Coins, "drag release does not sell");
                placement.CancelPlacement(); Check(!Data(stock).Placed, "cancel after a sale drop preserves inventory");
                placement.BeginPlacement(stock); price = f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == stock).SellPrice;
                ClickSale(PointerEventData.InputButton.Left); Near(coins + price, f.C.Campaign.Coins, "inventory selection also sells at metadata value");

                placement.BeginPlacement(other); controller.SetPaused(true); placement.UpdatePlacementPointer(Point(0, 1)); sale.Refresh();
                Check(placement.SelectedInstanceId == 0 && View(other).activeSelf && sale.Group.alpha == 0, "pause cancels moving and hides the sale target");
                Check(!placement.TrySellSelected(), "paused input cannot sell"); controller.SetPaused(false);
                placement.BeginPlacement(other); placement.enabled = false;
                Check(View(other).activeSelf && placement.SelectedInstanceId == 0, "disabling the UI restores the original view"); placement.enabled = true;
                placement.BeginPlacement(other); Check(f.Simulation.BeginRound(), "begin round with a pending UI preview");
                placement.UpdatePlacementPointer(Point(0, 1)); sale.Refresh();
                Check(placement.SelectedInstanceId == 0 && Data(other).X == 2 && sale.Group.alpha == 0, "phase transition cancels preview, keeps original pose and hides sale area");
                Check(!placement.HandleBoardClick(Point(2, 0)), "playing phase cannot select or move devices");

                var round = f.C.Round; round.Phase = (byte)RoundPhase.Shop; f.C.Round = round;
                Check(controller.EnterBuild() && controller.Session.Phase == RoundPhase.Build, "shop retains Build when all remaining devices are already placed");
                Near(2, Data(other).X, "entering Build preserves last committed layout");
                Check(!f.Simulation.PlaceDevice(other, 1, 0, .15f, float.NaN), "invalid rotation cannot partially commit a move");
                Near(2, Data(other).X, "invalid rotation leaves position unchanged");
                Check(controller.SellDevice(other), "sell final remaining device");
                round = f.C.Round; round.Phase = (byte)RoundPhase.Shop; f.C.Round = round;
                Check(controller.EnterBuild() && controller.Session.Phase == RoundPhase.Playing, "truly empty inventory still skips unnecessary Build");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                placement.CancelPlacement(); simulationField.SetValue(controller, null);
                UnityEngine.Object.DestroyImmediate(control); UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(board); UnityEngine.Object.DestroyImmediate(events);
                SceneManager.UnloadSceneAsync(scene);
            }
            Debug.Log("DEVICE_EDITING_VALIDATION_PASSED: " + (assertions - start) + " assertions.");
        }
    }
}
