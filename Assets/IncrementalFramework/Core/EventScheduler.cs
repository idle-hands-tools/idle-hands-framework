using System;
using System.Collections.Generic;

namespace IncrementalFramework
{
    /// <summary>Stable binary min-heap. Cancellation removes entries eagerly, keeping memory bounded.</summary>
    public sealed class EventScheduler<T>
    {
        private readonly List<ScheduledEvent<T>> heap = new List<ScheduledEvent<T>>();
        private readonly Dictionary<long, int> positions = new Dictionary<long, int>();
        private long nextSequence = 1;
        public int Count => heap.Count;
        public long NextSequence => nextSequence;

        public EventToken Schedule(double time, int priority, T payload)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0)
                throw new ArgumentOutOfRangeException(nameof(time));
            if (nextSequence == long.MaxValue) throw new InvalidOperationException("Event sequence exhausted.");
            var entry = new ScheduledEvent<T>(time, priority, nextSequence++, payload);
            Add(entry);
            return entry.Token;
        }

        public bool Cancel(EventToken token)
        {
            if (!positions.TryGetValue(token.Value, out var index)) return false;
            RemoveAt(index);
            return true;
        }

        public bool TryGet(EventToken token, out ScheduledEvent<T> entry)
        {
            if (positions.TryGetValue(token.Value, out int index)) { entry = heap[index]; return true; }
            entry = null; return false;
        }

        public bool TryPeek(out ScheduledEvent<T> entry)
        {
            entry = heap.Count == 0 ? null : heap[0];
            return entry != null;
        }

        public bool TryDequeue(out ScheduledEvent<T> entry)
        {
            if (!TryPeek(out entry)) return false;
            RemoveAt(0);
            return true;
        }

        public IReadOnlyList<ScheduledEvent<T>> Export()
        {
            var copy = heap.ToArray();
            Array.Sort(copy, Compare);
            return Array.AsReadOnly(copy);
        }

        public void Restore(IEnumerable<ScheduledEvent<T>> entries, long sequence)
        {
            if (entries == null || sequence < 1) throw new ArgumentException("Invalid scheduler state.");
            var validated = new List<ScheduledEvent<T>>();
            var ids = new HashSet<long>();
            foreach (var entry in entries)
            {
                if (entry == null || double.IsNaN(entry.Time) || double.IsInfinity(entry.Time) ||
                    entry.Time < 0 || entry.Sequence < 1 || entry.Sequence >= sequence || !ids.Add(entry.Sequence))
                    throw new ArgumentException("Invalid scheduler entry.");
                validated.Add(entry);
            }
            Clear();
            nextSequence = sequence;
            foreach (var entry in validated) Add(entry);
        }

        public void Clear() { heap.Clear(); positions.Clear(); }

        private static int Compare(ScheduledEvent<T> a, ScheduledEvent<T> b)
        {
            var value = a.Time.CompareTo(b.Time);
            if (value == 0) value = a.Priority.CompareTo(b.Priority);
            return value == 0 ? a.Sequence.CompareTo(b.Sequence) : value;
        }

        private void Add(ScheduledEvent<T> entry)
        {
            positions.Add(entry.Sequence, heap.Count);
            heap.Add(entry);
            Up(heap.Count - 1);
        }

        private void Swap(int a, int b)
        {
            var item = heap[a]; heap[a] = heap[b]; heap[b] = item;
            positions[heap[a].Sequence] = a; positions[heap[b].Sequence] = b;
        }

        private void Up(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (Compare(heap[parent], heap[index]) <= 0) break;
                Swap(parent, index); index = parent;
            }
        }

        private void Down(int index)
        {
            while (true)
            {
                int child = index * 2 + 1;
                if (child >= heap.Count) return;
                if (child + 1 < heap.Count && Compare(heap[child + 1], heap[child]) < 0) child++;
                if (Compare(heap[index], heap[child]) <= 0) return;
                Swap(index, child); index = child;
            }
        }

        private void RemoveAt(int index)
        {
            positions.Remove(heap[index].Sequence);
            int last = heap.Count - 1;
            if (index == last) { heap.RemoveAt(last); return; }
            heap[index] = heap[last]; heap.RemoveAt(last);
            positions[heap[index].Sequence] = index;
            if (index > 0 && Compare(heap[index], heap[(index - 1) / 2]) < 0) Up(index);
            else Down(index);
        }
    }
}
