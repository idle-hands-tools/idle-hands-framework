using System;
using IncrementalFramework.Core;

namespace IncrementalFramework.Persistence
{
    /// <summary>A fixed UTC checkpoint for deterministic restore calls.</summary>
    public sealed class TimingUtcClock : IClock
    {
        public DateTime UtcNow { get; }
        public TimingUtcClock(DateTime utc)
        { if(utc.Kind!=DateTimeKind.Utc)throw new ArgumentException("Clock value must be UTC."); UtcNow=utc; }
    }
}
