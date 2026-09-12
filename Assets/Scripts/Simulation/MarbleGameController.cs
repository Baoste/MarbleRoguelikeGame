using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.PhysX
{
    /// <summary>唯一固定步驱动：玩法前处理 → 本地 PhysX → 玩法后处理。</summary>
    [DisallowMultipleComponent]
    public sealed partial class MarbleGameController : MonoBehaviour
    {
        public MarbleTuning Tuning = new MarbleTuning();
        [Header("Gameplay")]
        [Min(0f)] public float StartingBlood = 100f;
        [Min(0.01f)] public float BallsPerSecond = 6.666667f;
        [Min(0.01f)] public float BallLaunchSpeed = 12f;
        public Transform LaunchPoint;
        [Tooltip("独立根对象；所有参与弹珠模拟的台面/区域/装置放在它下面。")]
        public Transform PhysicsRoot;
        [Tooltip("可留空，自动生成球。自定义时为直径1的球，根上唯一SphereCollider。")]
        public GameObject MarblePrefab;
        public PhysicMaterial MarbleMaterial;
        [Range(0.002f, 0.03f)] public float FixedStep = 1f / 60f;
        [Range(1, 16)] public int MaxStepsPerFrame = 4;
        public bool AutoStart = true;
        public bool AutoMoveLauncher = true;
        [Min(0f)] public float LauncherMoveHalfWidth = 5.2f;
        [Min(0f)] public float LauncherMoveSpeed = 2.5f;

        public bool Paused { get; private set; }
        public bool RequiresRestart { get; private set; }
        public bool IsReady => simulation != null && simulation.IsAlive;
        public MarbleSnapshot Snapshot => !IsReady ? default : simulation.Snapshot;
        public event Action<MarbleScoreEvent> Scored;

        private MarbleSimulation simulation;
        private MarblePhysicsBridge bridge;
        private PhysicsScene localPhysics;
        private Scene localScene;
        private Scene originalScene;
        private PhysicMaterial ownedMaterial;
        private double accumulator;
        private bool externalFireHeld;
        private bool submittedFireHeld;
        private float step;
        private int maxSteps;
        private bool quitting;


        public void SetFireHeld(bool held)
        {
            externalFireHeld = held;
            UpdateFireInput();
        }

        public void FireOnce() { if (simulation != null && !Paused) simulation.FireOnce(); }
        public void SetFlow(float normalized) { simulation?.SetFlow(normalized); }
        public void EndFiring() { if (simulation != null && !Paused) simulation.EndFiring(); }

        public bool ApplyScoreDrug(double multiplier, float durationSeconds)
        {
            return simulation != null && !Paused && simulation.ApplyScoreDrug(multiplier, durationSeconds);
        }

        public void SetPaused(bool paused)
        {
            if (!paused && RequiresRestart) return;
            Paused = paused;
            accumulator = 0;
            externalFireHeld = false;
            if (IsReady) simulation.ClearFireInput();
            submittedFireHeld = false;
        }

        public void RestartRound()
        {
            if (simulation == null || bridge == null) return;
            if (EnableCampaign) simulation.StartSession(bridge);
            else simulation.StartRound(bridge);
            externalFireHeld = submittedFireHeld = false;
            SyncLauncherTransform();
            accumulator = 0;
            RequiresRestart = false;
            Paused = false;
        }

        private void UpdateFireInput()
        {
            if (!IsReady)
                return;

            bool canFire = !Paused && simulation.Snapshot.Phase == RoundPhase.Playing;
            bool held = canFire && (externalFireHeld || SpaceHeld());
            if (held == submittedFireHeld)
                return;

            simulation.SetFireHeld(held);
            submittedFireHeld = held;
        }


        private void FlushScores()
        {
            while (simulation != null && simulation.TryDequeueScore(out var score))
            {
                // 表现层异常不撤销已经提交的玩法结算。
                try { Scored?.Invoke(score); }
                catch (Exception error) { Debug.LogException(error, this); }
            }
        }


        private void OnDisable()
        {
            accumulator = 0;
            externalFireHeld = false;
            submittedFireHeld = false;
            if (IsReady) simulation.ClearFireInput();
        }
        // private void OnApplicationFocus(bool focused) { if (!focused) SetPaused(true); }
        // private void OnApplicationPause(bool paused) { if (paused) SetPaused(true); }
        private void OnApplicationQuit() { quitting = true; Cleanup(); }
        private void OnDestroy() { UnregisterQuitHandler(); Cleanup(); }

    }
}
