using System;
using BreakInfinity;

namespace IncrementalFramework
{
    public sealed class ResourceView
    {
        private readonly Func<BigDouble> balance;
        private readonly Func<BigDouble> rate;
        private readonly Func<BigDouble> lifetime;
        public string Id { get; }
        public BigDouble Balance => balance();
        public BigDouble Rate => rate();
        public BigDouble LifetimeEarned => lifetime();
        public BigDouble? Cap { get; }
        internal ResourceView(string id, BigDouble? cap, Func<BigDouble> balance, Func<BigDouble> rate, Func<BigDouble> lifetime)
        { Id = id; Cap = cap; this.balance = balance; this.rate = rate; this.lifetime = lifetime; }
    }
    public sealed class ProducerView
    {
        private readonly Func<long> owned;
        private readonly Func<bool> enabled;
        private readonly Func<bool> unlocked;
        public string Id { get; }
        public long Owned => owned();
        public bool Enabled => enabled();
        public bool Unlocked => unlocked();
        internal ProducerView(string id, Func<long> owned, Func<bool> enabled, Func<bool> unlocked)
        { Id = id; this.owned = owned; this.enabled = enabled; this.unlocked = unlocked; }
    }
    public sealed class UpgradeView
    {
        private readonly Func<bool> purchased;
        private readonly Func<bool> unlocked;
        public string Id { get; }
        public bool Purchased => purchased();
        public bool Unlocked => unlocked();
        internal UpgradeView(string id, Func<bool> purchased, Func<bool> unlocked)
        { Id = id; this.purchased = purchased; this.unlocked = unlocked; }
    }
}
