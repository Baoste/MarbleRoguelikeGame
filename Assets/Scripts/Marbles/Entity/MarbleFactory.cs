using Unity.Entities;

namespace MarblesECS
{
    internal static class MarbleFactory
    {
        internal static Entity Create(SimulationContext context, MarbleKey key, ShotQuote quote)
        {
            var manager = context.Manager;
            var entity = manager.CreateEntity(typeof(MarbleTag), typeof(StableIdentity), typeof(ShotOrigin),
                typeof(Owner), typeof(OnBoard), typeof(DefinitionRef), typeof(MarbleScore), typeof(MarbleRush),
                typeof(SettlementState), typeof(DespawnState), typeof(Lifetime),
                typeof(Transform3D), typeof(Motion3D), typeof(PhysicsHandle), typeof(MarblePhysicsProperties));
            try
            {
                manager.SetComponentData(entity, new StableIdentity { StableId = checked(++context.NextStableId) });
                manager.SetComponentData(entity, new ShotOrigin { RoundId = key.RoundId, SpawnSequence = key.Sequence,
                    Launcher = context.LauncherEntity, BloodInvestment = quote.Cost });
                manager.SetComponentData(entity, new Owner { Player = context.PlayerEntity });
                manager.SetComponentData(entity, new OnBoard { Board = context.BoardEntity });
                manager.SetComponentData(entity, new DefinitionRef { DefinitionId = context.Tuning.MarbleDefinitionId });
                manager.SetComponentData(entity, new MarbleScore { BaseScore = quote.BaseValue, ScoreMultiplier = quote.LauncherMultiplier });
                manager.SetComponentData(entity, new MarbleRush { BaseRushChance = context.Tuning.BaseRushChance });
                manager.SetComponentData(entity, new MarblePhysicsProperties
                { Restitution = BounceDrugSystem.Restitution(context, out _, out _) });
                manager.SetComponentData(entity, new Lifetime { ExpireTick = checked(context.Round.Tick +
                    (long)System.Math.Ceiling(context.Tuning.MaxLifeSeconds / (double)context.StepSeconds)) });
                manager.AddBuffer<VisitedDeviceData>(entity);
                manager.AddBuffer<MarbleModifier>(entity);
                return entity;
            }
            catch
            {
                manager.DestroyEntity(entity);
                throw;
            }
        }
    }
}
