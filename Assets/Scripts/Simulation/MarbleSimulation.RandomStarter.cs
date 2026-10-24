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

        private void CreateStartingDevices(int randomTypeCount)
        {
            randomStartingDeviceIds = Array.Empty<uint>();
            if (randomTypeCount > 0)
            {
                var round = context.Round;
                randomStartingDeviceIds = RandomStartingDevices.Select(context.Balance.Devices,
                    randomTypeCount, context.Balance.Campaign.MaxOwnedDevices, ref round.RandomState);
                context.Round = round;
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
