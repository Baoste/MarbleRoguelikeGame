using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        [Header("随机数")]
        [InspectorName("每次开新局刷新随机种子")]
        [Tooltip("开启时，启动游戏或重新开局会生成新种子。关闭时使用 GameBalance 的 Marble.RandomSeed。商店、装置效果和开奖共用这一条序列，下一回合延续当前序列。")]
        public bool RefreshRandomSeedOnNewRun = true;
        public uint LastRunRandomSeed { get; private set; }

        [Header("测试 / 初始装置")]
        [InspectorName("开局获得全部装置")]
        [Tooltip("勾选后直接启动完整的弹珠机流程。每次开局或重开都会免费获得目录中每种装置一套。成对装置按整套发放。")]
        public bool EnableRandomStartingDevices;
        public uint[] LastRandomStarterDeviceIds => simulation != null
            ? simulation.GetRandomStartingDeviceIds() : Array.Empty<uint>();

        private void StartSimulationSession()
        {
            LastRunRandomSeed = RefreshRandomSeedOnNewRun ? CreateRunRandomSeed() : Balance.Marble.RandomSeed;
            if (EnableCampaign)
                simulation.StartSession(bridge, EnableRandomStartingDevices, LastRunRandomSeed);
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
