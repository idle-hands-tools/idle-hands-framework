using System;

namespace IncrementalFramework
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public static SystemClock Instance { get; } = new SystemClock();
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
