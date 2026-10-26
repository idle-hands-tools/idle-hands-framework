using System;

namespace IncrementalFramework
{
    public interface ISimulationCommand { }

    public readonly struct CommandResult
    {
        public bool Success { get; }
        public string Error { get; }
        private CommandResult(bool success, string error) { Success = success; Error = error; }
        public static CommandResult Ok() => new CommandResult(true, null);
        public static CommandResult Fail(string error) => new CommandResult(false, error);
    }

    public enum AdvanceDiagnostic { None, EventBudgetExhausted, SameTimeCascade }

    public readonly struct AdvanceResult
    {
        public bool Completed { get; }
        public double Time { get; }
        public int ProcessedEvents { get; }
        public int ProcessedWork { get; }
        public AdvanceDiagnostic Diagnostic { get; }
        public AdvanceResult(bool completed, double time, int processedEvents, int processedWork = -1,
            AdvanceDiagnostic diagnostic = AdvanceDiagnostic.None)
        { Completed = completed; Time = time; ProcessedEvents = processedEvents; ProcessedWork = processedWork < 0 ? processedEvents : processedWork;
            Diagnostic = !completed && diagnostic == AdvanceDiagnostic.None ? AdvanceDiagnostic.EventBudgetExhausted : diagnostic; }
    }

    public readonly struct EventToken : IEquatable<EventToken>
    {
        public long Value { get; }
        public long Version { get; }
        public EventToken(long value, long version = 1) { Value = value; Version = version; }
        public bool Equals(EventToken other) => Value == other.Value && Version == other.Version;
        public override bool Equals(object obj) => obj is EventToken other && Equals(other);
        public override int GetHashCode() => unchecked(Value.GetHashCode() * 397 ^ Version.GetHashCode());
    }

    public sealed class ScheduledEvent<T>
    {
        public double Time { get; }
        public int Priority { get; }
        public long Sequence { get; }
        public long Version { get; }
        public EventToken Token => new EventToken(Sequence, Version);
        public T Payload { get; }
        public ScheduledEvent(double time, int priority, long sequence, T payload, long version = 1)
        { Time = time; Priority = priority; Sequence = sequence; Payload = payload; Version = version; }
    }
}
