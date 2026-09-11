using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class GameBalance
    {
        public MarbleTuning Marble;
        public CampaignTuning Campaign;
        public StageDefinition[] Stages;
        public DeviceDefinition[] Devices;
        public DrugDefinition[] Drugs;
        public BoardTuning Board;
        public ZoneDefinition[] Zones;

        public static GameBalance Default() { return GameBalanceDefaults.Create(); }
        public void Validate() { GameBalanceValidation.Validate(this); }

        public GameBalance Copy()
        {
            Validate();
            return new GameBalance
            {
                Marble = Marble.Copy(), Campaign = Campaign.Copy(), Board = Board.Copy(),
                Stages = Array.ConvertAll(Stages, value => value.Copy()),
                Devices = Array.ConvertAll(Devices, value => value.Copy()),
                Drugs = Array.ConvertAll(Drugs, value => value.Copy()),
                Zones = Array.ConvertAll(Zones, value => value.Copy())
            };
        }
    }
}
