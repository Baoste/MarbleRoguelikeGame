using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MarblesECS.Editor
{
    /// <summary>Opt-in play-mode checks; never opens/saves scenes or restarts a campaign.</summary>
    public static class ItemLibraryValidation
    {
        private static IEnumerator running;
        private static double deadline;
        private static readonly List<string> results = new List<string>();
        public static bool IsRunning => running != null;
        public static bool Succeeded { get; private set; }
        public static string LastResult { get; private set; }

        [MenuItem("Marbles ECS/道具库/验证当前运行场景")]
        public static void RunInPlayingScene()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("请先运行要验证的游戏场景；此工具不会切换场景或自动重开。");
            if (IsRunning) throw new InvalidOperationException("道具库验证仍在运行。");
            var library = UnityEngine.Object.FindObjectOfType<ItemLibraryUI>();
            if (!library) throw new InvalidOperationException("当前运行场景没有 ItemLibraryUI。");
            results.Clear();
            Succeeded = false;
            LastResult = "RUNNING";
            deadline = EditorApplication.timeSinceStartup + 40;
            running = Validate(library, line => results.Add(line));
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("ItemLibrary validation timed out; release mouse/Space/Enter.");
                if (running.MoveNext()) return;
                Succeeded = true;
                LastResult = "ITEM_LIBRARY_VALIDATION_PASSED: " + results.Count + " checks";
                Debug.Log(LastResult);
                Finish();
            }
            catch (Exception error)
            {
                LastResult = error.ToString();
                Debug.LogException(error);
                Finish();
            }
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            (running as IDisposable)?.Dispose();
            running = null;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/item_library_validation"));
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "result.txt"), results.Concat(new[] { LastResult }));
        }

        /// <summary>Can be yielded directly by a larger migration validation runner.</summary>
        public static IEnumerator Validate(ItemLibraryUI library, Action<string> report = null)
        {
            Action<bool, string> check = (ok, message) => {
                if (!ok) throw new InvalidOperationException("ItemLibrary: " + message);
                report?.Invoke("PASS " + message);
            };
            check(library != null && library.isActiveAndEnabled, "library component is active");
            var controller = library.Controller;
            check(controller != null && controller.IsReady, "gameplay controller is ready");
            check(!library.InputBlocked, "validation begins with library closed");
            check(EventSystem.current != null, "existing EventSystem is active");
            bool originalPause = controller.Paused;
            bool originalLibraryEnabled = library.enabled;
            var placement = library.Placement;
            bool originalPlacementEnabled = placement && placement.enabled;
            var controls = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Selectable>(true)
                .Where(s => s.gameObject.scene == controller.gameObject.scene &&
                            (!library.LibraryCanvas || !s.transform.IsChildOf(library.LibraryCanvas.transform)))
                .ToDictionary(s => s, s => s.interactable);
            var eventSystems = UnityEngine.Object.FindObjectsOfType<EventSystem>(true).Select(e => e.GetInstanceID()).OrderBy(i => i).ToArray();
            int roundId = controller.Snapshot.RoundId;
            var startingIds = controller.LastRandomStarterDeviceIds.ToArray();
            uint starterSeed = controller.LastRandomStarterSeed;
            try
            {
                controller.SetPaused(true);
                library.Open();
                check(library.IsOpen && controller.Paused, "opening preserves an existing pause");
                check(library.LastError.Length == 0, "catalog loads without error");
                check(library.DeviceEntryCount == controller.Balance.Devices.Length, "every current MRes device is listed");
                check(library.DrugEntryCount == controller.Balance.Drugs.Length, "every current drug is listed");
                check(library.TotalEntryCount == library.DeviceEntryCount + library.DrugEntryCount, "catalog total has no extra entries");
                check(library.Font != null, "Chinese TMP font is assigned");
                foreach (char character in "道具药剂装置肾上腺素")
                    check(library.Font.HasCharacter(character, true, true), "Chinese glyph " + character + " is available");
                foreach (var text in library.LibraryCanvas.GetComponentsInChildren<TMP_Text>(true))
                    check(text.font == library.Font, "text uses the configured font: " + text.name);
                var scaler = library.LibraryCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                check(scaler && scaler.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                      scaler.referenceResolution == new Vector2(1920, 1080), "1080p-to-4K reference scaling is configured");
                check(UnityEngine.Object.FindObjectsOfType<EventSystem>(true).Select(e => e.GetInstanceID()).OrderBy(i => i).SequenceEqual(eventSystems),
                    "opening did not create, replace or remove EventSystem");
                check(!placement || !placement.enabled, "placement input is suspended");
                check(!placement || placement.SelectedInstanceId == 0, "pending placement is cancelled without consuming inventory");
                foreach (var hud in UnityEngine.Object.FindObjectsOfType<MarbleGameHud>(true).Where(h => h.Controller == controller))
                    check(!hud.enabled, "legacy IMGUI HUD is suspended");
                foreach (var board in UnityEngine.Object.FindObjectsOfType<MarbleBoardView>(true).Where(b => b.gameObject.scene == controller.gameObject.scene))
                    check(!board.enabled, "legacy board OnGUI is suspended");
                foreach (var shop in UnityEngine.Object.FindObjectsOfType<ShopUI>(true).Where(s => s.Controller == controller))
                    check(!shop.enabled, "shop refresh / continue listeners are suspended");
                foreach (var control in controls.Keys)
                    if (control) check(!control.interactable, "underlying selectable cannot submit: " + control.name);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var hits = new List<RaycastResult>();
                foreach (var point in new[] { new Vector2(Screen.width * .5f, Screen.height * .5f), new Vector2(2, 2) })
                {
                    hits.Clear();
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                    check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(library.LibraryCanvas.transform),
                        "modal receives the top pointer hit at " + point);
                }
                library.SetSearch("");
                library.SetCategory(ItemLibraryUI.Category.Devices);
                check(library.VisibleEntryCount == controller.Balance.Devices.Length, "device category filter");
                foreach (var item in controller.Balance.Devices)
                {
                    check(library.SelectEntry(ItemLibraryUI.Category.Devices, item.Id), "select device " + item.Id);
                    check(library.SelectedDescription == item.Description, "complete original device description " + item.Id);
                }
                library.SetCategory(ItemLibraryUI.Category.Drugs);
                check(library.VisibleEntryCount == controller.Balance.Drugs.Length, "drug category filter");
                foreach (var item in controller.Balance.Drugs)
                {
                    check(library.SelectEntry(ItemLibraryUI.Category.Drugs, item.Id), "select drug " + item.Id);
                    check(library.SelectedDescription == item.Description, "complete original drug description " + item.Id);
                }
                var drug = controller.Balance.Drugs.First();
                library.SetSearch(drug.Name);
                int matching = controller.Balance.Drugs.Count(d =>
                    (d.Name + "\n" + d.Description + "\n" + d.Id).IndexOf(drug.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                check(library.VisibleEntryCount == matching && library.SelectEntry(ItemLibraryUI.Category.Drugs, drug.Id),
                    "Chinese name search finds the intended drug");
                library.SetSearch("zzzz_no_item_matches_987654321");
                check(library.VisibleEntryCount == 0 && library.SelectedId == 0, "empty search result clears previous detail");
                library.SetSearch("");
                library.SetCategory(ItemLibraryUI.Category.All);
                check(library.VisibleEntryCount == library.TotalEntryCount, "clearing search restores all entries without duplicates");
                library.RefreshCatalog();
                check(library.VisibleEntryCount == library.TotalEntryCount, "reloading the catalog does not duplicate rows");

                // Exercise the actual close Button as well as the public API.
                var close = library.LibraryCanvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(b => b.name == "关闭");
                ExecuteEvents.Execute(close.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                    ExecuteEvents.pointerClickHandler);
                check(!library.IsOpen && library.InputBlocked && controller.Paused, "close click keeps input blocked until release");
                while (library.InputBlocked) yield return null;
                check(controller.Paused, "closing restores the original paused state");
                check(!placement || placement.enabled == originalPlacementEnabled, "placement enabled state restored");
                foreach (var pair in controls.Where(p => !p.Value))
                    if (pair.Key) check(!pair.Key.interactable, "previously disabled control stays disabled: " + pair.Key.name);

                if (placement) placement.enabled = false;
                controller.SetPaused(false);
                library.Toggle();
                check(library.IsOpen && controller.Paused, "opening from play pauses simulation");
                library.Toggle();
                while (library.InputBlocked) yield return null;
                check(!controller.Paused, "closing restores the original playing state");
                check(!placement || !placement.enabled, "a previously disabled placement component remains disabled");
                controller.SetPaused(true);
                library.Open();
                library.enabled = false;
                check(!library.InputBlocked && !library.IsOpen && controller.Paused, "disabling the library restores modal state safely");
                library.enabled = true;
                check(UnityEngine.Object.FindObjectsOfType<EventSystem>(true).Select(e => e.GetInstanceID()).OrderBy(i => i).SequenceEqual(eventSystems),
                    "repeated open/close did not create extra EventSystems");
                check(controller.Snapshot.RoundId == roundId &&
                      controller.LastRandomStarterSeed == starterSeed &&
                      controller.LastRandomStarterDeviceIds.SequenceEqual(startingIds),
                    "browsing never restarts or rerolls the game");
            }
            finally
            {
                // Cleanup is synchronous even if a test times out with a modal open.
                library.enabled = false;
                if (placement) placement.enabled = originalPlacementEnabled;
                foreach (var pair in controls) if (pair.Key) pair.Key.interactable = pair.Value;
                if (controller) controller.SetPaused(originalPause);
                library.enabled = originalLibraryEnabled;
                library.SetSearch("");
                library.SetCategory(ItemLibraryUI.Category.All);
            }
        }
    }
}
