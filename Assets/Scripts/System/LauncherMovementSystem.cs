using UnityEngine;

namespace MarblesECS
{
    internal static class LauncherMovementSystem
    {
        internal static void Execute(SimulationContext context, float seconds)
        {
            if (!context.IsRunning) return;
            var manager = context.Manager;
            var entity = context.LauncherEntity;
            var config = manager.GetComponentData<LauncherMovementConfig>(entity);
            var state = manager.GetComponentData<LauncherMovementState>(entity);
            var input = manager.GetComponentData<LauncherMovementInput>(entity);
            float center = config.InitialLocalPosition.x;
            if (input.Automatic != 0)
            {
                if (config.HalfWidth > 0 && config.Speed > 0)
                {
                    double cycle = config.HalfWidth * 4d;
                    state.Travel = (state.Travel + config.Speed * (double)seconds) % cycle;
                    state.LocalPosition.x = center + Mathf.PingPong(
                        config.HalfWidth + (float)state.Travel, config.HalfWidth * 2) - config.HalfWidth;
                }
            }
            else
            {
                float x = input.HasPositionRequest != 0 ? input.RequestedX :
                    state.LocalPosition.x + Mathf.Clamp(input.Horizontal, -1, 1) * config.Speed * seconds;
                state.LocalPosition.x = Mathf.Clamp(x, center - config.HalfWidth, center + config.HalfWidth);
            }
            input.HasPositionRequest = 0;
            manager.SetComponentData(entity, state);
            manager.SetComponentData(entity, input);
        }

        internal static void Reset(SimulationContext context)
        {
            var position = context.Manager.GetComponentData<LauncherMovementConfig>(context.LauncherEntity).InitialLocalPosition;
            context.Manager.SetComponentData(context.LauncherEntity, new LauncherMovementState { LocalPosition = position });
            context.Manager.SetComponentData(context.LauncherEntity, new LauncherMovementInput());
        }
    }
}
