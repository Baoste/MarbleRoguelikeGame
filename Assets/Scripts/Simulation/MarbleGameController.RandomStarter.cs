using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        [Header("随机数")]
        [InspectorName("每次开新局刷新随机种子")]
        [Tooltip("开启：启动游戏或重新开局时生成一个新种子；关闭：使用 GameBalance 的 Marble.RandomSeed。初始装置、商店、装置效果和开奖共用这一条序列，进入下一回合不刷新。")]
        public bool RefreshRandomSeedOnNewRun = true;
        public uint LastRunRandomSeed { get; private set; }

        [Header("测试 / 每局随机初始装置")]
        [Tooltip("开启后，每次进入游戏或重新开局时随机赠送装置，替代固定初始装置。进入下一回合不会重抽。")]
        public bool EnableRandomStartingDevices;
        [Range(1, 8)]
        [Tooltip("随机抽取的不同装置种类数；传送门等成套装置会完整赠送，并遵守库存上限。")]
        public int RandomStartingDeviceCount = 1;
        public uint[] LastRandomStarterDeviceIds => simulation != null
            ? simulation.GetRandomStartingDeviceIds() : Array.Empty<uint>();

        private void StartSimulationSession()
        {
            LastRunRandomSeed = RefreshRandomSeedOnNewRun ? CreateRunRandomSeed() : Balance.Marble.RandomSeed;
            if (EnableCampaign)
                simulation.StartSession(bridge,
                    EnableRandomStartingDevices ? Mathf.Clamp(RandomStartingDeviceCount, 1, 8) : 0,
                    LastRunRandomSeed);
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
