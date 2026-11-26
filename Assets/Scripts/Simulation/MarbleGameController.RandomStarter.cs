using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        private const string StartingDeviceBoardLayout = "Day0.json";

        [Header("随机数")]
        [InspectorName("每次开新局刷新随机种子")]
        [Tooltip("开启时，启动游戏或重新开局会生成新种子。关闭时使用 GameBalance 的 Marble.RandomSeed。商店、装置效果和开奖共用这一条序列，下一回合延续当前序列。")]
        public bool RefreshRandomSeedOnNewRun = true;
        public uint LastRunRandomSeed { get; private set; }

        [Header("测试 / 初始装置")]
        [InspectorName("开局获得全部装置")]
        [Tooltip("勾选后使用 Day0 盘面并直接进入机台布置。背包持续补充每种装置，金币按 Campaign.AllDevicesStartingCoins 发放。")]
        public bool EnableRandomStartingDevices;
        public uint[] LastRandomStarterDeviceIds => simulation != null
            ? simulation.GetRandomStartingDeviceIds() : Array.Empty<uint>();

        private void StartSimulationSession()
        {
            LastRunRandomSeed = RefreshRandomSeedOnNewRun ? CreateRunRandomSeed() : Balance.Marble.RandomSeed;
            if (EnableCampaign)
            {
                var layout = EnableRandomStartingDevices ? BoardLayoutLoader.Load(StartingDeviceBoardLayout) : null;
                if (layout != null) bridge.ValidateBoardLayout(layout);
                simulation.StartSession(bridge, EnableRandomStartingDevices, LastRunRandomSeed);
                bridge.ApplyBoardLayout(layout);
            }
            else simulation.StartRound(bridge, LastRunRandomSeed);
        }

        private uint CreateRunRandomSeed()
        {
            uint seed;
            do { seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0); }
            while (seed == 0 || seed == LastRunRandomSeed);
            return seed;
        }
    }
}
