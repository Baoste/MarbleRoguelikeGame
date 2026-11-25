using System;
using Unity.Profiling;
using UnityEngine;

namespace MarblesECS
{
    /// <summary>Maintains one presentation batch for one gameplay World or an isolated stress test.</summary>
    internal sealed class FloatingTextSystem : IDisposable
    {
        private static readonly ProfilerMarker EventsMarker = new ProfilerMarker("FloatingText.ProcessEvents");
        private uint revision;

        internal readonly FloatingTextEventBuffer Events;
        internal readonly FloatingTextPool Pool;
        internal readonly FloatingTextBatchRenderer Batch;

        internal FloatingTextSystem(Transform owner, FloatingTextConfig config, int capacityOverride = 0)
        {
            int capacity = capacityOverride == 0 ? config.Capacity : capacityOverride;
            if (!config.Enabled || capacity < 1 || capacity > FloatingTextConfig.MaxCapacity)
                throw new ArgumentException("Floating text needs an enabled config and a supported capacity.");
            var atlas = FloatingTextAtlas.Load(config);
            Events = new FloatingTextEventBuffer(config, Math.Max(config.PendingCapacity, capacity));
            Pool = new FloatingTextPool(config, atlas, capacity);
            Batch = new FloatingTextBatchRenderer(owner, config, atlas, capacity);
        }

        internal void Tick(double now, Camera camera, bool scoresOnly = false)
        {
            using (EventsMarker.Auto())
            {
                if (revision != Events.Revision)
                {
                    Pool.Clear();
                    revision = Events.Revision;
                }
                Pool.Recycle(now, scoresOnly);
                for (int slot = Events.First; slot >= 0; slot = Events.Next(slot))
                {
                    ref readonly var request = ref Events.Get(slot);
                    if (!scoresOnly || request.Event.Type == FloatingTextType.Score) Pool.Accept(request, now);
                }
                Events.ClearPending();
            }
            Batch.Render(Pool, camera, now);
        }

        internal void ClearAll()
        {
            Events.Reset();
            revision = Events.Revision;
            Pool.Clear();
            Batch.Clear();
        }

        public void Dispose() => Batch.Dispose();
    }
}
