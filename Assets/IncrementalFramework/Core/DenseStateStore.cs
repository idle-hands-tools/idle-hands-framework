using System;
using System.Collections.Generic;

namespace IncrementalFramework.Core
{
    public interface IReadOnlyStateStore<T> where T : struct
    {
        int Count { get; }
        T this[int index] { get; }
        string StableId(int index);
        int IndexOf(string id);
    }

    /// <summary>Module-owned value state: stable save keys and dense runtime indexes.</summary>
    public sealed class DenseStateStore<T> : IReadOnlyStateStore<T> where T : struct
    {
        private readonly Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> keys = new List<string>();
        private readonly List<T> values = new List<T>();
        private long version;
        public int Count => values.Count;
        public T this[int index] => values[index];
        public string StableId(int index) => keys[index];
        public int IndexOf(string id) => ids.TryGetValue(id, out int index) ? index
            : throw new ArgumentException("Unknown module state ID: " + id);

        public int Register(string id, T initial)
        {
            if (string.IsNullOrWhiteSpace(id) || ids.ContainsKey(id))
                throw new ArgumentException("Module state ID must be nonempty and unique.");
            int index = values.Count;
            ids.Add(id, index); keys.Add(id); values.Add(initial); version++;
            return index;
        }

        internal IStateWrite Stage(int index, T value)
        {
            _ = values[index];
            return new Write(this, index, value, version);
        }

        private sealed class Write : IStateWrite
        {
            private readonly DenseStateStore<T> store;
            private readonly int index;
            private readonly T value;
            private readonly long expectedVersion;
            public object Store => store;
            public int Index => index;
            public object Before => store.values[index];
            public object After => value;
            public Write(DenseStateStore<T> store, int index, T value, long version)
            { this.store = store; this.index = index; this.value = value; expectedVersion = version; }
            public bool Validate() => store.version == expectedVersion;
            public void Apply() { store.values[index] = value; store.version++; }
        }
    }

    internal interface IStateWrite
    {
        object Store { get; }
        int Index { get; }
        object Before { get; }
        object After { get; }
        bool Validate();
        void Apply();
    }

    public readonly struct StateValueChange
    {
        public object Store { get; }
        public int Index { get; }
        public object Before { get; }
        public object After { get; }
        internal StateValueChange(object store, int index, object before, object after)
        { Store = store; Index = index; Before = before; After = after; }
    }

    /// <summary>Stages module value writes; validation never mutates a store.</summary>
    public sealed class StateTransaction
    {
        private readonly List<IStateWrite> writes = new List<IStateWrite>();
        private bool committed;
        private readonly List<StateValueChange> changes = new List<StateValueChange>();
        public IReadOnlyList<StateValueChange> Changes => changes.AsReadOnly();
        public void Set<T>(DenseStateStore<T> store, int index, T value) where T : struct
        {
            if (committed) throw new InvalidOperationException("Transaction is already committed.");
            if (store == null) throw new ArgumentNullException(nameof(store));
            var write = store.Stage(index, value);
            for (int i = 0; i < writes.Count; i++)
                if (ReferenceEquals(writes[i].Store, store) && writes[i].Index == index)
                { writes[i] = write; return; }
            writes.Add(write);
        }
        public bool Validate()
        {
            if (committed) return false;
            for (int i = 0; i < writes.Count; i++) if (!writes[i].Validate()) return false;
            return true;
        }
        public bool Commit()
        {
            if (!Validate()) return false;
            // No user callbacks occur between validation and these nonthrowing indexed writes.
            for (int i = 0; i < writes.Count; i++)
            {
                var write = writes[i];
                object before = write.Before, after = write.After;
                if (!Equals(before, after)) changes.Add(new StateValueChange(write.Store, write.Index, before, after));
                write.Apply();
            }
            committed = true;
            return true;
        }
    }
}
