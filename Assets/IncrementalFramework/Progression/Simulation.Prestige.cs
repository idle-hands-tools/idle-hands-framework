using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using System;
using BreakInfinity;

namespace IncrementalFramework.Progression
{
    public sealed partial class Simulation
    {
        private ResetPolicy ProducerResetPolicy(int index, PrestigeDefinition definition) =>
            Content.Producers[index].PrestigePolicy ?? (definition.ResetProducers ? ResetPolicy.Reset : ResetPolicy.Preserve);
        private long ResetProducerCount(int index, PrestigeDefinition definition)
        {
            switch (ProducerResetPolicy(index, definition))
            {
                case ResetPolicy.Preserve: return owned[index];
                case ResetPolicy.Transform:
                    BigDouble transformed = BigDouble.Floor(Number.Multiply(new BigDouble(owned[index]), Content.Producers[index].PrestigeTransform));
                    if (transformed > new BigDouble(long.MaxValue)) throw new ArithmeticException("Transformed producer ownership overflows.");
                    return transformed >= new BigDouble(long.MaxValue) ? long.MaxValue : checked((long)transformed.ToDouble());
                default: return 0;
            }
        }
        public BigDouble GetPrestigeReward()
        {
            var definition = Content.Prestige;
            if (definition == null || !definition.UsesLifetimeReward) return BigDouble.Zero;
            BigDouble earned = lifetime[Content.ResourceIndex(definition.ResourceId)];
            if (earned < definition.Threshold) return BigDouble.Zero;
            BigDouble ratio = earned / definition.Threshold;
            double exponent = BigDouble.Log10(ratio) * definition.RewardExponent + BigDouble.Log10(definition.RewardMultiplier);
            if (double.IsNaN(exponent) || double.IsInfinity(exponent) || exponent >= long.MaxValue)
                throw new ArithmeticException("Prestige entitlement exceeds the BigDouble exponent range.");
            BigDouble entitlement = BigDouble.Floor(Number.Multiply(BigDouble.Pow(ratio, definition.RewardExponent), definition.RewardMultiplier));
            if (!Number.IsFinite(entitlement) || entitlement < BigDouble.Zero) throw new ArithmeticException("Invalid prestige entitlement.");
            return entitlement > PrestigeCurrencyEarned ? Number.AddSigned(entitlement, -PrestigeCurrencyEarned) : BigDouble.Zero;
        }
    }
}
