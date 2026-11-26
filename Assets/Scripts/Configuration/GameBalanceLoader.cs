using System;
using UnityEngine;

namespace MarblesECS
{
    public static class GameBalanceLoader
    {
        public static GameBalance Load(TextAsset source = null)
        {
            source = source != null ? source : Resources.Load<TextAsset>("GameBalance");
            var balance = source != null ? JsonUtility.FromJson<GameBalance>(source.text) : GameBalance.Default();
            if (balance == null) throw new ArgumentException("Game balance JSON is empty: " + source.name);
            if (source != null && balance.Campaign != null && balance.Campaign.AllDevicesStartingCoins == 0 &&
                !ConfigJson.Read(source.text, source.name).Object(nameof(GameBalance.Campaign)).Has(nameof(CampaignTuning.AllDevicesStartingCoins)))
                balance.Campaign.AllDevicesStartingCoins = CampaignTuning.DefaultAllDevicesStartingCoins;
            var contentAsset = DeviceResourceCatalog.LoadContentAsset();
            balance.Content = JsonUtility.FromJson<GameContent>(contentAsset.text);
            if (balance.Content == null) throw new ArgumentException("GameContent.json is empty.");
            balance.Content.Devices = DeviceResourceCatalog.ForContent(contentAsset).Definitions();
            balance.Validate();
            return balance.Copy();
        }
    }
}
