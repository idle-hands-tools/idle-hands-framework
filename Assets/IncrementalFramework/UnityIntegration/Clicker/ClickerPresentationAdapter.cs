using System;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Games.Clicker;
using IncrementalFramework.Progression;

namespace IncrementalFramework.Unity.Clicker
{
    public sealed class ClickerPresentationAdapter:IPresentationAdapter
    {
        private readonly ClickerModule module;
        private readonly Action<CommandResult> result;
        public event Action<BigDouble> AmountChanged;
        public ClickerPresentationAdapter(ClickerModule module,Action<CommandResult> result){this.module=module;this.result=result;}
        public void Attach(SimulationReadModel state,Func<ISimulationCommand,CommandResult> dispatch){module.Clicker.GetValue<BigDouble>("Amount").Changed+=Changed;}
        private void Changed(ValueChange<BigDouble> change)=>AmountChanged?.Invoke(change.NewValue);
        public void Click()=>result(module.Click());
        public void Detach(){module.Clicker.GetValue<BigDouble>("Amount").Changed-=Changed;}
    }
}
