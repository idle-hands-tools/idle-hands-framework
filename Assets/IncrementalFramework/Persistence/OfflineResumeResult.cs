using System;
using System.Collections.Generic;
using BreakInfinity;

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

    public sealed class OfflineResourceGain
    {
        public string ResourceId { get; }
        public BigDouble Before { get; }
        public BigDouble After { get; }
        public BigDouble NetGain => After - Before;
        internal OfflineResourceGain(string resourceId, BigDouble before, BigDouble after)
        { ResourceId = resourceId; Before = before; After = after; }
    }

    /// <summary>Net balance changes include spending, caps and resets; they are not gross production.</summary>
    public sealed class OfflineResumeResult
    {
        private readonly BigDouble[] before;
        public Simulation Simulation { get; }
        public double OfflineSeconds { get; }
        public double TargetTime { get; }
        public double RemainingSeconds => Math.Max(0, TargetTime - Simulation.Time);
        public AdvanceResult Advance { get; private set; }
        public IReadOnlyList<OfflineResourceGain> Gains { get; private set; }
        public bool Completed => Advance.Completed;

        internal OfflineResumeResult(Simulation simulation, double offlineSeconds, double targetTime)
        {
            Simulation = simulation; OfflineSeconds = offlineSeconds; TargetTime = targetTime;
            before = new BigDouble[simulation.Resources.Count];
            for (int i = 0; i < before.Length; i++) before[i] = simulation.Resources[i].Balance;
            Continue();
        }

        public AdvanceResult Continue()
        {
            Advance = Simulation.AdvanceTo(TargetTime);
            var gains = new OfflineResourceGain[before.Length];
            for (int i = 0; i < gains.Length; i++)
                gains[i] = new OfflineResourceGain(Simulation.Resources[i].Id, before[i], Simulation.Resources[i].Balance);
            Gains = Array.AsReadOnly(gains);
            return Advance;
        }
    }
}
