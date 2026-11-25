namespace MarblesECS
{
    internal readonly struct FloatingTextRequest
    {
        internal readonly FloatingTextEvent Event;
        internal readonly FloatingTextStyle Style;
        internal byte Priority => Event.Priority ?? Style.Priority;

        internal FloatingTextRequest(in FloatingTextEvent text, FloatingTextStyle style) { Event = text; Style = style; }
    }

    /// <summary>One bounded presentation queue per simulation. The simulation thread only writes value events.</summary>
    internal sealed class FloatingTextEventBuffer
    {
        private readonly FloatingTextConfig config;
        private readonly FloatingTextSlots slots;
        private readonly FloatingTextRequest[] requests;

        internal uint Revision { get; private set; }
        internal long Received { get; private set; }
        internal long Dropped { get; private set; }
        internal MarbleContact? WheelAnchor { get; set; }
        internal int First => slots.First;
        internal int Next(int slot) => slots.Next(slot);
        internal ref readonly FloatingTextRequest Get(int slot) => ref requests[slot];

        internal FloatingTextEventBuffer(FloatingTextConfig config, int pendingCapacity)
        {
            this.config = config;
            slots = new FloatingTextSlots(pendingCapacity);
            requests = new FloatingTextRequest[pendingCapacity];
        }

        internal void Enqueue(in FloatingTextEvent text)
        {
            if (!CanAccept(text.Type) || !MarbleRules.IsFinite(text.Value) || !SimulationContext.IsFinite(text.Position)) return;
            Received++;
            var request = new FloatingTextRequest(text, config.Resolve(text));
            if (slots.Count == slots.Capacity)
            {
                Dropped++;
                int replace = slots.OldestBelow(request.Priority);
                if (replace < 0) return;
                slots.Remove(replace);
            }
            requests[slots.Add(request.Priority)] = request;
        }

        private bool CanAccept(FloatingTextType type) => (byte)type <= (byte)FloatingTextType.PinContact &&
            (type != FloatingTextType.PinContact || config.ShowNeutralPins);

        internal void ClearPending() => slots.Clear();

        // Reset must also invalidate labels already carried across rendered frames.
        internal void Reset()
        {
            WheelAnchor = null;
            ClearPending();
            Revision++;
        }
    }
}
