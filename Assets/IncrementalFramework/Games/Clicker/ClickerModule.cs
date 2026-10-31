using System;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;

namespace IncrementalFramework.Games.Clicker
{
    public sealed class ClickDefinition:EntityTemplate
    {
        public string ResourceId{get=>Get<string>(nameof(ResourceId));set=>Set(nameof(ResourceId),value);}
        public BigDouble BaseAmount{get=>Get<BigDouble>(nameof(BaseAmount));set=>Set(nameof(BaseAmount),value);}
        public ClickDefinition(string resourceId,BigDouble amount):base("click:"+resourceId)
        {ResourceId=resourceId;BaseAmount=amount;Define("Amount",amount);}
    }
    public sealed class ClickerModule
    {
        public EntityInstance Clicker{get;}
        public ActionDefinition Action{get;}
        public ClickerModule(Simulation simulation,ClickDefinition definition)
        {
            simulation.Entities.RegisterTemplate(definition);
            Clicker=simulation.Entities.Entities.TryGetValue(definition.Id,out var existing)?existing:definition.Instantiate(simulation.Entities,definition.Id);
            Clicker.GetValue<BigDouble>("Amount").Calculate(entity=>
            {
                foreach(var upgrade in simulation.Upgrades)_=upgrade.Purchased;
                return simulation.ClickAmount(definition.ResourceId,entity.GetValue<BigDouble>("BaseAmount").Current);
            },"BaseAmount");
            Action=new ActionDefinition("click",null,Effects.Add(new PropertyKey<BigDouble>("Balance"),Expr.Property(new PropertyKey<BigDouble>("Amount")),TargetQuery.ById(Expr.Constant("resource:"+definition.ResourceId))));
        }
        public CommandResult Click()=>Action.Execute(new EvaluationContext(Clicker),out var error)?CommandResult.Ok():CommandResult.Fail(error);
    }
}
