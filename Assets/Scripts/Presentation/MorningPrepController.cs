using System;
using System.Collections.Generic;
using Cinemachine;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;

namespace MarblesECS.Presentation
{
    /// <summary>Projects the current Day's room objectives and hands off to the existing production controller.</summary>
    [DisallowMultipleComponent]
    public sealed class MorningPrepController : MonoBehaviour
    {
        [Serializable]
        internal struct Anchor
        {
            public string Id;
            public Transform Surface;
        }

        [SerializeField] private MarbleGameController controller;
        [SerializeField] private RoomCameraMovement roomCamera;
        [SerializeField] private CinemachineVirtualCamera machineCamera;
        [SerializeField] private CinemachineBrain brain;
        [SerializeField] private Transform machineInteraction;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Anchor[] roomAnchors;
        [SerializeField, Min(.01f)] private float fadeSeconds = .85f;
        [SerializeField, Min(.1f)] private float interactionRange = 7f;

        private readonly List<(MorningPrepDefinition definition, Transform view)> notes =
            new List<(MorningPrepDefinition definition, Transform view)>();
        private MorningPrepView view;
        private MorningPrepDefinition reading;
        private long wakeVersion = -1;
        private int authoredRoomPriority, authoredMachinePriority, roomPriority;
        private bool enteringMachine;

        internal bool IsReading => reading != null;
        internal float FadeAlpha => view != null ? view.FadeAlpha : 1;
        internal bool CanUseMachine => controller && CanUseMachineFrom(controller.Day);

        private void Start()
        {
            // Initialize the overlay before the first frame on the scene's normal Day entry.
            if (controller && controller.StartWithDayCampaign) PrepareView();
        }

        private void PrepareView()
        {
            if (view != null) return;
            if (!roomCamera || !machineCamera || !brain || !machineInteraction || !font)
                throw new InvalidOperationException("Morning Prep needs both cameras, their Cinemachine Brain, a machine interaction point and a font.");
            var free = roomCamera.GetComponent<CinemachineVirtualCamera>();
            authoredRoomPriority = free.Priority;
            authoredMachinePriority = machineCamera.Priority;
            roomPriority = Mathf.Max(authoredRoomPriority, authoredMachinePriority) + 1;
            free.Priority = roomPriority;
            view = new MorningPrepView(transform, font);
        }

        private void Update()
        {
            if (!controller || !controller.IsReady) return;
            var day = controller.Day;
            if (day.Status == DayCampaignStatus.NotStarted) return;
            PrepareView();

            if (day.Status == DayCampaignStatus.Active && day.Phase == DayPhase.WakeUp)
                UpdateWakeUp(day);
            else
                UpdateAfterWakeUp(day);

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.E) || (IsReading && Input.GetKeyDown(KeyCode.Escape))) TryInteract();
#endif
        }

        private void UpdateWakeUp(DayProgressSnapshot day)
        {
            if (wakeVersion != day.Version) BeginWakeUp(day);
            if (enteringMachine)
            {
                if (CameraReady(machineCamera) && !day.LifecyclePending)
                    controller.TryCompleteWakeUp(day.Version, out _);
                return;
            }
            if (!day.LifecyclePending && CameraReady(roomCamera.Camera))
                view.Fade(Mathf.MoveTowards(view.FadeAlpha, 0, Time.unscaledDeltaTime / Mathf.Max(.01f, fadeSeconds)));

            roomCamera.CanMove = view.FadeAlpha == 0 && !IsReading && !day.LifecyclePending;
            if (!roomCamera.CanMove) { view.ShowPrompt(null); return; }
            if (NearbyNote() != null) view.ShowPrompt("E  Read note");
            else if (CanUseMachineFrom(day)) view.ShowPrompt("E  Use marble machine");
            else view.ShowPrompt(day.MorningPrepRemaining > 0
                ? "Morning Prep   ·   Read the desk note   ·   WASD / RMB"
                : "Morning Prep   ·   Go to the marble machine");
        }

        private void BeginWakeUp(DayProgressSnapshot day)
        {
            ClearNotes();
            reading = null;
            enteringMachine = false;
            wakeVersion = day.Version;
            roomCamera.ResetWakePose();
            machineCamera.Priority = authoredMachinePriority;
            view.Fade(1);
            view.ShowNote(null);
            foreach (var objective in controller.CurrentDayDefinition.MorningPrep)
            {
                var anchor = FindAnchor(objective.AnchorId);
                if (objective.Interaction == MorningPrepInteraction.ReadNote)
                    notes.Add((objective, view.PlaceNote(anchor, objective.Text)));
            }
        }

        private Transform FindAnchor(string id)
        {
            if (roomAnchors != null)
                foreach (var anchor in roomAnchors)
                    if (anchor.Id == id && anchor.Surface) return anchor.Surface;
            throw new InvalidOperationException($"Morning Prep anchor '{id}' is missing from the scene.");
        }

        private void UpdateAfterWakeUp(DayProgressSnapshot day)
        {
            roomCamera.CanMove = false;
            if (machineCamera.Priority <= roomPriority) machineCamera.Priority = roomPriority + 1;
            if (wakeVersion != -1)
            {
                wakeVersion = -1;
                ClearNotes();
                reading = null;
                view.ShowNote(null);
                view.Fade(0);
            }
            if (day.Status == DayCampaignStatus.Completed) { view.ShowPrompt("Day complete"); return; }
            if (day.Phase == DayPhase.DaySettlement)
                view.ShowPrompt(day.LifecyclePending ? null : "E  Continue to rest");
            else if (day.Phase == DayPhase.Rest)
                view.ShowPrompt(day.LifecyclePending ? null : "E  Finish rest");
            else
            {
                view.ShowPrompt(null);
                if (controller.ProductionCommandsAvailable && !controller.HasProductionFeature(ProductionFeature.DevicePlacement) &&
                    controller.Session.Phase == RoundPhase.Build) controller.BeginRound();
            }
        }

        internal bool TryInteract()
        {
            if (view == null || !controller.IsReady || controller.RequiresRestart) return false;
            var day = controller.Day;
            if (day.Status != DayCampaignStatus.Active || day.LifecyclePending) return false;
            if (day.Phase == DayPhase.DaySettlement) return controller.TryCompleteDaySettlement(day.Version, out _);
            if (day.Phase == DayPhase.Rest) return controller.TryCompleteRest(day.Version, out _);
            if (day.Phase != DayPhase.WakeUp || day.Version != wakeVersion || view.FadeAlpha > 0 || enteringMachine) return false;

            if (reading != null)
            {
                if (!controller.TryCompleteMorningPrep(reading.ObjectiveId, day.Version) &&
                    !controller.IsMorningPrepComplete(reading.ObjectiveId)) return false;
                reading = null;
                view.ShowNote(null);
                return true;
            }
            var objective = NearbyNote();
            if (objective != null)
            {
                reading = objective;
                roomCamera.CanMove = false;
                view.ShowPrompt(null);
                view.ShowNote(objective.Text);
                return true;
            }
            if (!CanUseMachineFrom(day)) return false;

            enteringMachine = true;
            roomCamera.CanMove = false;
            view.ShowPrompt(null);
            machineCamera.Priority = roomPriority + 1;
            return true;
        }

        private bool CanUseMachineFrom(DayProgressSnapshot day) =>
            day.Status == DayCampaignStatus.Active && day.Phase == DayPhase.WakeUp && day.Version == wakeVersion &&
            day.MorningPrepRemaining == 0 && !day.LifecyclePending && !enteringMachine && !IsReading &&
            FadeAlpha == 0 && HorizontalDistanceSquared(machineInteraction.position) <= interactionRange * interactionRange;

        private MorningPrepDefinition NearbyNote()
        {
            MorningPrepDefinition nearest = null;
            float shortest = interactionRange * interactionRange;
            foreach (var note in notes)
            {
                float distance = HorizontalDistanceSquared(note.view.position);
                if (distance > shortest || controller.IsMorningPrepComplete(note.definition.ObjectiveId)) continue;
                nearest = note.definition;
                shortest = distance;
            }
            return nearest;
        }

        private float HorizontalDistanceSquared(Vector3 position)
        {
            var offset = position - roomCamera.transform.position;
            return offset.x * offset.x + offset.z * offset.z;
        }

        private bool CameraReady(CinemachineVirtualCamera camera) => brain.ActiveVirtualCamera == camera && !brain.IsBlending;

        private void ClearNotes()
        {
            foreach (var item in notes)
            {
                item.view.gameObject.SetActive(false);
                Destroy(item.view.gameObject);
            }
            notes.Clear();
        }

        private void OnDisable()
        {
            ClearNotes();
            if (view == null) return;
            view.Dispose();
            view = null;
            wakeVersion = -1;
            reading = null;
            if (roomCamera)
            {
                roomCamera.CanMove = false;
                var camera = roomCamera.GetComponent<CinemachineVirtualCamera>();
                if (camera) camera.Priority = authoredRoomPriority;
            }
            if (machineCamera) machineCamera.Priority = authoredMachinePriority;
        }
    }
}
