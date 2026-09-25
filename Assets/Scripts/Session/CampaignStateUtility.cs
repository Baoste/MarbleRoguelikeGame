using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    internal static class CampaignStateUtility
    {
        internal const long MaxCoins = 9000000000000000L;

        internal static DeviceDefinition Device(SimulationContext context, uint id)
        {
            foreach (var definition in context.Balance.Devices)
                if (definition.Id == id) return definition;
            return null;
        }

        internal static DrugDefinition Drug(SimulationContext context, uint id)
        {
            foreach (var definition in context.Balance.Drugs)
                if (definition.Id == id) return definition;
            return null;
        }

        internal static bool FindDevice(SimulationContext context, int instanceId, out Entity entity)
        {
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                    if (context.Manager.GetComponentData<OwnedDeviceData>(entities[i]).InstanceId == instanceId)
                    { entity = entities[i]; return true; }
            entity = Entity.Null;
            return false;
        }

        internal static void CreateDevice(SimulationContext context, DeviceDefinition definition, long price)
        {
            if (definition.Kind == DeviceKind.Portal)
            {
                int pair = context.Campaign.NextDeviceId + 1;
                CreateSingleDevice(context, definition, price / 2, pair);
                CreateSingleDevice(context, definition, price - price / 2, pair);
                return;
            }
            CreateSingleDevice(context, definition, price, 0);
        }

        private static void CreateSingleDevice(SimulationContext context, DeviceDefinition definition, long price, int pair)
        {
            var campaign = context.Campaign;
            int instanceId = checked(campaign.NextDeviceId + 1);
            var entity = context.Manager.CreateEntity(typeof(OwnedDeviceData), typeof(StableIdentity),
                typeof(Owner), typeof(OnBoard), typeof(DefinitionRef));
            context.Manager.SetComponentData(entity, new OwnedDeviceData
                { InstanceId = instanceId, DefinitionId = definition.Id, PurchasePrice = price, PairId = pair });
            context.Manager.SetComponentData(entity, new StableIdentity { StableId = ++context.NextStableId });
            context.Manager.SetComponentData(entity, new Owner { Player = context.PlayerEntity });
            context.Manager.SetComponentData(entity, new OnBoard { Board = context.BoardEntity });
            context.Manager.SetComponentData(entity, new DefinitionRef { DefinitionId = definition.Id });
            if (AttributeRuntime.Enabled(context))
            {
                AttributeRuntime.Initialize(context, entity);
                AttributeRuntime.SetBase(context, entity, GameAttribute.DEVICE_COOLDOWN, definition.Cooldown);
                AttributeRuntime.SetBase(context, entity, GameAttribute.DEVICE_TRIGGER_RATE, 1);
                AttributeRuntime.SetBase(context, entity, GameAttribute.MACHINE_MULT, 1);
            }
            campaign.NextDeviceId = instanceId;
            campaign.LayoutRevision++;
            context.Campaign = campaign;
        }

        internal static bool CanCashOut(SimulationContext context)
        {
            if (context.Balance == null || context.Round.Phase != (byte)RoundPhase.Playing || context.Player.Blood <= 0)
                return false;
            long confirmed = MarbleRules.AddScore(context.Campaign.TotalScore, context.Round.Score,
                context.Balance.Campaign.MaxTotalScore);
            return confirmed >= context.Round.TargetScore;
        }
    }
}
