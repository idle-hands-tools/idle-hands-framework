using IncrementalFramework.Unity.Strategy;
using IncrementalFramework.Progression;
using IncrementalFramework.Unity;
using IncrementalFramework.Games.Strategy;

namespace IncrementalFramework.Samples.IdleStrategy
{
    public sealed class StrategyBootstrap:EconomyBootstrap
    {
        private StrategyModule territories;
        private StrategyPresentationAdapter adapter;
        private static TerritoryDefinition[] Definitions()=>new[]
        {
            new TerritoryDefinition("territory:riverlands","Riverlands","riverlands"),
            new TerritoryDefinition("territory:highlands","Highlands","highlands"),
            new TerritoryDefinition("territory:citadel","Citadel","citadel")
        };
        protected override ModuleBuilder CreateModules(){var builder=new ModuleBuilder();foreach(var definition in Definitions())builder.Entity(definition);return builder;}
        protected override void OnSimulationChanged(){adapter?.Detach();territories?.Dispose();territories=new StrategyModule(Simulation,Definitions());adapter=new StrategyPresentationAdapter(territories);adapter.Attach(Simulation.GetReadModel(),Simulation.Execute);}
        protected override void OnDestroy(){adapter?.Detach();territories?.Dispose();base.OnDestroy();}
    }
}
