using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Persistence;
using IncrementalFramework.Progression;
using Newtonsoft.Json;

namespace IncrementalFramework.Features
{
    public static class FeatureJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error,
            MaxDepth = 64, Converters = { new BigDoubleJsonConverter() }
        };
        public static string Write(object value) => JsonConvert.SerializeObject(value, Settings);
        public static T Read<T>(string value) => JsonConvert.DeserializeObject<T>(value, Settings);
        public static T Copy<T>(T value) => Read<T>(Write(value));
        public static string Hash(object value)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Write(value)))).Replace("-", "");
        }
        public static void Require(bool condition, string message)
        { if (!condition) throw new ArgumentException(message); }
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class FeatureEvent
    {
        public string Kind { get; set; }
        public string Target { get; set; }
        public FeatureEvent() { }
        public FeatureEvent(string kind, string target = "") { Kind = kind; Target = target; }
    }

    public class FeatureState
    {
        public long NextSequence { get; set; } = 1;
        public List<ScheduledEvent<FeatureEvent>> Events { get; set; } = new List<ScheduledEvent<FeatureEvent>>();
        public bool Won { get; set; }
        public string Message { get; set; } = "Ready.";
        public void Schedule(double time, string kind, string target = "", int priority = 0)
        {
            var queue = Queue();
            queue.Schedule(time, priority, new FeatureEvent(kind, target));
            Store(queue);
        }
        public EventScheduler<FeatureEvent> Queue()
        {
            var queue = new EventScheduler<FeatureEvent>(); queue.Restore(Events, NextSequence); return queue;
        }
        public void Store(EventScheduler<FeatureEvent> queue)
        { Events = queue.Export().ToList(); NextSequence = queue.NextSequence; }
    }

    public sealed class FeatureCommand : ISimulationCommand
    {
        public string Action { get; }
        public string Target { get; }
        public string Argument { get; }
        public double Amount { get; }
        public int Quantity { get; }
        public FeatureCommand(string action, string target = "", string argument = "", double amount = 0, int quantity = 10)
        { Action = action; Target = target; Argument = argument; Amount = amount; Quantity = quantity; }
    }
    public struct FeatureCell { public string Json; }
    public sealed class FeatureEnvelope<T>
    {
        public string Fingerprint { get; set; }
        public T State { get; set; }
    }
    public interface IFeatureModule : IGameModule, ISnapshotSection
    {
        double? NextTime { get; }
        bool RunsOffline { get; }
        double? AdvanceLimit { get; }
        bool IsWon { get; }
        string Message { get; }
    }

    /// <summary>Atomic copy-on-command module state. Private scheduler payloads live in an explicit snapshot section,
    /// rather than adding sample-specific event kinds to the economy kernel.</summary>
    public abstract class FeatureModule<T> : IFeatureModule, ICommandHandler<FeatureCommand> where T : FeatureState
    {
        private readonly DenseStateStore<FeatureCell> store = new DenseStateStore<FeatureCell>();
        private readonly string fingerprint;
        private string cachedJson;
        private T cachedState;
        private T ReadState()
        {
            string json=store[0].Json;
            if(!ReferenceEquals(json,cachedJson)){cachedState=FeatureJson.Read<T>(json);cachedJson=json;}
            return cachedState;
        }
        public abstract string Id { get; }
        public int Version => 1;
        public virtual bool RunsOffline => true;
        public virtual double? AdvanceLimit => null;
        public T State => FeatureJson.Read<T>(store[0].Json);
        public double? NextTime => ReadState().Events.Count == 0 ? (double?)null : ReadState().Events.Min(e => e.Time);
        public bool IsWon => ReadState().Won;
        public string Message => ReadState().Message;
        protected FeatureModule(T initial, object content)
        { fingerprint = FeatureJson.Hash(content); store.Register("state", new FeatureCell { Json = FeatureJson.Write(initial) }); }
        public virtual void Register(ModuleBuilder builder)
        { builder.State(Id, store); builder.Command<FeatureCommand>(this); builder.Snapshot(this); }
        public CommandResult Build(FeatureCommand command, SimulationReadModel model, SimulationTransaction transaction)
        {
            var state = State;
            try
            {
                if (!FeatureJson.Finite(command.Amount)) return CommandResult.Fail("Amount must be finite.");
                if (command.Action == "__event")
                {
                    var queue = state.Queue();
                    if (!queue.TryDequeue(out var next) || next.Time > model.Time)
                        return CommandResult.Fail("No event is due.");
                    state.Store(queue); OnEvent(state, next.Payload, model.Time);
                }
                else
                {
                    string error = Act(state, command, model.Time);
                    if (error != null) return CommandResult.Fail(error);
                }
                ValidateState(state);
                transaction.Set(store, 0, new FeatureCell { Json = FeatureJson.Write(state) });
                return CommandResult.Ok();
            }
            catch (ArgumentException e) { return CommandResult.Fail(e.Message); }
            catch (OverflowException) { return CommandResult.Fail("The action exceeds a supported integer range."); }
        }
        protected abstract string Act(T state, FeatureCommand command, double time);
        protected virtual void OnEvent(T state, FeatureEvent evt, double time) { }
        protected virtual void ValidateState(T state)
        {
            FeatureJson.Require(state != null && state.Events != null && state.Events.Count <= 100000, "Invalid module state.");
            _ = state.Queue();
            FeatureJson.Require(state.Events.All(e => e.Payload != null && !string.IsNullOrEmpty(e.Payload.Kind)), "Invalid event payload.");
        }
        public string Capture() => FeatureJson.Write(new FeatureEnvelope<T> { Fingerprint = fingerprint, State = State });
        private T Decode(int version, string json)
        {
            FeatureJson.Require(version == Version && json != null && json.Length <= SnapshotService.MaximumSnapshotBytes, "Unsupported feature snapshot.");
            var envelope = FeatureJson.Read<FeatureEnvelope<T>>(json);
            FeatureJson.Require(envelope != null && envelope.Fingerprint == fingerprint, "Feature content differs; migrate the save explicitly.");
            ValidateState(envelope.State); return envelope.State;
        }
        public void Validate(int version, string data) => Decode(version, data);
        public void Restore(int version, string data)
        {
            var value = Decode(version, data); var tx = new StateTransaction();
            tx.Set(store, 0, new FeatureCell { Json = FeatureJson.Write(value) });
            if (!tx.Commit()) throw new InvalidOperationException("Stale feature restore.");
        }
    }

    /// <summary>Boundary integrator, analogous to DeclarativeRuntime. A fresh module is required on restore.</summary>
    public sealed class FeatureGame
    {
        public const double OfflineCap = 8 * 60 * 60;
        public IFeatureModule Module { get; }
        public Simulation Simulation { get; private set; }
        public double Time => Simulation.Time;
        public FeatureGame(IFeatureModule module, ulong seed = 1)
        { Module = module; Simulation = new ModuleBuilder().Add(module).Create(seed); }
        public CommandResult Execute(string action, string target = "", string argument = "", double amount = 0, int quantity = 10)
            => Simulation.Execute(new FeatureCommand(action, target, argument, amount, quantity));
        public bool AdvanceTo(double target, int budget = 256) =>
            AdvanceTimed(target,budget).Status != IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.BudgetExhausted;
        public IncrementalFramework.Progression.Timing.TimelineAdvanceResult AdvanceTimed(double target,int budget=256)
        {
            if (!FeatureJson.Finite(target) || target < Time || budget < 1) throw new ArgumentOutOfRangeException(nameof(target));
            double requested=target; int work=0;
            target=Math.Min(target,Module.AdvanceLimit??target);
            IncrementalFramework.Progression.Timing.TimelineAdvanceResult Result(IncrementalFramework.Progression.Timing.TimelineAdvanceStatus status) =>
                new IncrementalFramework.Progression.Timing.TimelineAdvanceResult(Time,status,work);
            while(Module.NextTime.HasValue && Module.NextTime.Value<=target)
            {
                if(work>=budget)return Result(IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.BudgetExhausted);
                double next=Module.NextTime.Value;
                if(next<Time)throw new InvalidOperationException("Feature event precedes simulation time.");
                var boundary=Simulation.AdvanceToBoundary(next,budget-work);work+=boundary.ProcessedWork;
                if(!boundary.Completed || work>=budget)return Result(IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.BudgetExhausted);
                var result=Simulation.Execute(new FeatureCommand("__event"));
                if(!result.Success)throw new InvalidOperationException(result.Error);
                work++;target=Math.Min(target,Module.AdvanceLimit??target);
            }
            if(work>=budget)return Result(IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.BudgetExhausted);
            var final=Simulation.AdvanceToBoundary(target,budget-work);work+=final.ProcessedWork;
            if(!final.Completed)return Result(IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.BudgetExhausted);
            return Result(Time<requested ? IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.Blocked : IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.ReachedTarget);
        }
        public string Save(DateTime utc) => SnapshotService.Serialize(SnapshotService.Capture(Simulation, utc));
        public static FeatureGame Load(IFeatureModule module, string json, DateTime utc, out double offline)
        {
            var snapshot = SnapshotService.Deserialize(json);
            var builder = new ModuleBuilder().Add(module);
            var simulation = SnapshotService.Restore(builder.Compile(), snapshot, modules: builder);
            if (module.AdvanceLimit.HasValue && module.AdvanceLimit.Value < simulation.Time || module.NextTime.HasValue && module.NextTime < simulation.Time) throw new ArgumentException("Expired module event.");
            var result = new FeatureGame(module, simulation);
            offline = module.RunsOffline ? Math.Min(OfflineCap, Math.Max(0, (utc.ToUniversalTime() - snapshot.SavedAtUtc).TotalSeconds)) : 0;
            return result;
        }
        /// <summary>Restore through the complete feature timeline, rather than advancing only its economy.</summary>
        public static FeatureGame LoadTimed(IFeatureModule module,string json,DateTime utc,
            IncrementalFramework.Progression.Timing.TimingOptions options,
            out IncrementalFramework.Progression.Timing.TimingController timing,
            out IncrementalFramework.Progression.Timing.OfflineClaimSummary offline,bool adoptLegacy=false)
        {
            FeatureGame game=null;var builder=new ModuleBuilder().Add(module);
            var resumed=TimingSnapshotService.Resume(builder.Compile(),SnapshotService.Deserialize(json),new TimingUtcClock(utc),options,builder,
                sim=>new FeatureTimeline(game=new FeatureGame(module,sim)),adoptLegacy);
            timing=resumed.Timing;offline=resumed.Offline;return game;
        }
        private FeatureGame(IFeatureModule module, Simulation simulation) { Module = module; Simulation = simulation; }
    }

    public sealed class Inventory
    {
        public Dictionary<string, BigDouble> Items { get; set; } = new Dictionary<string, BigDouble>(StringComparer.Ordinal);
        public BigDouble Capacity { get; set; } = new BigDouble(1000000);
        public BigDouble Reserved { get; set; }
        [JsonIgnore] public BigDouble Used => Items.Values.Aggregate(BigDouble.Zero, (a, b) => a + b);
        public BigDouble Get(string id) => Items.TryGetValue(id, out var value) ? value : BigDouble.Zero;
        public bool CanTake(IDictionary<string, BigDouble> costs) => costs.All(p => p.Value >= 0 && Get(p.Key) >= p.Value);
        public bool Take(IDictionary<string, BigDouble> costs)
        {
            if (!CanTake(costs)) return false;
            foreach (var pair in costs) Items[pair.Key] = Get(pair.Key) - pair.Value;
            return true;
        }
        public bool Put(string id, BigDouble amount)
        {
            if (amount < 0 || Used + Reserved + amount > Capacity) return false;
            Items[id] = Get(id) + amount; return true;
        }
        public void Validate()
        {
            FeatureJson.Require(Items != null && Items.All(p => !string.IsNullOrEmpty(p.Key) && !BigDouble.IsNaN(p.Value) && !BigDouble.IsInfinity(p.Value) && p.Value >= 0)
                && !BigDouble.IsNaN(Capacity) && !BigDouble.IsInfinity(Capacity) && !BigDouble.IsNaN(Reserved) && !BigDouble.IsInfinity(Reserved)
                && Capacity >= 0 && Reserved >= 0 && Used + Reserved <= Capacity, "Invalid inventory.");
        }
    }
}
