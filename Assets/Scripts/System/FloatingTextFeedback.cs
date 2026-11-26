namespace MarblesECS
{
    internal static class FloatingTextFeedback
    {
        internal static void DeviceAtPose(SimulationContext context, int instanceId, FloatingTextType type, double value, int styleId = 0)
        {
            if (context.FloatingText != null && context.Physics is IMarblePhysicsCommands commands &&
                commands.TryGetDevicePose(instanceId, out var pose))
                context.FloatingText.Enqueue(new FloatingTextEvent(pose.Position, FloatingTextSource.Device, instanceId, type, value,
                    styleId: styleId));
        }

        internal static bool TryParseDeviceType(string kind, out FloatingTextType type)
        {
            switch (kind)
            {
                case "additive": type = FloatingTextType.Additive; return true;
                case "multiplier": type = FloatingTextType.Multiplier; return true;
                case "critical": type = FloatingTextType.Critical; return true;
                case "custom": type = FloatingTextType.Custom; return true;
                default: type = default; return false;
            }
        }
    }
}
