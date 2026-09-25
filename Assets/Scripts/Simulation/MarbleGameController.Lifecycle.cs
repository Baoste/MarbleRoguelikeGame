using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        private void Start()
        {
            try
            {
                Balance = Balance ?? GameBalanceLoader.Load(BalanceJson);
                Tuning = Balance.Marble.Copy();
                MarbleTuning runtimeTuning = EnableCampaign ? Balance.Marble.Copy() : CreateRuntimeTuning();
                if (PhysicsRoot == null || PhysicsRoot.parent != null || transform.IsChildOf(PhysicsRoot))
                    throw new InvalidOperationException("PhysicsRoot必须是独立根对象，且不能包含controller。");
                if (LaunchPoint == null) throw new InvalidOperationException("请设置 LaunchPoint。");
                if (!MarbleRules.IsFinite(LauncherMoveHalfWidth) || LauncherMoveHalfWidth < 0f)
                    throw new InvalidOperationException("LauncherMoveHalfWidth must be finite and non-negative.");
                if (!MarbleRules.IsFinite(LauncherMoveSpeed) || LauncherMoveSpeed < 0f)
                    throw new InvalidOperationException("LauncherMoveSpeed must be finite and non-negative.");
                if (!MarbleRules.IsFinite(FixedStep) || FixedStep < 0.002f || FixedStep > 0.03f)
                    throw new InvalidOperationException("FixedStep应在0.002到0.03秒之间。");
                if (MaxStepsPerFrame < 1 || MaxStepsPerFrame > 16)
                    throw new InvalidOperationException("MaxStepsPerFrame应在1到16之间。");
                if (!MarbleRules.IsFinite(GridSize) || GridSize < 0 ||
                    !MarbleRules.IsFinite(PlacementClearance) || PlacementClearance < 0)
                    throw new InvalidOperationException("GridSize and PlacementClearance must be finite and non-negative.");
                if (PlacementObstacleRoots != null)
                    foreach (Transform obstacleRoot in PlacementObstacleRoots)
                        if (obstacleRoot != null && !obstacleRoot.IsChildOf(PhysicsRoot))
                            throw new InvalidOperationException("Placement obstacles must be under PhysicsRoot: " + obstacleRoot.name);
                TargetIdAllocator.EnsureUniqueUnder(PhysicsRoot);
                ValidateZones();
                step = FixedStep;
                maxSteps = MaxStepsPerFrame;
                originalScene = PhysicsRoot.gameObject.scene;
                localScene = SceneManager.CreateScene("Marbles Physics " + GetInstanceID(),
                    new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                localPhysics = localScene.GetPhysicsScene();
                SceneManager.MoveGameObjectToScene(PhysicsRoot.gameObject, localScene);

                var material = MarbleMaterial;
                if (material == null)
                {
                    ownedMaterial = new PhysicMaterial("Marbles Runtime Material")
                    {
                        dynamicFriction = 0.15f, staticFriction = 0.15f, bounciness = 0.6f,
                        frictionCombine = PhysicMaterialCombine.Minimum,
                        bounceCombine = PhysicMaterialCombine.Maximum
                    };
                    material = ownedMaterial;
                }
                simulation = EnableCampaign ? new MarbleSimulation(Balance) : new MarbleSimulation(runtimeTuning, Balance.Content);
                simulation.ConfigureLauncherMovement(LaunchPoint.localPosition, LauncherMoveHalfWidth, LauncherMoveSpeed);
                bridge = new MarblePhysicsBridge(localScene, MarblePrefab, material, simulation.EnqueueContact);
                if (EnableCampaign) simulation.StartSession(bridge);
                else simulation.StartRound(bridge);
                BeginRoundWhenInventoryIsEmpty();
                if (Balance.Content != null && FindObjectOfType<MarblesECS.Presentation.MarbleGameHud>() == null && GetComponent<ContentDrugPanel>() == null)
                    gameObject.AddComponent<ContentDrugPanel>().Controller = this;
                publishedGamblingRoundId = int.MinValue;
                gamblingRevealDeadline = -1f;
                PublishBloodIfChanged();
                Paused = !AutoStart;
                Physics.SyncTransforms();
                RegisterQuitHandler();
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                Cleanup();
                enabled = false;
            }
        }

        private MarbleTuning CreateRuntimeTuning()
        {
            if (!MarbleRules.IsFinite(StartingBlood) || StartingBlood < 0f)
                throw new InvalidOperationException("StartingBlood must be finite and non-negative.");
            if (!MarbleRules.IsFinite(BallsPerSecond) || BallsPerSecond <= 0f)
                throw new InvalidOperationException("BallsPerSecond must be finite and positive.");

            MarbleTuning runtimeTuning = Tuning.Copy();
            runtimeTuning.InitialBlood = StartingBlood;
            runtimeTuning.BloodCapacity = Math.Max(runtimeTuning.BloodCapacity, StartingBlood);
            runtimeTuning.FireIntervalSeconds = 1f / BallsPerSecond;
            runtimeTuning.Validate();
            return runtimeTuning;
        }

        private void ValidateZones()
        {
            var ids = new HashSet<int>();
            foreach (var zone in PhysicsRoot.GetComponentsInChildren<MarbleZone>(true))
            {
                if (!zone.IsValid || !ids.Add(zone.TargetId))
                    throw new InvalidOperationException("区域TargetId必须为正数且全场唯一，倍率/概率必须有效：" + zone.name);
                var found = false;
                foreach (var collider in zone.GetComponentsInChildren<Collider>(true))
                    if (collider.isTrigger) found = true;
                if (!found) throw new InvalidOperationException("区域缺少Trigger Collider：" + zone.name);
            }
        }

        private void Cleanup()
        {
            var releasedBridge = bridge;
            var releasedSimulation = simulation;
            bridge = null;
            simulation = null;
            publishedBlood = float.NaN;
            gamblingRevealDeadline = -1f;
            try { releasedBridge?.Dispose(); }
            finally
            {
                try { releasedSimulation?.Dispose(); }
                finally
                {
                    try { ReleasePhysicsScene(); }
                    finally
                    {
                        if (ownedMaterial != null) Destroy(ownedMaterial);
                        ownedMaterial = null;
                    }
                }
            }
        }

        private void ReleasePhysicsScene()
        {
            if (!quitting && localScene.IsValid() && localScene.isLoaded)
            {
                Scene releasedScene = localScene;
                localScene = default;
                if (PhysicsRoot != null && originalScene.IsValid() && originalScene.isLoaded &&
                    PhysicsRoot.gameObject.scene == releasedScene)
                    SceneManager.MoveGameObjectToScene(PhysicsRoot.gameObject, originalScene);
                sceneUnload = SceneManager.UnloadSceneAsync(releasedScene);
            }
        }
    }
}
