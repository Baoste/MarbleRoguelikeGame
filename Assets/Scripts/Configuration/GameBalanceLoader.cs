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
            var contentAsset = Resources.Load<TextAsset>("GameContent");
            if (contentAsset == null) throw new ArgumentException("Resources/GameContent.json is required for gameplay attributes and catalogs.");
            balance.Content = JsonUtility.FromJson<GameContent>(contentAsset.text);
            if (balance.Content == null) throw new ArgumentException("GameContent.json is empty.");
            balance.Validate();
            return balance.Copy();
        }
    }
}
