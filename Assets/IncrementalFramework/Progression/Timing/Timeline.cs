using System;

namespace IncrementalFramework.Progression.Timing
{
    public enum TimelineAdvanceStatus { ReachedTarget, BudgetExhausted, Blocked }

    public readonly struct TimelineAdvanceResult
    {
        public double Time { get; }
        public TimelineAdvanceStatus Status { get; }
        public int ProcessedWork { get; }
        public TimelineAdvanceResult(double time, TimelineAdvanceStatus status, int processedWork = 0)
        { Time = time; Status = status; ProcessedWork = processedWork; }
    }

    /// <summary>Advance the entire game, including its scheduler. Blocked results must leave a stable,
    /// input-ready boundary; BudgetExhausted results must be retried with the same target.</summary>
    public interface ITimeline
    {
        Simulation Simulation { get; }
        double Time { get; }
        bool AllowsOfflineRewards { get; }
        bool IsBlocked { get; }
        TimelineAdvanceResult AdvanceTo(double target, int workBudget);
    }

    /// <summary>Optional module-level gate, independent of whether offline rewards are enabled.</summary>
    public interface ITimelineGate { bool IsTimeBlocked { get; } }

    public sealed class SimulationTimeline : ITimeline
    {
        public Simulation Simulation { get; }
        public double Time => Simulation.Time;
        public bool AllowsOfflineRewards { get; }
        public bool IsBlocked => false;
        public SimulationTimeline(Simulation simulation, bool allowsOfflineRewards = true)
        { Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation)); AllowsOfflineRewards = allowsOfflineRewards; }
        public TimelineAdvanceResult AdvanceTo(double target, int workBudget)
        {
            var result = Simulation.AdvanceToBoundary(target, workBudget);
            return new TimelineAdvanceResult(result.Time, result.Completed ? TimelineAdvanceStatus.ReachedTarget : TimelineAdvanceStatus.BudgetExhausted, result.ProcessedWork);
        }
    }

    public sealed class DeclarativeTimeline : ITimeline
    {
        public DeclarativeRuntime Runtime { get; }
        public Simulation Simulation => Runtime.Simulation;
        public double Time => Simulation.Time;
        public bool AllowsOfflineRewards { get; }
        public bool IsBlocked => false;
        public DeclarativeTimeline(DeclarativeRuntime runtime, bool allowsOfflineRewards = true)
        { Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); AllowsOfflineRewards = allowsOfflineRewards; }
        public TimelineAdvanceResult AdvanceTo(double target, int workBudget)
        {
            bool done = Runtime.AdvanceTo(target, workBudget);
            // BehaviorRunner accounts for instructions and economy events in the same budget.
            return new TimelineAdvanceResult(Time, done ? TimelineAdvanceStatus.ReachedTarget : TimelineAdvanceStatus.BudgetExhausted, Runtime.Behaviors.LastProcessedWork);
        }
    }
}
