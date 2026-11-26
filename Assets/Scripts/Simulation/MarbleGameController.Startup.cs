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

        private void StartSimulationSession()
        {
            LastRunRandomSeed = RefreshRandomSeedOnNewRun ? CreateRunRandomSeed() : Balance.Marble.RandomSeed;
            if (EnableCampaign) simulation.StartSession(bridge, LastRunRandomSeed);
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
