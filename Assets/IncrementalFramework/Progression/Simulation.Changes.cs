using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework
{
    public enum SimulationChangeKind { Resource, Producer, Upgrade, Flag, Modifier, Prestige, Module }

    public readonly struct SimulationChangeValue : IEquatable<SimulationChangeValue>
    {
        public BigDouble Amount { get; }
        public BigDouble Rate { get; }
        public BigDouble Lifetime { get; }
        public long Count { get; }
        public bool Enabled { get; }
        public bool Unlocked { get; }
        public object ModuleValue { get; }
        internal SimulationChangeValue(BigDouble amount = default, BigDouble rate = default, BigDouble lifetime = default,
            long count = 0, bool enabled = false, bool unlocked = false, object moduleValue = null)
        { Amount = amount; Rate = rate; Lifetime = lifetime; Count = count; Enabled = enabled; Unlocked = unlocked; ModuleValue = moduleValue; }
        public bool Equals(SimulationChangeValue other) => Amount == other.Amount && Rate == other.Rate &&
            Lifetime == other.Lifetime && Count == other.Count && Enabled == other.Enabled && Unlocked == other.Unlocked &&
            Equals(ModuleValue, other.ModuleValue);
    }

    public readonly struct SimulationChange
    {
        public SimulationChangeKind Kind { get; }
        public int Index { get; }
        public string ModuleId { get; }
        public object ModuleStore { get; }
        public SimulationChangeValue Before { get; }
        public SimulationChangeValue After { get; }
        internal SimulationChange(SimulationChangeKind kind, int index, string moduleId, SimulationChangeValue before, SimulationChangeValue after,
            object moduleStore = null)
        { Kind = kind; Index = index; ModuleId = moduleId; ModuleStore = moduleStore; Before = before; After = after; }
    }

    public sealed class SimulationChangeSet
    {
        public double Time { get; }
        public IReadOnlyList<SimulationChange> Deltas { get; }
        internal SimulationChangeSet(double time, SimulationChange[] deltas) { Time = time; Deltas = Array.AsReadOnly(deltas); }
    }

    public sealed partial class Simulation
    {
        private readonly Dictionary<(SimulationChangeKind, int), SimulationChangeValue> changeBefore =
            new Dictionary<(SimulationChangeKind, int), SimulationChangeValue>();
        private readonly Dictionary<(object, int), (string Id, object Before, object After)> moduleChanges =
            new Dictionary<(object, int), (string, object, object)>();
        private bool journalReady;
        private bool notifying;
        private List<SimulationChange> publishDeltas = new List<SimulationChange>();
        private List<SimulationChange> scratchDeltas = new List<SimulationChange>();
        private SimulationChangeSet lastChanges;
        private bool pendingChangesSnapshot;
        private double lastChangeTime;
        public event Action<SimulationChangeSet> Changed;
        public event Action<Exception> ObserverException;
        public Exception LastObserverException { get; private set; }
        public SimulationChangeSet LastChanges
        {
            get
            {
                if (pendingChangesSnapshot)
                {
                    lastChanges = new SimulationChangeSet(lastChangeTime, publishDeltas.ToArray());
                    pendingChangesSnapshot = false;
                }
                return lastChanges;
            }
        }

        private SimulationChangeValue Capture(SimulationChangeKind kind, int index)
        {
            switch (kind)
            {
                case SimulationChangeKind.Resource: return new SimulationChangeValue(balances[index], EffectiveRate(index), lifetime[index]);
                case SimulationChangeKind.Producer: return new SimulationChangeValue(count: owned[index], enabled: enabled[index], unlocked: producerUnlocked[index]);
                case SimulationChangeKind.Upgrade: return new SimulationChangeValue(enabled: purchased[index], unlocked: upgradeUnlocked[index]);
                case SimulationChangeKind.Flag: return new SimulationChangeValue(enabled: flags[index]);
                case SimulationChangeKind.Modifier: return new SimulationChangeValue(enabled: activeModifiers[index]);
                case SimulationChangeKind.Prestige: return new SimulationChangeValue(PrestigeCurrencyEarned, PermanentMultiplier, count: PrestigeCount);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        private void Mark(SimulationChangeKind kind, int index)
        {
            if (!journalReady || changeBefore.ContainsKey((kind, index))) return;
            changeBefore.Add((kind, index), Capture(kind, index));
        }
        private void MarkReset()
        {
            for (int i = 0; i < balances.Length; i++) Mark(SimulationChangeKind.Resource, i);
            for (int i = 0; i < owned.Length; i++) Mark(SimulationChangeKind.Producer, i);
            for (int i = 0; i < purchased.Length; i++) Mark(SimulationChangeKind.Upgrade, i);
            for (int i = 0; i < flags.Length; i++) Mark(SimulationChangeKind.Flag, i);
            for (int i = 0; i < activeModifiers.Length; i++) Mark(SimulationChangeKind.Modifier, i);
            Mark(SimulationChangeKind.Prestige, 0);
        }
        private void CollectModuleChanges(StateTransaction transaction)
        {
            foreach (var change in transaction.Changes)
            {
                string id = null;
                foreach (var state in moduleStates)
                    if (ReferenceEquals(state.Value, change.Store))
                    {
                        id = state.Key; break;
                    }
                var key = (change.Store, change.Index);
                object before = moduleChanges.TryGetValue(key, out var old) ? old.Before : change.Before;
                moduleChanges[key] = (id, before, change.After);
            }
        }
        private void PublishChanges()
        {
            if (changeBefore.Count == 0 && moduleChanges.Count == 0) return;
            // Snapshot only when observed; headless advances reuse the indexed journal storage.
            scratchDeltas.Clear();
            foreach (var pair in changeBefore)
            {
                var after = Capture(pair.Key.Item1, pair.Key.Item2);
                if (!pair.Value.Equals(after)) scratchDeltas.Add(new SimulationChange(pair.Key.Item1, pair.Key.Item2, null, pair.Value, after));
            }
            foreach (var pair in moduleChanges)
                if (!Equals(pair.Value.Before, pair.Value.After))
                    scratchDeltas.Add(new SimulationChange(SimulationChangeKind.Module, pair.Key.Item2, pair.Value.Id,
                        new SimulationChangeValue(moduleValue: pair.Value.Before), new SimulationChangeValue(moduleValue: pair.Value.After),
                        pair.Key.Item1));
            changeBefore.Clear(); moduleChanges.Clear();
            if (scratchDeltas.Count == 0) return;
            var previous = publishDeltas; publishDeltas = scratchDeltas; scratchDeltas = previous;
            lastChangeTime = Time; pendingChangesSnapshot = true;
            if (Changed == null) return;
            var changes = LastChanges;
            notifying = true;
            try
            {
                foreach (Action<SimulationChangeSet> observer in Changed.GetInvocationList())
                    try { observer(changes); }
                    catch (Exception error)
                    {
                        LastObserverException = error;
                        if (ObserverException != null)
                            foreach (Action<Exception> diagnostic in ObserverException.GetInvocationList())
                                try { diagnostic(error); } catch (Exception) { }
                    }
            }
            finally { notifying = false; }
        }
    }
}
