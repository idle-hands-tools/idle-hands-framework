using System;
using System.Collections.Generic;

namespace IncrementalFramework
{
    /// <summary>Stable binary min-heap with lazy versioned cancellation and amortized tombstone compaction.</summary>
    public sealed class EventScheduler<T>
    {
        private readonly List<ScheduledEvent<T>> heap = new List<ScheduledEvent<T>>();
        private readonly Dictionary<long, ScheduledEvent<T>> live = new Dictionary<long, ScheduledEvent<T>>();
        private long nextSequence = 1;
        public int Count => live.Count;
        public long NextSequence => nextSequence;
        public int StorageCount => heap.Count;
        public bool HasCancelledHead => heap.Count > 0 && !IsLive(heap[0]);
        public double? NextStoredTime => heap.Count > 0 ? heap[0].Time : (double?)null;
        public int PruneCancelled(int budget)
        {
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
            int removed = 0;
            while (removed < budget && HasCancelledHead) { Pop(); removed++; }
            return removed;
        }

        public EventToken Schedule(double time, int priority, T payload)
        {
            ValidateTime(time);
            if (nextSequence == long.MaxValue) throw new InvalidOperationException("Event sequence exhausted.");
            var entry = new ScheduledEvent<T>(time, priority, nextSequence++, payload);
            live.Add(entry.Sequence, entry); Push(entry);
            return entry.Token;
        }
        public EventToken Reschedule(EventToken token, double time, int priority, T payload)
        {
            ValidateTime(time);
            if (!TryGet(token, out var previous)) throw new ArgumentException("Unknown or stale event token.", nameof(token));
            if (previous.Version == long.MaxValue) throw new InvalidOperationException("Event version exhausted.");
            var replacement = new ScheduledEvent<T>(time, priority, previous.Sequence, payload, previous.Version + 1);
            live[replacement.Sequence] = replacement; Push(replacement); CompactIfNeeded();
            return replacement.Token;
        }
        public bool Cancel(EventToken token)
        {
            if (!TryGet(token, out _)) return false;
            live.Remove(token.Value);
            CompactIfNeeded();
            return true;
        }
        public bool TryGet(EventToken token, out ScheduledEvent<T> entry)
        {
            if (live.TryGetValue(token.Value, out entry) && entry.Version == token.Version) return true;
            entry = null; return false;
        }
        public bool TryPeek(out ScheduledEvent<T> entry)
        {
            while (heap.Count > 0 && !IsLive(heap[0])) Pop();
            entry = heap.Count == 0 ? null : heap[0];
            return entry != null;
        }
        public bool TryDequeue(out ScheduledEvent<T> entry)
        {
            if (!TryPeek(out entry)) return false;
            Pop(); live.Remove(entry.Sequence); CompactIfNeeded();
            return true;
        }
        public IReadOnlyList<ScheduledEvent<T>> Export()
        {
            // Tombstones and superseded versions are deliberately absent from persisted state.
            Compact();
            var copy = new List<ScheduledEvent<T>>(live.Values).ToArray();
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
                if (entry == null || !Finite(entry.Time) || entry.Time < 0 || entry.Sequence < 1 ||
                    entry.Sequence >= sequence || entry.Version < 1 || !ids.Add(entry.Sequence))
                    throw new ArgumentException("Invalid scheduler entry.");
                validated.Add(entry);
            }
            Clear(); nextSequence = sequence;
            foreach (var entry in validated) { live.Add(entry.Sequence, entry); Push(entry); }
        }
        public void Clear() { heap.Clear(); live.Clear(); }
        private bool IsLive(ScheduledEvent<T> entry) =>
            live.TryGetValue(entry.Sequence, out var current) && current.Version == entry.Version;
        private void CompactIfNeeded()
        {
            if (heap.Count <= 64 || heap.Count <= (long)live.Count * 2) return;
            Compact();
        }
        private void Compact()
        {
            if (heap.Count == live.Count) return;
            heap.Clear(); heap.AddRange(live.Values);
            for (int i = heap.Count / 2 - 1; i >= 0; i--) Down(i);
        }
        private static int Compare(ScheduledEvent<T> a, ScheduledEvent<T> b)
        {
            int value = a.Time.CompareTo(b.Time);
            if (value == 0) value = a.Priority.CompareTo(b.Priority);
            return value == 0 ? a.Sequence.CompareTo(b.Sequence) : value;
        }
        private void Push(ScheduledEvent<T> entry)
        {
            heap.Add(entry);
            int index = heap.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (Compare(heap[parent], heap[index]) <= 0) break;
                Swap(parent, index); index = parent;
            }
        }
        private void Pop()
        {
            int last = heap.Count - 1;
            if (last == 0) { heap.RemoveAt(0); return; }
            heap[0] = heap[last]; heap.RemoveAt(last);
            Down(0);
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
        private void Swap(int a, int b)
        { var value = heap[a]; heap[a] = heap[b]; heap[b] = value; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void ValidateTime(double time)
        {
            if (!Finite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        }
    }
}
