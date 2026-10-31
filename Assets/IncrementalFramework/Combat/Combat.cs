using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.Combat
{
    public static class CombatProperties
    {
        public static readonly PropertyKey<double> Health=new PropertyKey<double>("Health");
        public static readonly PropertyKey<double> MaxHealth=new PropertyKey<double>("MaxHealth");
        public static readonly PropertyKey<double> Attack=new PropertyKey<double>("Attack");
        public static readonly PropertyKey<double> Defense=new PropertyKey<double>("Defense");
        public static readonly PropertyKey<double> Healing=new PropertyKey<double>("Healing");
        public static readonly PropertyKey<double> Priority=new PropertyKey<double>("Priority");
        public static readonly PropertyKey<double> IncomingMultiplier=new PropertyKey<double>("IncomingMultiplier");
        public static readonly PropertyKey<double> PartyBonus=new PropertyKey<double>("PartyBonus");
        public static readonly PropertyKey<int> Team=new PropertyKey<int>("Team");
        public static readonly PropertyKey<string> Encounter=new PropertyKey<string>("Encounter");
        public static readonly PropertyKey<string> Name=new PropertyKey<string>("Name");
    }
    public class Combatant:EntityInstance
    {
        public double Health{get=>Get<double>(nameof(Health));set=>Set(nameof(Health),value);}
        public double MaxHealth{get=>Get<double>(nameof(MaxHealth));set=>Set(nameof(MaxHealth),value);}
        public double Attack{get=>Get<double>(nameof(Attack));set=>Set(nameof(Attack),value);}
        public double Defense{get=>Get<double>(nameof(Defense));set=>Set(nameof(Defense),value);}
        public double Healing{get=>Get<double>(nameof(Healing));set=>Set(nameof(Healing),value);}
        public double Priority{get=>Get<double>(nameof(Priority));set=>Set(nameof(Priority),value);}
        public double IncomingMultiplier{get=>Get<double>(nameof(IncomingMultiplier));set=>Set(nameof(IncomingMultiplier),value);}
        public double PartyBonus{get=>Get<double>(nameof(PartyBonus));set=>Set(nameof(PartyBonus),value);}
        public int Team{get=>Get<int>(nameof(Team));set=>Set(nameof(Team),value);}
        public string Encounter{get=>Get<string>(nameof(Encounter));set=>Set(nameof(Encounter),value);}
        public string Name{get=>Get<string>(nameof(Name));set=>Set(nameof(Name),value);}
        public Combatant(string id=null,string templateId=null,EntityContext context=null):base(id,templateId,context)
        {Kind="combatant";Health=0;MaxHealth=1;Attack=0;Defense=0;Healing=0;Priority=0;IncomingMultiplier=1;PartyBonus=1;Team=0;Encounter="";Name=id??"Combatant";}
        protected override EntityInstance CreateClone()=>new Combatant(Id,TemplateId);
    }
    public static class CombatTargets
    {
        public static TargetQuery Living(bool allies)
        {
            var team=Expr.Equal(Expr.Property(CombatProperties.Team,EntityScope.Target),Expr.Property(CombatProperties.Team));
            return TargetQuery.All().OfKind("combatant").Where(Expr.And(allies?team:Expr.Not(team),
                Expr.Equal(Expr.Property(CombatProperties.Encounter,EntityScope.Target),Expr.Property(CombatProperties.Encounter)),
                Expr.Less(Expr.Constant(0d),Expr.Property(CombatProperties.Health,EntityScope.Target))));
        }
        public static TargetQuery PriorityEnemy()=>Living(false).OrderBy(Expr.Property(CombatProperties.Priority,EntityScope.Target),true).Take(1);
        public static TargetQuery MostInjuredAlly()=>Living(true)
            .Where(Expr.Less(Expr.Property(CombatProperties.Health,EntityScope.Target),Expr.Property(CombatProperties.MaxHealth,EntityScope.Target)))
            .OrderBy(Expr.Divide(Expr.Property(CombatProperties.Health,EntityScope.Target),Expr.Property(CombatProperties.MaxHealth,EntityScope.Target))).Take(1);
    }
    public sealed class CombatEffect:EffectDefinition
    {
        public bool IsHealing{get;}
        public Expression<double> Amount{get;}
        public TargetQuery Targets{get;}
        public string Label{get;}
        public override IEnumerable<string> ExtensionIds=>Amount.RequiredExtensions.Concat(Targets.RequiredExtensions);
        public override IEnumerable<PropertyReference> References=>Amount.References.Concat(Targets.References);
        public CombatEffect(bool healing,Expression<double> amount,TargetQuery targets,string label=null)
        {IsHealing=healing;Amount=amount;Targets=targets;Label=label??(healing?"healed":"hit");}
        public override void Stage(EvaluationContext context,EntityTransaction transaction)
        {
            var previous=context.Target;
            try
            {
                foreach(var target in Targets.Select(context))
                {
                    context.Target=target;double amount=Amount.Evaluate(context);
                    var health=target.GetValue(CombatProperties.Health);double before=transaction.Read(health);
                    if(before<=0)continue;
                    double delta=IsHealing?Math.Min(amount,transaction.Read(target.GetValue(CombatProperties.MaxHealth))-before)
                        :Math.Max(1,amount-transaction.Read(target.GetValue(CombatProperties.Defense)))*transaction.Read(target.GetValue(CombatProperties.IncomingMultiplier));
                    transaction.Set(health,IsHealing?before+delta:Math.Max(0,before-delta));
                    if(context.Arguments.TryGetValue("combat.log",out var output)&&output is List<string> log)
                    {
                        string report=context.Self.GetValue(CombatProperties.Name).Current+" "+Label+" "+target.GetValue(CombatProperties.Name).Current+" for "+Math.Round(delta);
                        transaction.OnCommit(()=>log.Add(report));
                    }
                }
            }
            finally{context.Target=previous;}
        }
    }
    public sealed class AbilityDefinition
    {
        public string Id{get;}
        public ActionDefinition Action{get;}
        public AbilityDefinition(string id,params EffectDefinition[] effects){Id=id;Action=new ActionDefinition(id,null,effects);}
        public bool Execute(Combatant actor,int round,List<string> log,out string error)
        {var context=new EvaluationContext(actor);context.Arguments["round"]=round;context.Arguments["combat.log"]=log;return Action.Execute(context,out error);}
    }
    public sealed class AuraDefinition
    {
        public string Id{get;}
        public PropertyKey<double> Property{get;}
        public double Multiplier{get;}
        public AuraDefinition(string id,PropertyKey<double>property,double multiplier){Id=id;Property=property;Multiplier=multiplier;}
        public void Apply(Combatant source,TargetQuery query)
        {
            var transaction=new EntityTransaction();
            foreach(var target in query.Select(new EvaluationContext(source)))
                new NumericModifier<double>(Id+":"+source.Id+":"+target.Id,source.Id,Property,NumericOperation.Override,Multiplier).Stage(target,transaction);
            if(!transaction.Commit())throw new InvalidOperationException("Aura transaction failed.");
        }
    }
}
