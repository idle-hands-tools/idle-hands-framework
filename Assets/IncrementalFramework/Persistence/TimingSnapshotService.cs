using System;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Timing;
using Newtonsoft.Json;

namespace IncrementalFramework.Persistence
{
    /// <summary>Optional versioned section. Binding is deferred until the complete game world is restored,
    /// allowing genre timelines to resolve entities without introducing dependencies into shared persistence.</summary>
    public sealed class TimingModule : IGameModule, ISnapshotSection
    {
        public const string SectionId = "framework.timing";
        public string Id => SectionId;
        public int Version => 1;
        public TimingController Controller { get; private set; }
        private Simulation owner;
        private TimingState restored;
        public void Register(ModuleBuilder builder)
        { builder.Configure(sim => owner = sim); builder.Snapshot(this); }
        internal void Attach(Simulation simulation,TimingController controller)
        { simulation.RegisterSnapshotSection(this); owner = simulation; Controller=controller; }
        public TimingController Bind(ITimeline timeline, TimingOptions options = null)
        {
            if (Controller != null || owner == null || timeline?.Simulation != owner) throw new InvalidOperationException("Bind timing once, to its registered simulation.");
            var controller = new TimingController(timeline, options);
            if (restored != null) controller.RestoreState(restored);
            Controller = controller; return controller;
        }
        public string Capture()
        {
            if (Controller == null) throw new InvalidOperationException("Bind the timing module before saving.");
            return Encode(Controller.CaptureState());
        }
        public void Validate(int version, string data) => Decode(version, data);
        public void Restore(int version, string data)
        {
            var value = Decode(version, data);
            if (Controller != null) Controller.RestoreState(value);
            else restored = value;
        }
        internal static string Encode(TimingState value) => JsonConvert.SerializeObject(value, SnapshotService.CreateSettings());
        internal static TimingState Decode(int version, string data)
        {
            if (version != 1 || string.IsNullOrWhiteSpace(data) || data.Length > SnapshotService.MaximumSnapshotBytes)
                throw new ArgumentException("Unsupported or oversized timing snapshot.");
            var value = JsonConvert.DeserializeObject<TimingState>(data, SnapshotService.CreateSettings());
            TimingController.ValidateState(value); return value;
        }
    }

    public sealed class TimingRestoreResult
    {
        public Simulation Simulation => Timing.Timeline.Simulation;
        public TimingController Timing { get; }
        public OfflineClaimSummary Offline { get; }
        public TimelineAdvanceResult Advance { get; }
        internal TimingRestoreResult(TimingController timing, OfflineClaimSummary offline, TimelineAdvanceResult advance)
        { Timing = timing; Offline = offline; Advance = advance; }
    }

    public static class TimingSnapshotService
    {
        public static TimingController Attach(ITimeline timeline, TimingOptions options = null)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            (options ?? new TimingOptions()).Validate();
            if (timeline.Simulation.SnapshotSections.Any(s => s.Id == TimingModule.SectionId))
                throw new InvalidOperationException("This simulation already has a timing section.");
            var controller=new TimingController(timeline,options);
            var module = new TimingModule(); module.Attach(timeline.Simulation,controller); return controller;
        }
        public static SimulationSnapshot Capture(TimingController timing, DateTime utcNow)
        {
            if (timing == null) throw new ArgumentNullException(nameof(timing));
            if (!timing.Timeline.Simulation.SnapshotSections.OfType<TimingModule>().Any(m => ReferenceEquals(m.Controller, timing)))
                throw new InvalidOperationException("Attach timing persistence before saving.");
            return SnapshotService.Capture(timing.Timeline.Simulation, utcNow);
        }
        /// <summary>Returns a detached snapshot. No root schema or content identity changes are needed.
        /// Only absent timing data is initialized; all other required sections remain strict.</summary>
        public static SimulationSnapshot AdoptLegacy(SimulationSnapshot snapshot, TimingOptions options = null)
        {
            var config = options ?? new TimingOptions(); config.Validate();
            var copy = SnapshotService.Deserialize(SnapshotService.Serialize(snapshot));
            if (copy.Sections.Any(s => s.Id == TimingModule.SectionId)) return copy;
            var state = new TimingState { SimulationTime = copy.State.Time, TickRateHz = config.TickRateHz };
            if (copy.State.PendingTarget.HasValue)
                state.Work.Add(new TimingWork { TargetTime = copy.State.PendingTarget.Value, Kind = TimingWorkKind.Normal, Started=true });
            TimingController.ValidateState(state);
            copy.Sections.Add(new SnapshotSectionData { Id = TimingModule.SectionId, Version = 1, Data = TimingModule.Encode(state) });
            return copy;
        }
        public static TimingRestoreResult Resume(ContentDatabase content, SimulationSnapshot snapshot, IClock clock,
            TimingOptions options, ModuleBuilder modules = null, Func<Simulation, ITimeline> timelineFactory = null, bool adoptLegacy = false)
        {
            if (options == null || clock == null) throw new ArgumentNullException(options == null ? nameof(options) : nameof(clock));
            options.Validate(); DateTime now = clock.UtcNow;
            if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("Resume time must be UTC.");
            // Validate and detach before registering modules or modifying a restored world.
            var prepared = adoptLegacy ? AdoptLegacy(snapshot, options) : SnapshotService.Deserialize(SnapshotService.Serialize(snapshot));
            var saved = prepared.Sections.SingleOrDefault(s => s.Id == TimingModule.SectionId);
            if (saved == null) throw new InvalidOperationException("No timing section; explicitly adopt this legacy save.");
            var state = TimingModule.Decode(saved.Version, saved.Data);
            if (state.SimulationTime != prepared.State.Time || prepared.State.PendingTarget.HasValue &&
                (state.Work.Count == 0 || state.Work[0].TargetTime < prepared.State.PendingTarget.Value))
                throw new ArgumentException("Timing work and the saved simulation clock differ.");
            var builder = modules ?? new ModuleBuilder(); var module = new TimingModule(); builder.Add(module);
            var simulation = SnapshotService.Restore(content, prepared, modules: builder);
            var timeline = timelineFactory == null ? new SimulationTimeline(simulation) : timelineFactory(simulation);
            var timing = module.Bind(timeline, options);
            var offline = timing.ClaimOffline(prepared.SavedAtUtc, now);
            return new TimingRestoreResult(timing, offline, timing.Continue());
        }
    }
}
