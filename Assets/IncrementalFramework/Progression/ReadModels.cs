using System;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;

namespace IncrementalFramework.Progression
{
    public sealed class ResourceView : EntityInstance
    {
        public BigDouble Balance { get => Get<BigDouble>(nameof(Balance)); set => Set(nameof(Balance),value); }
        public BigDouble Rate { get => Get<BigDouble>(nameof(Rate)); set => Set(nameof(Rate),value); }
        public BigDouble LifetimeEarned { get => Get<BigDouble>(nameof(LifetimeEarned)); set => Set(nameof(LifetimeEarned),value); }
        public BigDouble? Cap { get => Get<BigDouble?>(nameof(Cap)); set => Set(nameof(Cap),value); }
        internal ResourceView(string id,BigDouble? cap,Func<BigDouble> balance,Func<BigDouble> rate,Func<BigDouble> lifetime,
            Action<BigDouble> setBalance,Action<BigDouble> setRate,Action<BigDouble> setLifetime,EntityContext context) : base("resource:"+id,id,context)
        {
            Define(nameof(Balance),balance()).Bind(balance,setBalance);
            Define(nameof(Rate),rate()).Bind(rate,setRate,true);
            Define(nameof(LifetimeEarned),lifetime()).Bind(lifetime,setLifetime);
            Cap=cap;Kind="resource";context.Add(this);
        }
    }
    public sealed class ProducerView : EntityInstance
    {
        public long Owned {get=>Get<long>(nameof(Owned));set=>Set(nameof(Owned),value);}
        public bool Enabled {get=>Get<bool>(nameof(Enabled));set=>Set(nameof(Enabled),value);}
        public bool Unlocked {get=>Get<bool>(nameof(Unlocked));set=>Set(nameof(Unlocked),value);}
        internal ProducerView(string id,Func<long> owned,Func<bool> enabled,Func<bool> unlocked,Action<long> setOwned,Action<bool> setEnabled,Action<bool> setUnlocked,EntityContext context):base("producer:"+id,id,context)
        {Define(nameof(Owned),owned()).Bind(owned,setOwned);Define(nameof(Enabled),enabled()).Bind(enabled,setEnabled);Define(nameof(Unlocked),unlocked()).Bind(unlocked,setUnlocked);Kind="producer";context.Add(this);}
    }
    public sealed class UpgradeView : EntityInstance
    {
        public bool Purchased {get=>Get<bool>(nameof(Purchased));set=>Set(nameof(Purchased),value);}
        public bool Unlocked {get=>Get<bool>(nameof(Unlocked));set=>Set(nameof(Unlocked),value);}
        internal UpgradeView(string id,Func<bool> purchased,Func<bool> unlocked,Action<bool> setPurchased,Action<bool> setUnlocked,EntityContext context):base("upgrade:"+id,id,context)
        {Define(nameof(Purchased),purchased()).Bind(purchased,setPurchased);Define(nameof(Unlocked),unlocked()).Bind(unlocked,setUnlocked);Kind="upgrade";context.Add(this);}
    }
}
