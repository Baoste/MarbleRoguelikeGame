using System;

namespace MarblesECS
{
    // Both lists use the same fixed slot indices. The priority lists make eviction independent of capacity.
    internal sealed class FloatingTextSlots
    {
        private struct Links
        {
            internal int Previous, Next, PreviousAtPriority, NextAtPriority;
            internal byte Priority;
        }

        private const int Priorities = byte.MaxValue + 1;
        private readonly Links[] links;
        private readonly int[] heads = new int[Priorities];
        private readonly int[] tails = new int[Priorities];
        private int tail = -1, free = -1, unused;

        internal int Capacity => links.Length;
        internal int Count { get; private set; }
        internal int First { get; private set; } = -1;
        internal int LowestPriority { get; private set; } = Priorities;

        internal FloatingTextSlots(int capacity)
        {
            links = new Links[capacity];
            Array.Fill(heads, -1);
            Array.Fill(tails, -1);
        }

        internal int Next(int index) => links[index].Next;
        internal int FirstAt(int priority) => heads[priority];
        internal int NextAt(int index) => links[index].NextAtPriority;
        internal int OldestBelow(byte priority) => LowestPriority < priority ? heads[LowestPriority] : -1;

        internal int Add(byte priority)
        {
            int index;
            if (free >= 0)
            {
                index = free;
                free = links[index].Next;
            }
            else index = unused++;

            links[index].Previous = tail;
            links[index].Next = -1;
            if (tail >= 0) links[tail].Next = index;
            else First = index;
            tail = index;
            Count++;
            AppendPriority(index, priority);
            return index;
        }

        internal void Remove(int index)
        {
            var item = links[index];
            if (item.Previous >= 0) links[item.Previous].Next = item.Next;
            else First = item.Next;
            if (item.Next >= 0) links[item.Next].Previous = item.Previous;
            else tail = item.Previous;
            Count--;
            RemovePriority(item);
            links[index].Next = free;
            free = index;
        }

        internal void RaisePriority(int index, byte priority)
        {
            var item = links[index];
            if (priority <= item.Priority) return;
            RemovePriority(item);
            AppendPriority(index, priority);
        }

        private void AppendPriority(int index, byte priority)
        {
            links[index].Priority = priority;
            links[index].PreviousAtPriority = tails[priority];
            links[index].NextAtPriority = -1;
            if (tails[priority] >= 0) links[tails[priority]].NextAtPriority = index;
            else heads[priority] = index;
            tails[priority] = index;
            if (priority < LowestPriority) LowestPriority = priority;
        }

        private void RemovePriority(Links item)
        {
            if (item.PreviousAtPriority >= 0) links[item.PreviousAtPriority].NextAtPriority = item.NextAtPriority;
            else heads[item.Priority] = item.NextAtPriority;
            if (item.NextAtPriority >= 0) links[item.NextAtPriority].PreviousAtPriority = item.PreviousAtPriority;
            else tails[item.Priority] = item.PreviousAtPriority;

            if (Count == 0) LowestPriority = Priorities;
            else if (item.Priority == LowestPriority && heads[item.Priority] < 0)
            {
                int next = item.Priority + 1;
                while (next < Priorities && heads[next] < 0) next++;
                LowestPriority = next;
            }
        }

        internal void Clear()
        {
            if (Count != 0)
            {
                Array.Fill(heads, -1);
                Array.Fill(tails, -1);
            }
            First = tail = free = -1;
            Count = unused = 0;
            LowestPriority = Priorities;
        }
    }
}
