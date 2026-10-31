using System;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.Progression
{
    /// <summary>Shares monotonic time and an execution budget between analytical economy and declared behaviors.</summary>
    public sealed class DeclarativeRuntime
    {
        public Simulation Simulation{get;}
        public BehaviorRunner Behaviors{get;}
        public EntityContext Entities=>Simulation.Entities;
        public DeclarativeRuntime(Simulation simulation,CompiledGameDefinition definition)
        {
            Simulation=simulation;
            foreach(var template in definition.Templates.Values)Entities.RegisterTemplate(template);
            Behaviors=new BehaviorRunner(definition,Entities){SourceClock=()=>Simulation.Time,AdvanceBoundary=Simulation.AdvanceToBoundary};
            Behaviors.Restore(simulation.Time,1,Array.Empty<BehaviorJob>());
        }
        public bool AdvanceTo(double target,int workBudget=1024)=>Behaviors.AdvanceTo(target,workBudget);
    }
}
