using System;
using System.Linq;
using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    internal struct DailyLoadoutData : IComponentData
    {
        internal bool Confirmed;
        internal int ShopDraws, Revision;
    }

    internal struct SelectedPegType : IBufferElementData { internal uint Id; }
    internal struct PegPermissionData : IBufferElementData
    {
        internal uint Id;
        internal PegPermissionScope Scope;
    }
    internal static class DailyLoadoutSystem
    {
        internal static void Initialize(SimulationContext context)
        {
            context.Manager.AddComponent<DailyLoadoutData>(context.RoundEntity);
            context.Manager.AddBuffer<SelectedPegType>(context.RoundEntity);
            context.Manager.AddBuffer<PegPermissionData>(context.RoundEntity);
        }

        internal static DailyLoadoutData State(SimulationContext context) =>
            context.Manager.GetComponentData<DailyLoadoutData>(context.RoundEntity);

        internal static void StartDay(SimulationContext context, DailyLoadoutDefinition definition)
        {
            var permissions = context.Manager.GetBuffer<PegPermissionData>(context.RoundEntity);
            for (int i = permissions.Length - 1; i >= 0; i--)
                if (permissions[i].Scope != PegPermissionScope.CampaignPermanent) permissions.RemoveAt(i);
            var selected = context.Manager.GetBuffer<SelectedPegType>(context.RoundEntity);
            selected.Clear();
            foreach (uint id in definition.RequiredPegIds) selected.Add(new SelectedPegType { Id = id });
            if (definition.SelectionMode == LoadoutSelectionMode.AutoSelectAvailable)
                foreach (uint id in definition.UnlockedPegIds)
                {
                    if (selected.Length >= definition.MaxLoadoutTypeCount) break;
                    if (!IsSelected(context, id) && HasPermission(context, definition, id))
                        selected.Add(new SelectedPegType { Id = id });
                }
            context.Manager.SetComponentData(context.RoundEntity, new DailyLoadoutData());
        }

        internal static bool HasPermission(SimulationContext context, DailyLoadoutDefinition definition, uint id)
        {
            if (!definition.UnlockedPegIds.Contains(id)) return false;
            if (definition.DefaultPegIds.Contains(id)) return true;
            foreach (var permission in context.Manager.GetBuffer<PegPermissionData>(context.RoundEntity))
                if (permission.Id == id) return true;
            return false;
        }

        internal static bool IsSelected(SimulationContext context, uint id)
        {
            foreach (var type in context.Manager.GetBuffer<SelectedPegType>(context.RoundEntity))
                if (type.Id == id) return true;
            return false;
        }

        internal static bool Toggle(SimulationContext context, DailyLoadoutDefinition definition, uint id)
        {
            if (!HasPermission(context, definition, id) || definition.RequiredPegIds.Contains(id)) return false;
            var selected = context.Manager.GetBuffer<SelectedPegType>(context.RoundEntity);
            for (int i = 0; i < selected.Length; i++)
                if (selected[i].Id == id)
                {
                    selected.RemoveAt(i);
                    SetConfirmed(context, false);
                    return true;
                }
            if (selected.Length >= definition.MaxLoadoutTypeCount) return false;
            selected.Add(new SelectedPegType { Id = id });
            SetConfirmed(context, false);
            return true;
        }

        internal static int SelectedCount(SimulationContext context) =>
            context.Manager.GetBuffer<SelectedPegType>(context.RoundEntity).Length;

        internal static bool Confirm(SimulationContext context, DailyLoadoutDefinition definition)
        {
            if (SelectedCount(context) < definition.MinLoadoutTypeCount) return false;
            SetConfirmed(context, true);
            return true;
        }

        private static void SetConfirmed(SimulationContext context, bool confirmed)
        {
            var state = State(context);
            state.Confirmed = confirmed;
            state.Revision++;
            context.Manager.SetComponentData(context.RoundEntity, state);
        }

        internal static void RecordPurchase(SimulationContext context, uint id, PegPermissionScope scope)
        {
            context.Manager.GetBuffer<PegPermissionData>(context.RoundEntity).Add(new PegPermissionData { Id = id, Scope = scope });
            var state = State(context);
            state.ShopDraws++;
            state.Revision++;
            context.Manager.SetComponentData(context.RoundEntity, state);
        }

        internal static void ClearRunPermissions(SimulationContext context)
        {
            var permissions = context.Manager.GetBuffer<PegPermissionData>(context.RoundEntity);
            for (int i = permissions.Length - 1; i >= 0; i--)
                if (permissions[i].Scope == PegPermissionScope.RunOnly) permissions.RemoveAt(i);
        }

    }

    public struct DailyPegSnapshot
    {
        public uint DefinitionId;
        public string Name, Description;
        public long InstallPrice;
        public int Durability;
        public bool Permitted, Selected, Required;
    }

    public readonly struct DailyLoadoutSnapshot
    {
        public string DayId { get; }
        public int SelectedCount { get; }
        public int Capacity { get; }
        public bool Confirmed { get; }
        public bool CanConfirm { get; }
        public int Draws { get; }
        public int Revision { get; }

        internal DailyLoadoutSnapshot(string dayId, DailyLoadoutDefinition definition, int selected, DailyLoadoutData state)
        {
            DayId = dayId;
            Capacity = definition.MaxLoadoutTypeCount;
            SelectedCount = selected;
            Confirmed = state.Confirmed;
            CanConfirm = selected >= definition.MinLoadoutTypeCount;
            Draws = state.ShopDraws;
            Revision = state.Revision;
        }
    }
}
