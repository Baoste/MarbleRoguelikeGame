using System;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public DailyLoadoutSnapshot DailyLoadout
        {
            get
            {
                var definition = CurrentDayDefinition;
                return definition?.DailyLoadout == null ? default : new DailyLoadoutSnapshot(definition.DayId,
                    definition.DailyLoadout, DailyLoadoutSystem.SelectedCount(context), DailyLoadoutSystem.State(context));
            }
        }

        public bool TryToggleLoadoutPeg(long version, uint id) => CanChangeShop && dayFlow.TryTogglePeg(version, id);
        public bool TryConfirmLoadout(long version) => CanChangeShop && dayFlow.TryConfirmLoadout(version);

        public DailyPegSnapshot[] GetDailyPegs()
        {
            var loadout = CurrentDayDefinition?.DailyLoadout;
            if (loadout == null) return Array.Empty<DailyPegSnapshot>();
            var pegs = new DailyPegSnapshot[loadout.UnlockedPegIds.Count];
            for (int i = 0; i < pegs.Length; i++)
            {
                uint id = loadout.UnlockedPegIds[i];
                var definition = CampaignStateUtility.Device(context, id);
                bool required = false;
                foreach (uint item in loadout.RequiredPegIds) required |= item == id;
                pegs[i] = new DailyPegSnapshot
                {
                    DefinitionId = id, Name = definition.Name, Description = definition.Description,
                    InstallPrice = definition.InstallPrice, Durability = definition.Durability, Required = required,
                    Permitted = DailyLoadoutSystem.HasPermission(context, loadout, id),
                    Selected = DailyLoadoutSystem.IsSelected(context, id)
                };
            }
            return pegs;
        }
    }
}
