using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal sealed class SimulationContext
    {
        internal readonly World World;
        internal EntityManager Manager => World.EntityManager;
        internal readonly MarbleTuning Tuning;
        internal readonly GameBalance Balance;
        internal readonly GameContent Content;
        internal readonly Entity RoundEntity, PlayerEntity, LauncherEntity, BoardEntity;
        internal readonly EntityQuery MarbleQuery;
        internal readonly EntityQuery DeviceQuery;
        internal readonly Dictionary<MarbleKey, Entity> Marbles = new Dictionary<MarbleKey, Entity>();
        internal readonly List<MarbleContact> Contacts = new List<MarbleContact>();
        internal readonly Queue<MarbleScoreEvent> ScoreEvents = new Queue<MarbleScoreEvent>();
        internal IMarblePhysics Physics;
        internal ulong NextSpawnSequence;
        internal ulong NextStableId;
        internal float StepSeconds;
        internal readonly HashSet<ulong> CloverFamilies = new HashSet<ulong>();
        internal readonly HashSet<ulong> RefundedFamilies = new HashSet<ulong>();

        internal SimulationContext(MarbleTuning tuning, GameBalance balance = null, GameContent content = null)
        {
            Tuning = tuning;
            Balance = balance;
            Content = balance?.Content ?? content;
            World = new World("MarblesECS.OwnedGameplayWorld");
            RoundEntity = Manager.CreateEntity(typeof(RoundData));
            PlayerEntity = Manager.CreateEntity(typeof(PlayerData));
            LauncherEntity = Manager.CreateEntity(typeof(LauncherData), typeof(LauncherMovementConfig),
                typeof(LauncherMovementState), typeof(LauncherMovementInput));
            BoardEntity = Manager.CreateEntity(typeof(StableIdentity));
            Manager.AddComponentData(RoundEntity, new StableIdentity { StableId = ++NextStableId });
            Manager.AddComponentData(PlayerEntity, new StableIdentity { StableId = ++NextStableId });
            Manager.AddComponentData(LauncherEntity, new StableIdentity { StableId = ++NextStableId });
            Manager.SetComponentData(BoardEntity, new StableIdentity { StableId = ++NextStableId });
            Manager.AddBuffer<ActiveScoreEffectData>(PlayerEntity);
            Manager.AddBuffer<ActiveBounceDrugData>(PlayerEntity);
            Manager.AddBuffer<ActiveDrugData>(PlayerEntity);
            Manager.AddComponentData(RoundEntity, new CampaignData());
            Manager.AddBuffer<DrugInventoryData>(PlayerEntity);
            Manager.AddBuffer<ShopOfferData>(RoundEntity);
            MarbleQuery = Manager.CreateEntityQuery(typeof(MarbleTag), typeof(ShotOrigin), typeof(DespawnState));
            DeviceQuery = Manager.CreateEntityQuery(typeof(OwnedDeviceData));
            Round = new RoundData { Phase = (byte)RoundPhase.Lost, TargetScore = tuning.TargetScore };
            Player = new PlayerData { DrugScoreMultiplier = 1 };
            if (Content != null) AttributeRuntime.Initialize(this, PlayerEntity);
        }

        internal RoundData Round { get => Manager.GetComponentData<RoundData>(RoundEntity); set => Manager.SetComponentData(RoundEntity, value); }
        internal PlayerData Player { get => Manager.GetComponentData<PlayerData>(PlayerEntity); set => Manager.SetComponentData(PlayerEntity, value); }
        internal LauncherData Launcher { get => Manager.GetComponentData<LauncherData>(LauncherEntity); set => Manager.SetComponentData(LauncherEntity, value); }
        internal CampaignData Campaign { get => Manager.GetComponentData<CampaignData>(RoundEntity); set => Manager.SetComponentData(RoundEntity, value); }
        internal bool IsRunning => Round.Phase == (byte)RoundPhase.Playing || Round.Phase == (byte)RoundPhase.Draining;

        internal void ClearFireInput()
        {
            var launcher = Launcher;
            launcher.FireHeld = 0;
            launcher.PendingSingleShots = 0;
            Launcher = launcher;
        }

        internal void BeginDraining()
        {
            var round = Round;
            round.Phase = (byte)RoundPhase.Draining;
            round.DrainStartedAt = round.Time;
            Round = round;
            ClearFireInput();
        }

        internal void RemoveAllMarbles()
        {
            using (var entities = MarbleQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var id = Manager.GetComponentData<ShotOrigin>(entities[i]);
                    Physics?.Remove(new MarbleKey(id.RoundId, id.SpawnSequence));
                    Manager.DestroyEntity(entities[i]);
                }
            }
            Marbles.Clear();
            var round = Round;
            round.ActiveMarbleCount = 0;
            Round = round;
        }

        internal bool TryGetFlying(MarbleKey key, out Entity entity)
        {
            if (key.RoundId == Round.RoundId && Marbles.TryGetValue(key, out entity) && Manager.Exists(entity) &&
                !Manager.GetComponentData<DespawnState>(entity).PendingDespawn &&
                !Manager.GetComponentData<SettlementState>(entity).IsSettled) return true;
            entity = Entity.Null;
            return false;
        }

        internal static bool IsFinite(Vector3 value) =>
            MarbleRules.IsFinite(value.x) && MarbleRules.IsFinite(value.y) && MarbleRules.IsFinite(value.z);

        internal static bool IsFinite(Quaternion value) =>
            MarbleRules.IsFinite(value.x) && MarbleRules.IsFinite(value.y) && MarbleRules.IsFinite(value.z) &&
            MarbleRules.IsFinite(value.w) && ((double)value.x * value.x + (double)value.y * value.y +
            (double)value.z * value.z + (double)value.w * value.w) > 0.000001;
    }
}
