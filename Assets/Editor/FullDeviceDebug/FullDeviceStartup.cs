using System;
using System.Linq;
using MarblesECS.PhysX;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    [InitializeOnLoad]
    internal static class FullDeviceStartup
    {
        private const string MenuPath = "Marbles ECS/Debug/Full Device Startup";
        private const string ScenePath = "Assets/Scenes/TestScene.unity";
        private const string SettingsPath = "Assets/Editor/FullDeviceDebug/FullDeviceStartup.json";
        private const string Preference = "MarblesECS.EnableRandomStartingDevices";

        private static bool EnableRandomStartingDevices
        {
            get => SessionState.GetBool(Preference, false);
            set => SessionState.SetBool(Preference, value);
        }

        [Serializable]
        private sealed class Settings
        {
            public string dayId;
            public string boardLayout;
            public long initialBiomassScrap;
            public bool skipMorningPrep;
            public uint[] availableDeviceIds;
        }

        static FullDeviceStartup() => SceneManager.sceneLoaded += ConfigureScene;

        [MenuItem(MenuPath)]
        private static void Toggle() => EnableRandomStartingDevices = !EnableRandomStartingDevices;

        [MenuItem(MenuPath, true)]
        private static bool ValidateMenu()
        {
            Menu.SetChecked(MenuPath, EnableRandomStartingDevices);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void ConfigureScene(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isPlaying || !EnableRandomStartingDevices || scene.path != ScenePath) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var controller in root.GetComponentsInChildren<MarbleGameController>(true))
                    if (controller.StartWithDayCampaign) controller.ConfigureDayCampaign(ConfigureDay);
        }

        private static DayCampaignDefinition ConfigureDay(GameBalance balance, DayCampaignDefinition campaign)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SettingsPath);
            var settings = asset != null ? JsonUtility.FromJson<Settings>(asset.text) : null;
            if (settings == null) throw new ArgumentException("Full Device Startup needs its configuration asset.");
            var day = campaign.Days.FirstOrDefault(item => item.DayId == settings.dayId);
            if (day?.DailyLoadout == null) throw new ArgumentException($"Day '{settings.dayId}' needs a daily loadout.");
            var devices = settings.availableDeviceIds;
            if (devices == null || devices.Length == 0)
                devices = balance.Devices.Select(device => device.Id).ToArray();
            var loadout = new DailyLoadoutDefinition(devices, devices, Array.Empty<uint>(), devices.Length, 1,
                LoadoutSelectionMode.AutoSelectAvailable, day.DailyLoadout.Shop);
            var layout = BoardLayoutLoader.Load(settings.boardLayout);
            var production = day.Production.WithSupply(DeviceSupply.SelectedTypes);
            return campaign.WithDays(day.WithSetup(settings.boardLayout, layout, settings.initialBiomassScrap, loadout,
                settings.skipMorningPrep, production));
        }
    }
}
