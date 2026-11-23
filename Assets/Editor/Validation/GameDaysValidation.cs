using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static class GameDaysValidation
    {
        [MenuItem("Marbles ECS/Validate Day Configuration")]
        public static void Run()
        {
            var campaign = GameDaysLoader.Load(GameBalanceLoader.Load());
            Debug.Log(campaign.IsConfigured ? $"DAY_CONFIGURATION_VALID: {campaign.Days.Count} days." :
                "DAY_CONFIGURATION_UNCONFIGURED: valid schema with an empty day sequence.");
        }
    }
}
