using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        [Header("测试 / 每局随机初始装置")]
        [Tooltip("开启后，每次进入游戏或重新开局时随机赠送装置，替代固定初始装置。进入下一回合不会重抽，药剂初始库存保持原设置。")]
        public bool EnableRandomStartingDevices;
        [Range(1, 8)]
        [Tooltip("随机抽取的不同装置种类数；传送门等成套装置会完整赠送，并遵守库存上限。")]
        public int RandomStartingDeviceCount = 1;
        [Tooltip("0：每局生成新种子；其他整数：固定抽取结果，方便复现问题。只影响初始装置，不改变计分或商店随机数。")]
        public int RandomStartingDeviceSeed;

        public uint LastRandomStarterSeed { get; private set; }
        public uint[] LastRandomStarterDeviceIds => simulation != null
            ? simulation.GetRandomStartingDeviceIds() : Array.Empty<uint>();

        private void StartCampaignSession()
        {
            LastRandomStarterSeed = EnableRandomStartingDevices
                ? (RandomStartingDeviceSeed != 0
                    ? unchecked((uint)RandomStartingDeviceSeed)
                    : CreateRandomStarterSeed())
                : 0u;
            simulation.StartSession(bridge,
                EnableRandomStartingDevices ? Mathf.Clamp(RandomStartingDeviceCount, 1, 8) : 0,
                LastRandomStarterSeed);
        }

        private static uint CreateRandomStarterSeed()
        {
            // Guid supplies a fresh seed even with domain reload disabled or several quick restarts.
            uint seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
            return seed == 0 ? 0x6D2B79F5u : seed;
        }
    }
}
