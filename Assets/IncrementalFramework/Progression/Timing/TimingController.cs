using System;
using System.Linq;
using System.Threading;
using BreakInfinity;
using IncrementalFramework.Core;

namespace IncrementalFramework.Progression.Timing
{
    /// <summary>Single writer for wall-time entitlement and paid advancement. The timeline must not be
    /// advanced independently while this controller owns it. Banking and catch-up are mutually exclusive claims.</summary>
    public sealed class TimingController
    {
        private readonly int writerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly TimingOptions options;
        private TimingState state;
        private double boost = 1;
        private bool operating;
        public ITimeline Timeline { get; }
        public TimingOptions Options => options.Copy();
        public BigDouble BankSeconds => state.BankSeconds;
        public BigDouble ReservedBankSeconds => Reserved(state);
        public BigDouble AvailableBankSeconds => BigDouble.Max(BigDouble.Zero, BankSeconds - ReservedBankSeconds);
        public double BoostMultiplier => boost;
        public bool HasPendingWork => state.Work.Count != 0;
        public bool HasPendingSkip => state.Work.Any(w => w.Kind == TimingWorkKind.Skip);
        public bool CanAcceptGameplayInput => !HasPendingWork;
        public double PendingSeconds => HasPendingWork ? Math.Max(0, state.Work[state.Work.Count - 1].TargetTime - Timeline.Time) : 0;
        public TimingView View => new TimingView(BankSeconds, ReservedBankSeconds, boost, PendingSeconds, HasPendingWork, Timeline.IsBlocked);
        public event Action<TimingView> Changed;
        public string LastObserverError { get; private set; }

        public TimingController(ITimeline timeline, TimingOptions options = null)
        {
            Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            this.options = (options ?? new TimingOptions()).Copy(); this.options.Validate();
            TimingNumbers.NonNegative(timeline.Time, nameof(timeline.Time));
            if(timeline.Simulation==null || timeline.Time!=timeline.Simulation.Time)throw new ArgumentException("Timeline must share its simulation clock.");
            state = new TimingState { SimulationTime = timeline.Time, TickRateHz = this.options.TickRateHz };
            // Already-earned native work is never converted into another offline reward.
            if (timeline.Simulation.PendingTarget.HasValue)
                { Append(state, timeline.Simulation.PendingTarget.Value, TimingWorkKind.Normal); state.Work[0].Started=true; }
        }
        public CommandResult SetBoostMultiplier(double multiplier)
        {
            Begin();
            try
            {
                if (!TimingNumbers.Finite(multiplier) || multiplier != 1 && !options.BoostMultipliers.Contains(multiplier))
                    return CommandResult.Fail("Choose one of the configured boost multipliers, or 1 to stop.");
                if (multiplier > 1 && Timeline.IsBlocked) return CommandResult.Fail("Gameplay is waiting for input.");
                boost = multiplier; return CommandResult.Ok();
            }
            finally { End(); }
        }
        public void StopBoost()
        {
            var result = SetBoostMultiplier(1);
            if (!result.Success) throw new InvalidOperationException(result.Error);
        }

        /// <summary>Claim a new absence without doing simulation work. Persist the resulting snapshot before
        /// allowing spending. Repeating the same claim on this state grants zero additional time.</summary>
        public OfflineClaimSummary ClaimOffline(DateTime savedAtUtc, DateTime nowUtc)
        {
            Begin();
            try
            {
                TimingNumbers.Utc(savedAtUtc); TimingNumbers.Utc(nowUtc);
                DateTime start = state.LastOfflineClaimUtc.HasValue && state.LastOfflineClaimUtc.Value > savedAtUtc ? state.LastOfflineClaimUtc.Value : savedAtUtc;
                double elapsed = Math.Max(0, (nowUtc - start).TotalSeconds);
                bool enabled = Timeline.AllowsOfflineRewards;
                double eligible = enabled ? Math.Min(options.MaximumOfflineSeconds, elapsed) : 0;
                double queued = 0; BigDouble credited = 0, discarded = 0;
                double end = EndTime(state);
                if (options.OfflineMode == OfflineProgressMode.AutomaticCatchUp && eligible > 0)
                { end = AddTime(end, eligible); queued = eligible; }
                else if (options.OfflineMode == OfflineProgressMode.BankTime)
                {
                    BigDouble potential = new BigDouble(eligible) * options.BankEarningRate;
                    credited = BigDouble.Min(potential, BigDouble.Max(BigDouble.Zero, new BigDouble(options.BankCapacitySeconds) - state.BankSeconds));
                    discarded = potential - credited;
                }
                // All calculations and overflow checks above precede mutation.
                if (queued > 0) Append(state, end, TimingWorkKind.Offline);
                state.BankSeconds += credited;
                state.LastOfflineClaimUtc = nowUtc > start ? nowUtc : start;
                boost = 1;
                return new OfflineClaimSummary(elapsed, eligible, queued, credited, discarded, enabled);
            }
            finally { End(); }
        }

        /// <summary>Accumulate foreground wall time independently of frame rate. Multiple missed controller
        /// ticks are coalesced; intrinsic game events still execute at their exact authored boundaries.</summary>
        public TimelineAdvanceResult AdvanceOnline(double elapsedRealSeconds, int? workBudget = null)
        {
            Begin();
            try
            {
                TimingNumbers.NonNegative(elapsedRealSeconds, nameof(elapsedRealSeconds));
                int budget = Budget(workBudget);
                if (Timeline.IsBlocked) return Block(0);
                double total = state.CadenceRemainderSeconds + elapsedRealSeconds;
                TimingNumbers.NonNegative(total, nameof(elapsedRealSeconds));
                Quantize(total, options.TickRateHz, out double due, out double remainder);
                if (due > 0)
                {
                    double end = AddTime(EndTime(state), due);
                    // While draining older work, foreground time is owed but no additional boost is reserved.
                    double extra = !HasPendingWork && boost > 1 ? due * (boost - 1) : 0;
                    extra = Math.Min(extra, Math.Min(double.MaxValue, AvailableBankSeconds.ToDouble()));
                    TimingNumbers.NonNegative(extra, "boost duration");
                    double boostedEnd = extra > 0 ? AddTime(end, extra) : end;
                    Append(state, end, TimingWorkKind.Normal);
                    if (extra > 0) Append(state, boostedEnd, TimingWorkKind.Boost);
                }
                state.CadenceRemainderSeconds = remainder;
                var result = Drain(budget);
                if (AvailableBankSeconds <= BigDouble.Zero) boost = 1;
                return result;
            }
            finally { End(); }
        }

        /// <summary>Accept an affordable skip and start it immediately. Budget exhaustion retains the request
        /// and its reservation; call Continue to finish it. No second skip can overlap pending work.</summary>
        public TimelineAdvanceResult RequestSkip(double seconds, int? workBudget = null)
        {
            Begin();
            try
            {
                TimingNumbers.Positive(seconds, nameof(seconds)); int budget = Budget(workBudget);
                if (HasPendingWork) throw new InvalidOperationException("Finish pending advancement before requesting a skip.");
                if (Timeline.IsBlocked) return Block(0);
                double affordable = Math.Min(seconds, AvailableBankSeconds.ToDouble());
                if (affordable <= 0) throw new InvalidOperationException("No banked time is available.");
                Append(state, AddTime(Timeline.Time, affordable), TimingWorkKind.Skip);
                return Drain(budget);
            }
            finally { End(); }
        }
        public TimelineAdvanceResult Continue(int? workBudget = null)
        {
            Begin();
            try { return Timeline.IsBlocked ? Block(0) : Drain(Budget(workBudget)); }
            finally { End(); }
        }

        public TimingState CaptureState()
        {
            CheckOwner(); var copy = state.Copy(); ValidateState(copy); return copy;
        }
        public void RestoreState(TimingState incoming)
        {
            Begin();
            try
            {
                ValidateState(incoming);
                if (incoming.SimulationTime != Timeline.Time || Timeline.Simulation.PendingTarget.HasValue &&
                    (incoming.Work.Count==0 || incoming.Work[0].TargetTime<Timeline.Simulation.PendingTarget.Value))
                    throw new ArgumentException("Timing state and simulation clock differ.");
                var copy = incoming.Copy();
                // Cadence changes on a later load preserve accrued foreground time, without restoring a boost.
                Quantize(copy.CadenceRemainderSeconds, options.TickRateHz, out double due, out double remainder);
                if (due > 0) Append(copy, AddTime(EndTime(copy), due), TimingWorkKind.Normal);
                copy.CadenceRemainderSeconds = remainder; copy.TickRateHz = options.TickRateHz;
                state = copy; boost = 1;
            }
            finally { End(); }
        }
        public static void ValidateState(TimingState value)
        {
            if (value == null || value.Work == null || value.Work.Count > 100000) throw new ArgumentException("Invalid timing state.");
            TimingNumbers.NonNegative(value.SimulationTime, "simulation time");
            TimingNumbers.Positive(value.TickRateHz, "saved tick rate");
            TimingNumbers.Positive(1 / value.TickRateHz, "saved tick rate");
            TimingNumbers.NonNegative(value.CadenceRemainderSeconds, "cadence remainder");
            if (value.CadenceRemainderSeconds >= 1 / value.TickRateHz) throw new ArgumentException("Invalid saved cadence remainder.");
            if (value.LastOfflineClaimUtc.HasValue) TimingNumbers.Utc(value.LastOfflineClaimUtc.Value);
            if (BigDouble.IsNaN(value.BankSeconds) || BigDouble.IsInfinity(value.BankSeconds) || value.BankSeconds < 0)
                throw new ArgumentException("Invalid bank balance.");
            if(value.Work.Skip(1).Any(w=>w!=null && w.Started))throw new ArgumentException("Only the first work segment may have started.");
            double previous = value.SimulationTime;
            foreach (var work in value.Work)
            {
                if (work == null || !TimingNumbers.Finite(work.TargetTime) || work.TargetTime < previous || !Enum.IsDefined(typeof(TimingWorkKind), work.Kind))
                    throw new ArgumentException("Invalid pending timing work.");
                previous = work.TargetTime;
            }
            BigDouble reserved = Reserved(value);
            if (reserved > value.BankSeconds && reserved - value.BankSeconds > BigDouble.Max(BigDouble.One, reserved) * 1e-9)
                throw new ArgumentException("Timing reservations exceed the bank balance.");
        }
        private TimelineAdvanceResult Drain(int budget)
        {
            int work = 0;
            while (state.Work.Count > 0)
            {
                if (Timeline.IsBlocked) return Block(work);
                if (work >= budget) return new TimelineAdvanceResult(Timeline.Time, TimelineAdvanceStatus.BudgetExhausted, work);
                var item = state.Work[0]; double before = Timeline.Time;
                TimelineAdvanceResult result;
                try { item.Started=true; result = Timeline.AdvanceTo(item.TargetTime, budget - work); }
                finally
                {
                    double progressed = Timeline.Time - before;
                    if (!TimingNumbers.Finite(progressed) || progressed < 0 || Timeline.Time > item.TargetTime)
                        throw new InvalidOperationException("Timeline violated its advancement contract.");
                    if (IsBanked(item.Kind)) state.BankSeconds = BigDouble.Max(BigDouble.Zero, state.BankSeconds - progressed);
                    state.SimulationTime = Timeline.Time;
                }
                if (result.Time != Timeline.Time || result.ProcessedWork < 0 || result.ProcessedWork>budget-work) throw new InvalidOperationException("Invalid timeline result.");
                // Count dispatches too, bounding empty/analytical segments without adding simulation ticks.
                work += Math.Max(1, result.ProcessedWork);
                if (result.Status == TimelineAdvanceStatus.Blocked) return Block(work);
                if (result.Status == TimelineAdvanceStatus.BudgetExhausted)
                    return new TimelineAdvanceResult(Timeline.Time, TimelineAdvanceStatus.BudgetExhausted, work);
                if (Timeline.Time != item.TargetTime) throw new InvalidOperationException("Timeline reported an incomplete target as reached.");
                state.Work.RemoveAt(0);
            }
            return new TimelineAdvanceResult(Timeline.Time, TimelineAdvanceStatus.ReachedTarget, work);
        }
        private TimelineAdvanceResult Block(int work)
        {
            // Only elapsed banked seconds were charged. Removing pending work releases all unused reservations.
            state.Work.Clear(); state.CadenceRemainderSeconds = 0; boost = 1;
            return new TimelineAdvanceResult(Timeline.Time, TimelineAdvanceStatus.Blocked, work);
        }
        private static void Append(TimingState value, double target, TimingWorkKind kind)
        {
            if (value.Work.Count > 0 && value.Work[value.Work.Count - 1].Kind == kind && !value.Work[value.Work.Count - 1].Started)
                value.Work[value.Work.Count - 1].TargetTime = target;
            else value.Work.Add(new TimingWork { TargetTime = target, Kind = kind });
        }
        private static bool IsBanked(TimingWorkKind kind) => kind == TimingWorkKind.Boost || kind == TimingWorkKind.Skip;
        private static BigDouble Reserved(TimingState value)
        {
            BigDouble sum = 0; double start = value.SimulationTime;
            foreach (var item in value.Work)
            { if (IsBanked(item.Kind)) sum += item.TargetTime - start; start = item.TargetTime; }
            return sum;
        }
        private static double EndTime(TimingState value) => value.Work.Count == 0 ? value.SimulationTime : value.Work[value.Work.Count - 1].TargetTime;
        private static double AddTime(double start, double duration)
        {
            double target = start + duration;
            if (!TimingNumbers.Finite(target) || target <= start) throw new ArgumentOutOfRangeException(nameof(duration), "Duration exceeds clock precision or range.");
            return target;
        }
        private static void Quantize(double total, double hz, out double due, out double remainder)
        {
            double interval = 1 / hz, ticks = total / interval;
            if (TimingNumbers.Finite(ticks))
            {
                due = Math.Floor(ticks + 1e-9) * interval;
                remainder = Math.Max(0, total - due);
            }
            else { remainder = total % interval; due = total - remainder; }
            if (!TimingNumbers.Finite(due) || !TimingNumbers.Finite(remainder)) throw new ArgumentOutOfRangeException(nameof(total));
        }
        private int Budget(int? value)
        { int result = value ?? options.WorkBudget; if (result < 1) throw new ArgumentOutOfRangeException(nameof(value)); return result; }
        private void CheckOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != writerThread) throw new InvalidOperationException("Timing has one logical writer.");
            if (Timeline.Time != state.SimulationTime) throw new InvalidOperationException("Advance the timeline through its timing controller.");
        }
        private void Begin()
        { CheckOwner(); if (operating) throw new InvalidOperationException("Do not mutate timing from its notifications."); operating = true; }
        private void End()
        {
            try
            {
                if(AvailableBankSeconds<=BigDouble.Zero) boost=1;
                if (Changed != null)
                    foreach (Action<TimingView> listener in Changed.GetInvocationList())
                        try { listener(View); } catch (Exception e) { LastObserverError = e.Message; }
            }
            finally { operating = false; }
        }
    }
}
