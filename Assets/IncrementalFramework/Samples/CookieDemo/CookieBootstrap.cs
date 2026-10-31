using IncrementalFramework.Unity.Clicker;
using IncrementalFramework.Core;
using IncrementalFramework.Progression;
using IncrementalFramework.Unity;
using IncrementalFramework.Games.Clicker;

namespace IncrementalFramework.Samples.CookieDemo
{
    public sealed class CookieBootstrap:EconomyBootstrap
    {
        private ClickerModule clicker;
        private ClickerPresentationAdapter adapter;
        private ClickDefinition Definition=>new ClickDefinition(Compiled.ClickResourceId,Compiled.ClickAmount);
        protected override ModuleBuilder CreateModules(){var builder=new ModuleBuilder();builder.Entity(Definition);return builder;}
        protected override void OnSimulationChanged(){adapter?.Detach();clicker=new ClickerModule(Simulation,Definition);adapter=new ClickerPresentationAdapter(clicker,ShowResult);adapter.Attach(Simulation.GetReadModel(),Simulation.Execute);}
        public override void Bake(){adapter.Click();}
        protected override void OnDestroy(){adapter?.Detach();base.OnDestroy();}
    }
}
