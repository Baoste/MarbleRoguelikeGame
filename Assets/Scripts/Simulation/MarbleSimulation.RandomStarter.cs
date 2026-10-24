using System;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        private uint[] randomStartingDeviceIds = Array.Empty<uint>();

        public uint[] GetRandomStartingDeviceIds()
        {
            ThrowIfDisposed();
            return (uint[])randomStartingDeviceIds.Clone();
        }

        private void CreateStartingDevices(int randomTypeCount, uint seed)
        {
            randomStartingDeviceIds = Array.Empty<uint>();
            if (randomTypeCount > 0)
            {
                randomStartingDeviceIds = RandomStartingDevices.Select(context.Balance.Devices,
                    randomTypeCount, context.Balance.Campaign.MaxOwnedDevices, seed);
                foreach (uint id in randomStartingDeviceIds)
                    CampaignStateUtility.CreateDevice(context, CampaignStateUtility.Device(context, id), 0);
                return;
            }

            foreach (var device in context.Balance.Devices)
                for (int i = 0; i < device.StartingCount; i++)
                    CampaignStateUtility.CreateDevice(context, device, device.Price);
        }
    }
}
