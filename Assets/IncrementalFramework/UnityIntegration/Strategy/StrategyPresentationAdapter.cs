using System;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Games.Strategy;
using IncrementalFramework.Progression;

namespace IncrementalFramework.Unity.Strategy
{
    public sealed class StrategyPresentationAdapter:IPresentationAdapter
    {
        private readonly StrategyModule module;
        public int Controlled=>module.Territories.Count(x=>x.GetValue<bool>("Claimed").Current);
        public event Action<int> Changed;
        public StrategyPresentationAdapter(StrategyModule module){this.module=module;}
        public void Attach(SimulationReadModel state,Func<ISimulationCommand,CommandResult> dispatch){foreach(var territory in module.Territories)territory.GetValue<bool>("Claimed").Changed+=OnChanged;}
        private void OnChanged(ValueChange<bool> change)=>Changed?.Invoke(Controlled);
        public void Detach(){foreach(var territory in module.Territories)territory.GetValue<bool>("Claimed").Changed-=OnChanged;}
    }
}
