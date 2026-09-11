using System;
using UnityEngine;

namespace MarblesECS
{
    public static class GameBalanceLoader
    {
        public static GameBalance Load(TextAsset source = null)
        {
            source = source != null ? source : Resources.Load<TextAsset>("GameBalance");
            if (source == null)
            {
                Debug.LogWarning("GameBalance.json was not found. Using built-in prototype balance.");
                return GameBalance.Default().Copy();
            }
            var balance = JsonUtility.FromJson<GameBalance>(source.text);
            if (balance == null) throw new ArgumentException("Game balance JSON is empty: " + source.name);
            balance.Validate();
            return balance.Copy();
        }
    }
}
