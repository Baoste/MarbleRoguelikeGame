using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class GameBalance
    {
        public MarbleTuning Marble;
        public CampaignTuning Campaign;
        public StageDefinition[] Stages;
        public GameContent Content;
        // Catalogs are views of GameContent, never a second serialized copy.
        public DeviceDefinition[] Devices
        {
            get => Content != null ? Content.Devices : legacyDevices;
            set { if (Content != null) Content.Devices = value; else legacyDevices = value; }
        }
        public DrugDefinition[] Drugs
        {
            get => Content != null ? Content.Drugs : legacyDrugs;
            set { if (Content != null) Content.Drugs = value; else legacyDrugs = value; }
        }
        private DeviceDefinition[] legacyDevices = Array.Empty<DeviceDefinition>();
        private DrugDefinition[] legacyDrugs = Array.Empty<DrugDefinition>();

        public static GameBalance Default() { return GameBalanceDefaults.Create(); }
        public void Validate() { GameBalanceValidation.Validate(this); }

        public GameBalance Copy()
        {
            Validate();
            var copy = new GameBalance
            {
                Marble = Marble.Copy(), Campaign = Campaign.Copy(),
                Stages = Array.ConvertAll(Stages, value => value.Copy()),
                Content = Content?.Copy()
            };
            if (Content == null)
            {
                copy.Devices = Array.ConvertAll(Devices, value => value.Copy());
                copy.Drugs = Array.ConvertAll(Drugs, value => value.Copy());
            }
            return copy;
        }
    }
}
