using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;

namespace IncrementalFramework.Progression.Timing
{
    public enum TimingWorkKind { Normal, Offline, Boost, Skip }
    public sealed class TimingWork
    {
        public double TargetTime { get; set; }
        public TimingWorkKind Kind { get; set; }
        public bool Started { get; set; }
        public TimingWork Copy() => new TimingWork { TargetTime = TargetTime, Kind = Kind, Started = Started };
    }

    /// <summary>Explicit snapshot DTO. Work targets are absolute simulation times, not UTC timestamps.</summary>
    public sealed class TimingState
    {
        public double SimulationTime { get; set; }
        public BigDouble BankSeconds { get; set; }
        public double CadenceRemainderSeconds { get; set; }
        public double TickRateHz { get; set; } = 10;
        public DateTime? LastOfflineClaimUtc { get; set; }
        public List<TimingWork> Work { get; set; } = new List<TimingWork>();
        public TimingState Copy() => new TimingState
        {
            SimulationTime = SimulationTime, BankSeconds = BankSeconds,
            CadenceRemainderSeconds = CadenceRemainderSeconds, TickRateHz = TickRateHz,
            LastOfflineClaimUtc = LastOfflineClaimUtc, Work = Work.Select(w => w.Copy()).ToList()
        };
    }

    public readonly struct TimingView
    {
        public BigDouble BankSeconds { get; }
        public BigDouble ReservedBankSeconds { get; }
        public BigDouble AvailableBankSeconds => BigDouble.Max(BigDouble.Zero, BankSeconds - ReservedBankSeconds);
        public double BoostMultiplier { get; }
        public double PendingSeconds { get; }
        public bool HasPendingWork { get; }
        public bool IsBlocked { get; }
        public bool CanAcceptGameplayInput => !HasPendingWork;
        public TimingView(BigDouble balance, BigDouble reserved, double boost, double pending, bool busy, bool blocked)
        { BankSeconds = balance; ReservedBankSeconds = reserved; BoostMultiplier = boost; PendingSeconds = pending; HasPendingWork = busy; IsBlocked = blocked; }
    }

    public sealed class OfflineClaimSummary
    {
        public double ElapsedSeconds { get; }
        public double EligibleSeconds { get; }
        public double DiscardedOfflineSeconds => ElapsedSeconds - EligibleSeconds;
        public double QueuedCatchUpSeconds { get; }
        public BigDouble BankedSeconds { get; }
        public BigDouble DiscardedBankSeconds { get; }
        public bool RewardsEnabled { get; }
        internal OfflineClaimSummary(double elapsed, double eligible, double queued, BigDouble banked, BigDouble discarded, bool enabled)
        { ElapsedSeconds = elapsed; EligibleSeconds = eligible; QueuedCatchUpSeconds = queued; BankedSeconds = banked; DiscardedBankSeconds = discarded; RewardsEnabled = enabled; }
    }
}
