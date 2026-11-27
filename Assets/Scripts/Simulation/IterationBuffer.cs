using System;
using System.Collections.Generic;

namespace MarblesECS
{
    /// <summary>Reuses iteration storage while keeping an in-flight snapshot stable across nested callbacks.</summary>
    internal sealed class IterationBuffer<T>
    {
        internal sealed class Buffer
        {
            internal readonly List<T> Items = new List<T>();
            internal int Readers;
        }

        private readonly Stack<Buffer> spare = new Stack<Buffer>();
        private Buffer cached;
        private int version;

        internal Lease Read<TSource>(int sourceVersion, TSource source, Action<TSource, List<T>> collect)
        {
            if (cached == null || version != sourceVersion)
            {
                Invalidate();
                var buffer = Take();
                try { collect(source, buffer.Items); }
                catch { Recycle(buffer); throw; }
                cached = buffer;
                version = sourceVersion;
            }
            return new Lease(this, cached);
        }

        internal Lease Rent() => new Lease(this, Take());

        internal void Invalidate()
        {
            var previous = cached;
            cached = null;
            if (previous != null && previous.Readers == 0) Recycle(previous);
        }

        private Buffer Take() => spare.Count > 0 ? spare.Pop() : new Buffer();

        private void Recycle(Buffer buffer)
        {
            buffer.Items.Clear();
            spare.Push(buffer);
        }

        internal readonly struct Lease : IDisposable
        {
            private readonly IterationBuffer<T> owner;
            private readonly Buffer buffer;
            internal List<T> Items => buffer.Items;

            internal Lease(IterationBuffer<T> owner, Buffer buffer)
            {
                this.owner = owner;
                this.buffer = buffer;
                buffer.Readers++;
            }

            public void Dispose()
            {
                if (--buffer.Readers == 0 && !ReferenceEquals(buffer, owner.cached)) owner.Recycle(buffer);
            }
        }
    }
}
