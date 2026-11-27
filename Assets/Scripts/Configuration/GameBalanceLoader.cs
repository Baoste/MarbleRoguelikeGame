using System;
using UnityEngine;

namespace MarblesECS
{
    public static class GameBalanceLoader
    {
        public static GameBalance Load(TextAsset source = null)
        {
            source = source != null ? source : Resources.Load<TextAsset>("GameBalance");
            if (source == null) throw new ArgumentException("Resources/GameBalance.json is required.");
            var balance = JsonUtility.FromJson<GameBalance>(source.text);
            if (balance == null) throw new ArgumentException("Game balance JSON is empty: " + source.name);
            var contentAsset = DeviceResourceCatalog.LoadContentAsset();
            balance.Content = JsonUtility.FromJson<GameContent>(contentAsset.text);
            if (balance.Content == null) throw new ArgumentException("GameContent.json is empty.");
            balance.Content.Devices = DeviceResourceCatalog.ForContent(contentAsset).Definitions();
            balance.Validate();
            return balance.Copy();
        }
    }
}
