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

    public readonly struct AdvanceResult
    {
        public bool Completed { get; }
        public double Time { get; }
        public int ProcessedEvents { get; }
        public AdvanceResult(bool completed, double time, int processedEvents)
        { Completed = completed; Time = time; ProcessedEvents = processedEvents; }
    }

    public readonly struct EventToken : IEquatable<EventToken>
    {
        public long Value { get; }
        public EventToken(long value) { Value = value; }
        public bool Equals(EventToken other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EventToken other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed class ScheduledEvent<T>
    {
        public double Time { get; }
        public int Priority { get; }
        public long Sequence { get; }
        public EventToken Token => new EventToken(Sequence);
        public T Payload { get; }
        public ScheduledEvent(double time, int priority, long sequence, T payload)
        { Time = time; Priority = priority; Sequence = sequence; Payload = payload; }
    }
}
