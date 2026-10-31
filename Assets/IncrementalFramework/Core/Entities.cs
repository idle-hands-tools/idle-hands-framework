using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace IncrementalFramework.Core
{
    public readonly struct PropertyKey<T>
    {
        public string Name { get; }
        public PropertyKey(string name) { Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Property name required.") : name; }
        public override string ToString() => Name;
    }

    public sealed class FrameworkDiagnostic
    {
        public string Code { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public FrameworkDiagnostic(string code, string message, Exception exception = null)
        { Code = code; Message = message; Exception = exception; }
    }

    public readonly struct ValueChange<T>
    {
        public Value<T> Value { get; }
        public T OldValue { get; }
        public T NewValue { get; }
        public ValueChange(Value<T> value, T before, T after) { Value = value; OldValue = before; NewValue = after; }
    }

    public abstract class Value
    {
        private readonly List<Value> dependencies = new List<Value>();
        internal readonly List<Value> Dependents = new List<Value>();
        public Entity Owner { get; internal set; }
        public string Name { get; internal set; }
        public abstract Type DataType { get; }
        public abstract object BaseBoxed { get; }
        public abstract bool HasFormula { get; }
        public bool IsCalculated { get; protected set; }
        public IReadOnlyList<Value> Dependencies => dependencies.AsReadOnly();
        public abstract object Boxed { get; set; }
        internal abstract Value Copy(Entity owner);
        public Value CopyFor(Entity owner) => Copy(owner);
        internal virtual void ConnectCopy() { }
        internal abstract void Publish(object before, object after);
        internal abstract void AssignSilent(object value);
        internal abstract void PrepareAssignment(object value);
        internal abstract void AcceptCurrent();
        public abstract void BindStorage(Func<object> read, Action<object> write);
        public abstract void Recalculate();
        public abstract void Refresh();
        internal void DependsOn(IEnumerable<Value> values)
        {
            var proposed = values.Distinct().ToArray();
            bool Reaches(Value from, Value target, HashSet<Value> visited)
            {
                if (ReferenceEquals(from, target)) return true;
                return visited.Add(from) && from.Dependencies.Any(x => Reaches(x, target, visited));
            }
            if (proposed.Any(x => Reaches(x, this, new HashSet<Value>()))) throw new ArgumentException("Calculation dependency cycle at " + Name);
            foreach (var dependency in dependencies) dependency.Dependents.Remove(this);
            dependencies.Clear(); dependencies.AddRange(proposed);
            foreach (var dependency in dependencies) dependency.Dependents.Add(this);
        }
    }

    public class Value<T> : Value
    {
        private T stored;
        private T baseValue;
        internal Func<T, T> Transform;
        private T observed;
        private Func<T> read;
        private Action<T> write;
        private Func<Entity, T> calculation;
        private string[] dependencyNames = Array.Empty<string>();
        public override Type DataType => typeof(T);
        public override object BaseBoxed => Transform == null ? (object)Current : baseValue;
        public override bool HasFormula => calculation != null;
        public event Action<ValueChange<T>> Changed;
        public virtual T Current
        {
            get { Owner?.Context.ObserveRead(this); return read == null ? stored : read(); }
            set
            {
                if (IsCalculated) Owner?.Context.Warn("calculated-write", "Direct assignment to calculated Value " + Owner.Id + "." + Name + "; the next recalculation replaces it.");
                object before = Current;
                if (!IsCalculated) baseValue = value;
                AssignSilent(value);
                Owner?.Context.Changed(this, before, Current);
                if (Owner == null) Publish(before, Current);
            }
        }
        public override object Boxed { get => Current; set => Current = (T)value; }
        public Value(T initial = default) { baseValue = stored = observed = initial; }
        public override void BindStorage(Func<object> getter, Action<object> setter) => Bind(() => (T)getter(), value => setter(value), IsCalculated);
        public Value<T> Bind(Func<T> getter, Action<T> setter, bool calculated = false)
        { read = getter ?? throw new ArgumentNullException(nameof(getter)); write = setter ?? throw new ArgumentNullException(nameof(setter)); IsCalculated = calculated; observed = Current; return this; }
        public void Calculate(Func<Entity, T> formula, params string[] dependencies)
        {
            calculation = formula ?? throw new ArgumentNullException(nameof(formula));
            dependencyNames = dependencies ?? Array.Empty<string>();
            DependsOn(dependencyNames.Select(Owner.GetValue));
            IsCalculated = true; Recalculate();
        }
        public override void Recalculate()
        {
            var before = Current;
            T result;
            if (calculation == null) result = baseValue;
            else
            {
                using (Owner.Context.TrackCalculation(this)) result = calculation(Owner);
            }
            AssignSilent(Transform == null ? result : Transform(result));
            IsCalculated = calculation != null || Transform != null || (read != null && IsCalculated);
            Owner.Context.Changed(this, before, Current);
        }
        public override void Refresh()
        { var before = observed; var after = Current; if (!IsCalculated) baseValue = after; if (!EqualityComparer<T>.Default.Equals(before, after)) Owner.Context.Changed(this, before, after); }
        internal override void AcceptCurrent() { observed = Current; }
        internal override void PrepareAssignment(object value) { if (!IsCalculated) baseValue = (T)value; }
        internal override void AssignSilent(object value)
        { if (write == null) stored = (T)value; else write((T)value); }
        internal override void Publish(object before, object after)
        {
            observed = (T)after;
            if (EqualityComparer<T>.Default.Equals((T)before, (T)after) || Changed == null) return;
            var change = new ValueChange<T>(this, (T)before, (T)after);
            foreach (Action<ValueChange<T>> listener in Changed.GetInvocationList())
                try { listener(change); } catch (Exception error) { Owner?.Context.Warn("listener-error", error.Message, error); }
        }
        internal override Value Copy(Entity owner)
        {
            var copy = new Value<T>((T)Entity.CopyOwned(Current)) { Owner = owner, Name = Name, calculation = calculation,
                dependencyNames = (string[])dependencyNames.Clone(), baseValue = (T)Entity.CopyOwned(baseValue), IsCalculated = IsCalculated };
            return copy;
        }
        internal override void ConnectCopy() { if (calculation != null) DependsOn(dependencyNames.Select(Owner.GetValue)); }
    }

    /// <summary>Immutable collection snapshots prevent mutation outside observable writes.</summary>
    public sealed class ValueList<T> : Value<IReadOnlyList<T>>
    {
        public ValueList(IEnumerable<T> initial = null) : base(Array.AsReadOnly((initial ?? Enumerable.Empty<T>()).ToArray())) { }
        public override IReadOnlyList<T> Current { get => base.Current; set => base.Current = Array.AsReadOnly(value.ToArray()); }
        internal override void AssignSilent(object value) => base.AssignSilent(Array.AsReadOnly(((IReadOnlyList<T>)value).ToArray()));
        public void Add(T item) => Current = Array.AsReadOnly(Current.Concat(new[] { item }).ToArray());
        public bool Remove(T item)
        { var list = Current.ToList(); if (!list.Remove(item)) return false; Current = list.AsReadOnly(); return true; }
        public void Replace(int index, T value) { var list = Current.ToArray(); list[index] = value; Current = Array.AsReadOnly(list); }
        internal override Value Copy(Entity owner) => new ValueList<T>(Current.Select(x => (T)Entity.CopyOwned(x))) { Owner = owner, Name = Name };
    }

    public interface IImmutableValue { }

    public abstract class Entity
    {
        private readonly Dictionary<string, Value> values = new Dictionary<string, Value>(StringComparer.Ordinal);
        public string Id { get; protected set; }
        public EntityContext Context { get; internal set; }
        public IReadOnlyDictionary<string, Value> Values => values;
        protected Entity(string id = null, EntityContext context = null) { Id = id ?? Guid.NewGuid().ToString("N"); Context = context ?? new EntityContext(); }
        public Value GetValue(string name) => values.TryGetValue(name, out var value) ? value : throw new ArgumentException("Unknown property " + Id + "." + name);
        public Value<T> GetValue<T>(string name) => GetValue(name) is Value<T> value ? value : throw new ArgumentException("Property type mismatch: " + name);
        public Value<T> GetValue<T>(PropertyKey<T> key) => GetValue<T>(key.Name);
        protected T Get<T>(string name) => values.TryGetValue(name, out var value) ? ((Value<T>)value).Current : default;
        protected void Set<T>(string name, T value)
        { if (!values.ContainsKey(name)) Define(name, value); else GetValue<T>(name).Current = value; }
        protected void SetList<T>(string name, IEnumerable<T> items)
        { if(!values.ContainsKey(name)) DefineList(name,items); else ((ValueList<T>)values[name]).Current=Array.AsReadOnly(items.ToArray()); }
        public Value<T> Define<T>(string name, T initial = default)
        { var value = new Value<T>(initial); Register(name, value); return value; }
        public Value<T> Define<T>(PropertyKey<T> key, T initial = default) => Define(key.Name, initial);
        public ValueList<T> DefineList<T>(string name, IEnumerable<T> initial = null)
        { var value = new ValueList<T>(initial); Register(name, value); return value; }
        public void Register(string name, Value value)
        {
            if (string.IsNullOrWhiteSpace(name) || value == null || values.ContainsKey(name) || (value.Owner != null && !ReferenceEquals(value.Owner,this))) throw new ArgumentException("Invalid or duplicate property: " + name);
            value.Owner = this; value.Name = name; values.Add(name, value);
        }
        [ThreadStatic] private static Dictionary<EntityInstance,EntityInstance> cloneGraph;
        public void CopyValuesTo(Entity target)
        {
            bool outer=cloneGraph==null;
            if(outer)cloneGraph=new Dictionary<EntityInstance,EntityInstance>();
            if(this is EntityInstance source && target is EntityInstance destination)cloneGraph[source]=destination;
            try
            {
                foreach (var pair in values) target.RegisterCopy(pair.Key, pair.Value.Copy(target));
                foreach (var value in target.values.Values) value.ConnectCopy();
                foreach (var value in target.values.Values) if (value.IsCalculated) value.Recalculate();
            }
            finally{if(outer)cloneGraph=null;}
        }
        internal static EntityInstance CloneEntity(EntityInstance source)
        {if(cloneGraph!=null&&cloneGraph.TryGetValue(source,out var existing))return existing;return source.CloneOwned();}
        private void RegisterCopy(string name, Value value) { values[name] = value; }
        internal static object CopyOwned(object value)
        {
            if (value == null || value is string || value.GetType().IsValueType) return value;
            if (value is EntityInstance instance) return CloneEntity(instance);
            if (value is Array array)
            { var copy = (Array)array.Clone(); for (int i = 0; i < copy.Length; i++) copy.SetValue(CopyOwned(copy.GetValue(i)), i); return copy; }
            if (value is IImmutableValue) return value;
            if (value is IList list && value.GetType().IsGenericType)
            {
                var copy = Array.CreateInstance(value.GetType().GetGenericArguments()[0], list.Count);
                for (int i = 0; i < list.Count; i++) copy.SetValue(CopyOwned(list[i]), i);
                return copy;
            }
            if (value is ICloneable cloneable) return cloneable.Clone();
            if (value is IEnumerable) throw new ArgumentException("Use ValueList<T> for owned collections.");
            throw new ArgumentException("Owned mutable properties require an explicit clone: " + value.GetType().Name);
        }
    }

    public class EntityTemplate : Entity
    {
        public virtual string InstanceKind => "entity";
        public EntityTemplate(string id = null) : base(id) { }
        protected virtual void InitializeInstance(EntityInstance instance) { }
        public virtual EntityInstance Instantiate(EntityContext context, string instanceId)
        {
            if(context.Entities.ContainsKey(instanceId))throw new ArgumentException("Duplicate entity identity.");
            using(context.Batch())
            {var instance=new EntityInstance(instanceId,Id,context,InstanceKind);CopyValuesTo(instance);InitializeInstance(instance);context.Add(instance);return instance;}
        }
    }

    public class EntityInstance : Entity
    {
        public string TemplateId { get; protected internal set; }
        public string Kind { get; protected internal set; } = "entity";
        public EntityInstance(string id = null, string templateId = null, EntityContext context = null, string kind = "entity") : base(id, context) { TemplateId = templateId ?? "instance"; Kind=kind; }
        protected virtual EntityInstance CreateClone() => new EntityInstance(Id, TemplateId, new EntityContext()) { Kind = Kind };
        internal EntityInstance CloneOwned()
        { var clone = CreateClone(); CopyValuesTo(clone); return clone; }
    }

    public sealed class EntityContext
    {
        private readonly Dictionary<string, EntityInstance> entities = new Dictionary<string, EntityInstance>(StringComparer.Ordinal);
        private readonly Queue<Action> notifications = new Queue<Action>();
        private readonly Dictionary<Value, (object Before, object After)> changes = new Dictionary<Value, (object, object)>();
        private readonly List<ModifierApplication> modifiers = new List<ModifierApplication>();
        public IReadOnlyList<ModifierApplication> Modifiers => modifiers.AsReadOnly();
        private ulong randomState=1;
        public ulong RandomState { get => randomState; set => randomState=value==0?1:value; }
        public Func<ulong> RandomOverride { get; set; }
        public ulong NextRandomUInt64()
        {if(RandomOverride!=null)return RandomOverride();ulong value=randomState;value^=value<<13;value^=value>>7;value^=value<<17;return randomState=value;}
        public long NextIdentitySequence { get; set; } = 1;
        public string NewId(string prefix) => prefix + ":" + NextIdentitySequence++;
        public event Action<Value, object, object> ValueChanged;
        public void Attach(ModifierApplication application)
        {
            modifiers.RemoveAll(x => x.Id == application.Id && ReferenceEquals(x.Target, application.Target));
            modifiers.Add(application); application.Rebuild(modifiers);
        }
        public void RemoveModifiers(string sourceId)
        {
            var removed = modifiers.Where(x => x.SourceId == sourceId).ToArray();
            modifiers.RemoveAll(x => x.SourceId == sourceId);
            using (Batch()) foreach (var application in removed) application.Rebuild(modifiers);
        }
        private readonly HashSet<Value> calculating = new HashSet<Value>();
        private bool dispatching;
        private int batching;
        private int changeDepth;
        private int suppress;
        private bool lifecycleDirty;
        private readonly Queue<Action> lifecycleNotifications = new Queue<Action>();
        private readonly Stack<HashSet<Value>> observers = new Stack<HashSet<Value>>();
        private readonly Stack<(Value Value, HashSet<Value> Reads)> tracking = new Stack<(Value, HashSet<Value>)>();
        private readonly Dictionary<string, EntityTemplate> templates = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, EntityTemplate> Templates => templates;
        public void RegisterTemplate(EntityTemplate template) { templates[template.Id] = template; }
        internal void ObserveRead(Value value)
        { if (tracking.Count > 0 && !ReferenceEquals(tracking.Peek().Value,value)) tracking.Peek().Reads.Add(value); if(observers.Count>0)observers.Peek().Add(value); }
        public IReadOnlyList<Value> ObserveReads(Action read)
        {var dependencies=new HashSet<Value>();observers.Push(dependencies);try{read();}finally{observers.Pop();}return dependencies.ToArray();}
        internal IDisposable TrackCalculation(Value value)
        { tracking.Push((value,new HashSet<Value>())); return new EndCalculation(this); }
        private sealed class EndCalculation : IDisposable
        {
            private EntityContext context;
            public EndCalculation(EntityContext context){this.context=context;}
            public void Dispose(){if(context==null)return;var top=context.tracking.Pop();top.Value.DependsOn(top.Reads);context=null;}
        }
        public IDisposable RestoreScope() { suppress++; return new EndRestore(this); }
        private sealed class EndRestore : IDisposable
        {
            private EntityContext context;
            public EndRestore(EntityContext context){this.context=context;}
            public void Dispose()
            {if(context==null)return;if(--context.suppress==0){context.lifecycleDirty=false;context.lifecycleNotifications.Clear();context.changes.Clear();context.notifications.Clear();foreach(var entity in context.entities.Values)foreach(var value in entity.Values.Values)value.AcceptCurrent();}context=null;}
        }
        public IReadOnlyDictionary<string, EntityInstance> Entities => entities;
        public event Action<FrameworkDiagnostic> Diagnostic;
        public event Action<EntityInstance> Added;
        public event Action<EntityInstance> Removed;
        public void Warn(string code, string message, Exception error = null)
        { if (Diagnostic != null) foreach (Action<FrameworkDiagnostic> observer in Diagnostic.GetInvocationList()) try { observer(new FrameworkDiagnostic(code, message, error)); } catch (Exception) { } }
        public EntityInstance Find(string id) => entities.TryGetValue(id, out var entity) ? entity : throw new ArgumentException("Unknown entity " + id);
        public void Add(EntityInstance entity)
        {
            if(entity==null||entities.ContainsKey(entity.Id))throw new ArgumentException("Duplicate entity identity.");
            using(Batch())
            { entity.Context=this;entities.Add(entity.Id,entity);lifecycleDirty=true;if(suppress==0)lifecycleNotifications.Enqueue(()=>Added?.Invoke(entity)); }
        }
        public void Remove(string id)
        {
            using(Batch())
            {
            var entity=Find(id); RemoveModifiers(id);
            foreach(var application in modifiers.Where(x=>ReferenceEquals(x.Target,entity)).ToArray())modifiers.Remove(application);
            foreach(var value in entity.Values.Values)value.DependsOn(Array.Empty<Value>());
            entities.Remove(id);lifecycleDirty=true;if(suppress==0)lifecycleNotifications.Enqueue(()=>Removed?.Invoke(entity));
            }
        }
        public IDisposable Batch() { batching++; return new BatchScope(this); }
        private sealed class BatchScope : IDisposable
        { private EntityContext owner; public BatchScope(EntityContext context) { owner = context; } public void Dispose() { if (owner == null) return; if (--owner.batching == 0 && owner.suppress == 0) owner.FinishBatch(); owner = null; } }
        internal void Changed(Value value, object before, object after)
        {
            if (Equals(before, after)) return;
            changeDepth++;
            try
            {
                if (changes.TryGetValue(value, out var old)) changes[value] = (old.Before, after); else changes[value] = (before, after);
                foreach (var dependent in value.Dependents.ToArray())
                {
                    if (!calculating.Add(dependent)) throw new InvalidOperationException("Calculation cycle.");
                    try { dependent.Recalculate(); } finally { calculating.Remove(dependent); }
                }
            }
            finally { if (--changeDepth == 0 && batching == 0 && suppress == 0) Flush(); }
        }
        private void FinishBatch()
        {
            if(lifecycleDirty)
            {
                lifecycleDirty=false;batching++;
                try { foreach(var entity in entities.Values.ToArray())foreach(var value in entity.Values.Values.ToArray())if(value.HasFormula)value.Recalculate(); }
                finally { batching--; }
            }
            while(lifecycleNotifications.Count>0)
                try { lifecycleNotifications.Dequeue()(); } catch(Exception error) { Warn("listener-error",error.Message,error); }
            Flush();
        }
        private void Flush()
        {
            foreach (var change in changes.ToArray())
            { var value = change.Key; var before = change.Value.Before; var after = change.Value.After; notifications.Enqueue(() =>
                {
                    value.Publish(before, after);
                    if (!Equals(before, after) && ValueChanged != null)
                        foreach (Action<Value, object, object> observer in ValueChanged.GetInvocationList())
                            try { observer(value, before, after); } catch (Exception error) { Warn("listener-error", error.Message, error); }
                }); }
            changes.Clear();
            if (dispatching) return;
            dispatching = true;
            try
            {
                int delivered = 0;
                while (notifications.Count > 0)
                {
                    if (++delivered > 1024) { notifications.Clear(); Warn("notification-budget", "Notification cascade exceeded 1024 deliveries."); break; }
                    notifications.Dequeue()();
                }
            }
            finally { dispatching = false; }
        }
        internal object Checkpoint()
        { return (changes.ToArray(),lifecycleNotifications.ToArray(),lifecycleDirty,NextIdentitySequence,RandomState); }
        internal void RestoreCheckpoint(object checkpoint)
        {
            var saved=((KeyValuePair<Value,(object Before,object After)>[] Changes,Action[] Lifecycle,bool Dirty,long Next,ulong Random))checkpoint;
            changes.Clear();foreach(var change in saved.Changes)changes.Add(change.Key,change.Value);
            lifecycleNotifications.Clear();foreach(var notification in saved.Lifecycle)lifecycleNotifications.Enqueue(notification);
            lifecycleDirty=saved.Dirty;NextIdentitySequence=saved.Next;RandomState=saved.Random;
        }
        internal void RestoreMembership(EntityInstance entity,bool present)
        {if(present){entity.Context=this;entities[entity.Id]=entity;}else entities.Remove(entity.Id);}
        internal void RestoreModifiers(ModifierApplication[] originals)
        {
            var affected=modifiers.Concat(originals).ToArray();modifiers.Clear();modifiers.AddRange(originals);
            foreach(var application in affected.GroupBy(x=>(x.Target,x.Property)).Select(x=>x.First()))application.Rebuild(modifiers);
        }

        public Action<Modifier, EntityInstance, double> Scheduler { get; set; }
        public void Schedule(Modifier modifier, EntityInstance target, double delay)
        { if (Scheduler == null) throw new InvalidOperationException("No scheduler attached."); Scheduler(modifier, target, delay); }
        public void Refresh() { using (Batch()) foreach (var entity in entities.Values) foreach (var value in entity.Values.Values) value.Refresh(); }
    }

    public abstract class Modifier : IImmutableValue
    {
        public string Id { get; protected set; }
        protected Modifier(string id = null) { Id = id ?? Guid.NewGuid().ToString("N"); }
        public abstract void Stage(EntityInstance target, EntityTransaction transaction);
    }

    public sealed class SetValueModifier<T> : Modifier
    {
        public PropertyKey<T> Property { get; }
        public T Amount { get; }
        public SetValueModifier(string id, PropertyKey<T> property, T amount) : base(id) { Property = property; Amount = amount; }
        public override void Stage(EntityInstance target, EntityTransaction transaction) => transaction.Set(target.GetValue(Property), Amount);
    }

    public sealed class EntityTransaction
    {
        private readonly List<(Value Value, object Before, object After)> writes = new List<(Value, object, object)>();
        private readonly List<(EntityContext Context, EntityInstance Entity, bool Add)> lifecycle = new List<(EntityContext, EntityInstance, bool)>();
        private readonly List<Func<bool>> requirements = new List<Func<bool>>();
        private bool closed;
        private readonly List<Action> onCommit = new List<Action>();
        private readonly List<ModifierApplication> attached = new List<ModifierApplication>();
        private readonly HashSet<EntityContext> additionalContexts = new HashSet<EntityContext>();
        public void IncludeContext(EntityContext context) { EnsureOpen();additionalContexts.Add(context); }
        public void Attach(ModifierApplication application) { EnsureOpen();attached.Add(application);additionalContexts.Add(application.Target.Context); }
        private readonly Dictionary<EntityContext,(long Original,long Next)> reservations = new Dictionary<EntityContext,(long,long)>();
        public string ReserveId(EntityContext context,string prefix)
        {
            EnsureOpen(); var state=reservations.TryGetValue(context,out var existing)?existing:(context.NextIdentitySequence,context.NextIdentitySequence);
            string id=prefix+":"+state.Item2.ToString("D8",System.Globalization.CultureInfo.InvariantCulture);
            reservations[context]=(state.Item1,state.Item2+1); return id;
        }
        public void OnCommit(Action action) { EnsureOpen(); onCommit.Add(action); }
        public void Require(Func<bool> predicate) { EnsureOpen(); requirements.Add(predicate); }
        public T Read<T>(Value<T> value) { for (int i = writes.Count - 1; i >= 0; i--) if (ReferenceEquals(writes[i].Value, value)) return (T)writes[i].After; return value.Current; }
        public void Set<T>(Value<T> value, T after) { Set(value, (object)after); }
        public void Set(Value value, object after)
        {
            EnsureOpen(); if ((after == null && value.DataType.IsValueType && Nullable.GetUnderlyingType(value.DataType) == null) || (after != null && !value.DataType.IsInstanceOfType(after))) throw new ArgumentException("Property type mismatch.");
            for (int i = 0; i < writes.Count; i++) if (ReferenceEquals(writes[i].Value, value)) { writes[i] = (value, writes[i].Before, after); return; }
            writes.Add((value, value.Boxed, after));
        }
        public void Create(EntityContext context, EntityInstance entity) { EnsureOpen(); lifecycle.Add((context, entity, true)); }
        public void Remove(EntityInstance entity) { EnsureOpen(); lifecycle.Add((entity.Context, entity, false)); }
        private void EnsureOpen() { if (closed) throw new InvalidOperationException("Transaction is closed."); }
        public bool Commit()
        {
            EnsureOpen(); closed = true;
            if (reservations.Any(x=>x.Key.NextIdentitySequence!=x.Value.Original) || requirements.Any(x => !x()) || writes.Any(x => !Equals(x.Before, x.Value.Boxed))) return false;
            var identities = new HashSet<(EntityContext, string)>();
            foreach (var op in lifecycle)
                if (!identities.Add((op.Context, op.Entity.Id)) || op.Add == op.Context.Entities.ContainsKey(op.Entity.Id)) return false;
            var contexts = writes.Select(x => x.Value.Owner.Context).Concat(lifecycle.Select(x => x.Context)).Concat(additionalContexts).Distinct().ToArray();
            var checkpoints=contexts.ToDictionary(context=>context,context=>context.Checkpoint());
            var oldModifiers=contexts.ToDictionary(context=>context,context=>context.Modifiers.ToArray());
            var modifierValues=attached.Select(x=>x.Target.GetValue(x.Property)).Distinct().ToDictionary(value=>value,value=>value.Boxed);
            var batches = contexts.Select(x => x.Batch()).ToArray();
            try
            {
                foreach (var reservation in reservations) reservation.Key.NextIdentitySequence=reservation.Value.Next;
                foreach (var write in writes) { write.Value.PrepareAssignment(write.After); write.Value.AssignSilent(write.After); }
                foreach (var op in lifecycle) { if (op.Add) op.Context.Add(op.Entity); else op.Context.Remove(op.Entity.Id); }
                foreach(var application in attached)application.Target.Context.Attach(application);
                foreach (var action in onCommit) action();
                foreach (var write in writes)
                {
                    if (write.Value.IsCalculated) write.Value.Owner.Context.Warn("calculated-write", "Transaction assigned calculated Value " + write.Value.Name);
                    write.Value.Owner.Context.Changed(write.Value, write.Before, write.After);
                }
            }
            catch
            {
                foreach(var op in lifecycle.AsEnumerable().Reverse())op.Context.RestoreMembership(op.Entity,!op.Add);
                foreach(var write in writes.AsEnumerable().Reverse()){write.Value.PrepareAssignment(write.Before);write.Value.AssignSilent(write.Before);}
                foreach(var context in contexts)context.RestoreModifiers(oldModifiers[context]);
                foreach(var value in modifierValues)value.Key.AssignSilent(value.Value);
                foreach(var context in contexts)context.RestoreCheckpoint(checkpoints[context]);
                throw;
            }
            finally { foreach (var batch in batches.Reverse()) batch.Dispose(); }
            return true;
        }
    }
}
