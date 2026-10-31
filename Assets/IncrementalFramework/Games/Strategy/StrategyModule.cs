using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;

namespace IncrementalFramework.Games.Strategy
{
    public sealed class TerritoryDefinition:EntityTemplate
    {
        public string Name{get=>Get<string>(nameof(Name));set=>Set(nameof(Name),value);}
        public string RuleId{get=>Get<string>(nameof(RuleId));set=>Set(nameof(RuleId),value);}
        public TerritoryDefinition(string id,string name,string ruleId):base(id){Name=name;RuleId=ruleId;Define("Claimed",false);}
        public RuleDefinition Conquest(RequirementDefinition[] requirements,params RuleAction[]effects)=>new RuleDefinition(RuleId,requirements,effects,trigger:new RuleTriggerDefinition(RuleTriggerKind.Manual));
    }
    /// <summary>Territories compose existing declarative conquest rules with observable entity state.</summary>
    public sealed class StrategyModule:IDisposable
    {
        private readonly Simulation simulation;
        public IReadOnlyList<EntityInstance> Territories{get;}
        public StrategyModule(Simulation simulation,IEnumerable<TerritoryDefinition> definitions)
        {
            this.simulation=simulation;
            var instances=new List<EntityInstance>();
            foreach(var definition in definitions)
            {
                simulation.Entities.RegisterTemplate(definition);
                var entity=simulation.Entities.Entities.TryGetValue(definition.Id,out var existing)?existing:definition.Instantiate(simulation.Entities,definition.Id);
                entity.GetValue<bool>("Claimed").Calculate(_=>simulation.HasRuleFired(entity.GetValue<string>("RuleId").Current),"RuleId");instances.Add(entity);
            }
            Territories=instances.AsReadOnly();simulation.Changed+=Refresh;
        }
        private void Refresh(SimulationChangeSet changes){using(simulation.Entities.Batch())foreach(var territory in Territories)territory.GetValue<bool>("Claimed").Recalculate();}
        public void Dispose(){simulation.Changed-=Refresh;}
    }
}
