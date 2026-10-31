using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using System;

namespace IncrementalFramework.Progression
{
    public sealed partial class Simulation
    {
        private IRandomSource injectedRandom;
        public Simulation(ContentDatabase content, IRandomSource random, int eventBudget = 10000)
            : this(content, ValidateRandom(random), eventBudget)
        { injectedRandom = random; }

        private static ulong ValidateRandom(IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (random.Algorithm != XorShift64.AlgorithmId || random.State == 0)
                throw new ArgumentException("This snapshot schema supports the documented xorshift64-13-7-17 algorithm only.", nameof(random));
            return random.State;
        }
        private ulong NextInjectedRandom()
        {
            if (injectedRandom.State != randomState)
                throw new InvalidOperationException("Injected RNG was advanced outside its owning simulation.");
            ulong next = injectedRandom.NextUInt64();
            if (next == 0 || injectedRandom.State != next)
                throw new InvalidOperationException("Injected RNG violated its algorithm/state contract.");
            randomState = next;
            return next;
        }
    }
}
