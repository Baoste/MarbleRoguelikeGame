using System;
using System.Linq;
using System.Reflection;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
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
            GameObject View(int id) => placement.GetPlacedDeviceRoot(id);
            Material Highlight(GameObject root = null) => (root ? root : board).GetComponentsInChildren<MeshRenderer>()
                .Where(renderer => renderer.enabled)
                .SelectMany(renderer => renderer.sharedMaterials)
                .FirstOrDefault(material => material && material.shader.name == "Marbles/QuickOutline/Fill");
            void CheckHighlight(bool placeable, string message)
            {
                var material = Highlight();
                var color = material ? material.GetColor("_OutlineColor") : Color.clear;
                Check(material && (placeable ? color.g > color.r : color.r > color.g), message);
            }
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

                Check(placement.HandleBoardPointerDown(Point(0, 0)), "pick up the placed device through the glass cover");
                placement.CancelPlacement();
                Check(placement.UpdatePlacedDeviceHover(Point(0, 0), out var hovered) && hovered.InstanceId == id,
                    "Build hover picks the device through the glass cover");
                Check(!string.IsNullOrWhiteSpace(hovered.Description) && hovered.RemainingDurability == 16,
                    "hover supplies the package description and current durability");
                var blue = Highlight(View(id));
                Check(blue && blue.GetColor("_OutlineColor") == QuickOutline.HoverColor, "a placed device uses the blue hover outline");
                Check(placement.UpdatePlacedDeviceHover(Point(2, 0), out hovered) && hovered.InstanceId == other &&
                    Highlight(View(other)) && !Highlight(View(id)), "hover transfers to the new device and clears the old outline");
                Check(!placement.UpdatePlacedDeviceHover(Point(20, 20), out _) && !Highlight(), "leaving the board clears hover");
                Check(placement.UpdatePlacedDeviceHover(Point(0, 0), out _) && Highlight(View(id)) == blue, "re-entering reuses the outline");
                Check(placement.HandleBoardPointerDown(Point(0, 0)) && !placement.HandleBoardPointerUp(Point(0, 0)) &&
                    placement.SelectedInstanceId == 0 && View(id).activeSelf, "a click without dragging leaves the device in place");
                Check(placement.HandleBoardPointerDown(Point(0, 0)) && placement.IsMovingPlacedDevice, "holding left starts a board drag");
                Check(!placement.UpdatePlacedDeviceHover(Point(2, 0), out _) && !Highlight(), "moving a device suppresses the blue hover outline");
                Check(!View(id).activeSelf && Data(id).Placed, "moving hides only the original view, not the committed instance");
                placement.RotateSelected(45); placement.UpdatePlacementPointer(Point(-1, 1));
                Near(0, Data(id).X, "preview does not mutate committed X"); Near(45, Data(id).Angle, "preview rotation is deferred");
                placement.CancelPlacement();
                Check(View(id).activeSelf && placement.SelectedInstanceId == 0, "cancel restores the original view and clears selection");
                Near(45, Data(id).Angle, "cancel restores original rotation without a rollback command");
                Check(ReferenceEquals(script, f.C.DeviceScripts.Get(id, original.DefinitionId)), "cancel retains the same Lua state owner");

                int revision = f.C.Campaign.LayoutRevision;
                foreach (var invalid in new[] { Point(2, 0), Point(-2, 0), Point(20, 20) })
                {
                    Check(placement.HandleBoardPointerDown(Point(0, 0)), "pick up the device for an invalid drop");
                    Check(!placement.HandleBoardPointerUp(invalid) && placement.SelectedInstanceId == 0 && View(id).activeSelf,
                        "blocked or off-board release restores the device and clears the drag");
                }
                Near(revision, f.C.Campaign.LayoutRevision, "failed moves do not change layout revision");
                Check(Data(id).Placed && Data(id).X == 0 && Data(id).Z == 0, "failed moves keep the original placement");
                Check(placement.HandleBoardPointerDown(Point(0, 0)), "pick up the device for a valid move");
                placement.RotateSelected(45);
                placement.UpdatePlacementPointer(Point(-1, 1));
                CheckHighlight(true, "moving a placed device shows a green outline at a clear destination");
                Check(placement.HandleBoardPointerUp(Point(-1, 1)), "releasing commits the new placement");
                Check(!Highlight(), "confirming a move clears the preview outline");
                Near(-1, Data(id).X, "committed X"); Near(1, Data(id).Z, "committed Z"); Near(90, Data(id).Angle, "position and rotation commit together");
                Near(revision + 1, f.C.Campaign.LayoutRevision, "move publishes one layout revision");
                Near(4, Data(id).UsesThisRound, "move preserves durability consumption"); Near(7, Data(id).StoredValue, "move preserves stored gameplay state");
                Check(ReferenceEquals(script, f.C.DeviceScripts.Get(id, original.DefinitionId)), "move preserves Lua state owner");
                Check(View(id).activeSelf && placement.SelectedInstanceId == 0, "committed view is active at its new pose");
                Near(-1, View(id).transform.localPosition.x, "physical view follows moved instance");

                foreach (var c in View(id).GetComponentsInChildren<Collider>()) c.enabled = false;
                Check(placement.UpdatePlacedDeviceHover(Point(-1, 1), out hovered) && hovered.InstanceId == id,
                    "hover uses the footprint when an exhausted device has no enabled colliders");
                Check(placement.HandleBoardPointerDown(Point(-1, 1)), "logical footprint picks up an exhausted device with disabled colliders");
                placement.CancelPlacement();
                Check(placement.HandleBoardPointerDown(Point(-1, 1)), "pick up the board device for a sale");
                long coins = f.C.Campaign.Coins; long price = f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == id).SellPrice;
                sale.Refresh(); Check(sale.Group.alpha == 1 && sale.Group.blocksRaycasts, "sale area is visible and receives input during Build");
                placement.UpdatePlacementPointer(salePoint); sale.Refresh();
                Near(coins, f.C.Campaign.Coins, "hovering the red area never sells");
                ClickSale(PointerEventData.InputButton.Left);
                Near(coins, f.C.Campaign.Coins, "left click alone cannot sell the dragged device");
                ClickSale(PointerEventData.InputButton.Right);
                Near(coins, f.C.Campaign.Coins, "right click on sale area never sells");
                Check(placement.SelectedInstanceId == 0 && View(id).activeSelf, "right click on sale area cancels the move");
                Check(placement.HandleBoardPointerDown(Point(-1, 1)) && placement.HandleBoardPointerUp(salePoint), "release in the sale area sells the board device");
                Check(!CampaignStateUtility.FindDevice(f.C, id, out _) && placement.SelectedInstanceId == 0, "sale removes the dragged instance");
                Near(coins + price, f.C.Campaign.Coins, "sale credits the metadata value");
                Check(!placement.HandleBoardPointerUp(salePoint), "a repeated release cannot sell twice");
                Near(coins + price, f.C.Campaign.Coins, "repeated release pays nothing");
                Check(board.GetComponentsInChildren<DeviceBody>(true).All(b => b.InstanceId != id || !b.gameObject.activeInHierarchy),
                    "sold view and colliders are inactive immediately, before deferred Unity destruction");

                int stock = Create(9008, -1, 0, false);
                coins = f.C.Campaign.Coins;
                int dragRevision = f.C.Campaign.LayoutRevision;
                Check(placement.BeginInventoryDrag(stock, Point(-1, 1)), "inventory drag still starts");
                CheckHighlight(true, "dragging to clear space shows a green outline");
                placement.UpdateInventoryDrag(Point(2, 0));
                CheckHighlight(false, "an overlapping device turns the outline red");
                placement.UpdateInventoryDrag(Point(-2, 0));
                CheckHighlight(false, "a fixed obstacle turns the outline red");
                placement.UpdateInventoryDrag(Point(3.3f, 0));
                CheckHighlight(false, "a footprint crossing the board edge turns the outline red");
                placement.UpdateInventoryDrag(Point(3.8f, 0));
                CheckHighlight(false, "dragging beyond the placement area keeps a visible red outline");
                placement.UpdateInventoryDrag(Point(-1, 1));
                CheckHighlight(true, "returning to clear space turns the outline green again");
                Check(f.C.Campaign.LayoutRevision == dragRevision && !Data(stock).Placed, "highlight checks leave the layout and inventory untouched");
                Check(!placement.EndInventoryDrag(Point(2, 0)) && !Highlight(), "an invalid drop clears the outline and cancels the drag");
                Check(placement.BeginInventoryDrag(stock, Point(-1, 1)), "inventory drag can restart after an invalid drop");
                placement.UpdateInventoryDrag(salePoint);
                Check(!Highlight() && placement.LastFeedback.Contains("松开出售"), "hovering over sale shows the release hint");
                Near(coins, f.C.Campaign.Coins, "hovering during an inventory drag pays nothing");
                price = f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == stock).SellPrice;
                Check(placement.EndInventoryDrag(salePoint, panel) && placement.SelectedInstanceId == 0 &&
                    !CampaignStateUtility.FindDevice(f.C, stock, out _), "inventory release in the sale area sells the dragged item");
                Near(coins + price, f.C.Campaign.Coins, "inventory sale credits the metadata value");

                Check(placement.HandleBoardPointerDown(Point(2, 0)), "pick up the device before pausing");
                controller.SetPaused(true); placement.UpdatePlacementPointer(Point(0, 1)); sale.Refresh();
                Check(placement.SelectedInstanceId == 0 && View(other).activeSelf && sale.Group.alpha == 0, "pause cancels moving and hides the sale target");
                Check(!placement.HandleBoardPointerUp(salePoint), "paused release cannot sell"); controller.SetPaused(false);
                Check(placement.HandleBoardPointerDown(Point(2, 0)), "pick up the device before disabling the UI");
                placement.enabled = false;
                Check(View(other).activeSelf && placement.SelectedInstanceId == 0, "disabling the UI restores the original view"); placement.enabled = true;
                Check(placement.HandleBoardPointerDown(Point(2, 0)) && f.Simulation.BeginRound(), "begin round with a pending UI preview");
                placement.UpdatePlacementPointer(Point(0, 1)); sale.Refresh();
                Check(placement.SelectedInstanceId == 0 && Data(other).X == 2 && sale.Group.alpha == 0, "phase transition cancels preview, keeps original pose and hides sale area");
                Check(!placement.HandleBoardPointerDown(Point(2, 0)), "playing phase cannot start a board drag");

                var round = f.C.Round;
                foreach (var phase in new[] { RoundPhase.Playing, RoundPhase.Draining, RoundPhase.Shop })
                {
                    round.Phase = (byte)phase; f.C.Round = round;
                    Check(placement.UpdatePlacedDeviceHover(Point(2, 0), out hovered) && hovered.InstanceId == other &&
                        Highlight(View(other)), phase + " uses the same placed-device hover");
                }
                Check(controller.EnterBuild() && controller.Session.Phase == RoundPhase.Build, "shop retains Build when all remaining devices are already placed");
                Near(2, Data(other).X, "entering Build preserves last committed layout");
                Check(!f.Simulation.PlaceDevice(other, 1, 0, .15f, float.NaN), "invalid rotation cannot partially commit a move");
                Near(2, Data(other).X, "invalid rotation leaves position unchanged");
                Check(controller.SellDevice(other), "sell the remaining rotatable device");
                int mushroom = Create(9016, 0, 0, false);
                revision = f.C.Campaign.LayoutRevision;
                Check(!controller.RotateDevice(mushroom, 45), "direct rotation is rejected for a fixed device");
                Check(!f.Simulation.PlaceDevice(mushroom, 0, 0, angle: 45), "placement cannot bypass the rotation lock");
                Check(f.C.Campaign.LayoutRevision == revision && !Data(mushroom).Placed, "rejected rotation preserves inventory");
                Check(placement.BeginInventoryDrag(mushroom, Point(0, 0)), "fixed device can be dragged from inventory");
                placement.RotateSelected(45);
                Check(placement.EndInventoryDrag(Point(0, 0)), "fixed device can be placed after rotation input");
                Near(0, Data(mushroom).Angle, "inventory drag keeps the authored angle");
                Check(placement.HandleBoardPointerDown(Point(0, 0)), "pick up a fixed device for moving");
                placement.RotateSelected(-45);
                Check(placement.HandleBoardPointerUp(Point(-1, 1)), "fixed device can be moved after rotation input");
                Near(0, Data(mushroom).Angle, "moving keeps the authored angle");
                Near(0, Quaternion.Angle(board.transform.rotation, View(mushroom).transform.rotation), "fixed device view follows the board orientation");
                Check(controller.SellDevice(mushroom), "sell the fixed device");
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
