using System;
using System.Linq;

namespace IncrementalFramework.Progression.Timing
{
    public enum OfflineProgressMode { AutomaticCatchUp, BankTime }

    public sealed class TimingOptions
    {
        public OfflineProgressMode OfflineMode { get; set; } = OfflineProgressMode.AutomaticCatchUp;
        public double MaximumOfflineSeconds { get; set; } = 28800;
        public double BankCapacitySeconds { get; set; } = 28800;
        public double BankEarningRate { get; set; } = 1;
        public double TickRateHz { get; set; } = 10;
        public int WorkBudget { get; set; } = 256;
        public double[] BoostMultipliers { get; set; } = new[] { 2d, 4d, 8d };

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(OfflineProgressMode), OfflineMode)) throw new ArgumentException("Unknown offline progress mode.");
            TimingNumbers.NonNegative(MaximumOfflineSeconds, nameof(MaximumOfflineSeconds));
            TimingNumbers.NonNegative(BankCapacitySeconds, nameof(BankCapacitySeconds));
            TimingNumbers.NonNegative(BankEarningRate, nameof(BankEarningRate));
            TimingNumbers.Positive(TickRateHz, nameof(TickRateHz));
            TimingNumbers.Positive(1 / TickRateHz, nameof(TickRateHz));
            if (WorkBudget < 1) throw new ArgumentOutOfRangeException(nameof(WorkBudget));
            if (BoostMultipliers == null || BoostMultipliers.Any(x => !TimingNumbers.Finite(x) || x <= 1) || BoostMultipliers.Distinct().Count() != BoostMultipliers.Length)
                throw new ArgumentException("Boost multipliers must be distinct finite numbers above one.");
        }
        public TimingOptions Copy() => new TimingOptions
        {
            OfflineMode = OfflineMode, MaximumOfflineSeconds = MaximumOfflineSeconds,
            BankCapacitySeconds = BankCapacitySeconds, BankEarningRate = BankEarningRate,
            TickRateHz = TickRateHz, WorkBudget = WorkBudget, BoostMultipliers = BoostMultipliers?.ToArray()
        };
    }
    internal static class TimingNumbers
    {
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static void NonNegative(double value, string name)
        { if (!Finite(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
        public static void Positive(double value, string name)
        { if (!Finite(value) || value <= 0) throw new ArgumentOutOfRangeException(name); }
        public static void Utc(DateTime value)
        { if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timing checkpoints must be UTC."); }
    }
}
