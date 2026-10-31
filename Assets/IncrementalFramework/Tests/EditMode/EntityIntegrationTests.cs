using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using IncrementalFramework.Games.Rpg;
using IncrementalFramework.Games.Clicker;
using IncrementalFramework.Games.Strategy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IncrementalFramework.Tests
{
    public sealed class EntityIntegrationTests
    {
        private static readonly PropertyKey<int> Coins=new PropertyKey<int>("Coins");
        private static readonly PropertyKey<int> Score=new PropertyKey<int>("Score");
        [Test]public void OwnedGraphCopiesPreserveAliasesAndDoNotShareListeners()
        {var child=new EntityInstance("child");child.Define(Coins,5);var parent=new EntityTemplate("parent");parent.Define("Left",child);parent.Define("Right",child);int events=0;child.GetValue(Coins).Changed+=_=>events++;var copy=parent.Instantiate(new EntityContext(),"copy");var left=copy.GetValue<EntityInstance>("Left").Current;Assert.That(left,Is.SameAs(copy.GetValue<EntityInstance>("Right").Current));left.GetValue(Coins).Current=10;Assert.That(child.GetValue(Coins).Current,Is.EqualTo(5));Assert.That(events,Is.Zero);}
        [Test]public void TemplateEditsDoNotChangeExistingEconomyInstances()
        {var resource=new ResourceDefinition("gold",10);var producer=new ProducerDefinition("worker",null,new[]{new ResourceAmount("gold",1)});var content=new ContentDatabase(new[]{resource},new[]{producer});var a=new Simulation(content);producer.CostGrowth=2;resource.Initial=50;var b=new Simulation(content);Assert.That(a.GetResource("gold").Balance.ToDouble(),Is.EqualTo(10));Assert.That(a.GetProducer("worker").GetValue<double>("CostGrowth").Current,Is.EqualTo(1.15));Assert.That(b.GetResource("gold").Balance.ToDouble(),Is.EqualTo(50));}
        [Test]public void RuntimeConfigurationWritesInvalidateCompiledCaches()
        {var sim=new Simulation(new ContentDatabase(new[]{new ResourceDefinition("gold",100)},new[]{new ProducerDefinition("worker",new[]{new ResourceAmount("gold",10)},new[]{new ResourceAmount("gold",2)})}));sim.GetProducer("worker").Owned=1;sim.GetProducer("worker").GetValue<IReadOnlyList<ResourceAmount>>("Production").Current=new[]{new ResourceAmount("gold",5)};Assert.That(sim.GetResource("gold").Rate.ToDouble(),Is.EqualTo(5));sim.GetProducer("worker").GetValue<double>("CostGrowth").Current=2;Assert.That(sim.GetProducerCost("worker")[0].Amount.ToDouble(),Is.EqualTo(20));}
        [Test]public void IntegerOverflowInAModifierRollsBackOtherWritesAndNotifications()
        {var entity=new EntityInstance("x");var coins=entity.Define(Coins,int.MaxValue);var score=entity.Define(Score,1);int notifications=0;score.Changed+=_=>notifications++;var tx=new EntityTransaction();tx.Set(score,9);new NumericModifier<int>("overflow","source",Coins,NumericOperation.Flat,1).Stage(entity,tx);Assert.Throws<OverflowException>(()=>tx.Commit());Assert.That(score.Current,Is.EqualTo(1));Assert.That(coins.Current,Is.EqualTo(int.MaxValue));Assert.That(entity.Context.Modifiers.Count,Is.Zero);Assert.That(notifications,Is.Zero);}
        [Test]public void ModifierListenersObserveAllEffectsOfTheTransaction()
        {var entity=new EntityInstance("x");var coins=entity.Define(Coins,10);var score=entity.Define(Score,1);coins.Changed+=_=>Assert.That(score.Current,Is.EqualTo(9));var tx=new EntityTransaction();new NumericModifier<int>("first","source",Coins,NumericOperation.Flat,5).Stage(entity,tx);new NumericModifier<int>("second","source",Score,NumericOperation.Flat,8).Stage(entity,tx);Assert.That(tx.Commit(),Is.True);}
        [Test]public void EntitySnapshotsRestoreModifierSourcesAndDiscardCalculatedOverrides()
        {var template=new TemplateBuilder("wallet").Value(Coins,10).Calculated(Score,Expr.Multiply(Expr.Property(Coins),Expr.Constant(2))).Template;var world=new EntityContext();world.RegisterTemplate(template);var entity=template.Instantiate(world,"wallet");var tx=new EntityTransaction();new NumericModifier<int>("boost","item",Score,NumericOperation.Flat,5).Stage(entity,tx);tx.Commit();entity.GetValue(Score).Current=999;var snapshot=EntitySnapshots.Capture(world);var restored=new EntityContext();restored.RegisterTemplate(template);EntitySnapshots.Restore(restored,snapshot);Assert.That(restored.Find("wallet").GetValue(Score).Current,Is.EqualTo(25));restored.RemoveModifiers("item");Assert.That(restored.Find("wallet").GetValue(Score).Current,Is.EqualTo(20));}
        [Test]public void MalformedEntitySnapshotIsRejectedBeforeDestinationWrites()
        {var template=new EntityTemplate("wallet");template.Define(Coins,10);var world=new EntityContext();world.RegisterTemplate(template);template.Instantiate(world,"wallet");var snapshot=EntitySnapshots.Capture(world);snapshot.Entities[0].Properties["missing"]=new JValue(1);Assert.Throws<ArgumentException>(()=>EntitySnapshots.Restore(world,snapshot));Assert.That(world.Find("wallet").GetValue(Coins).Current,Is.EqualTo(10));}
        [Test]public void DelayedBehaviorSectionRoundTripsAnActiveContinuation()
        {var builder=new GameDefinitionBuilder();builder.Template("wallet").Value(Coins,0);builder.Action(new ActionDefinition("gain",null,Effects.Add(Coins,Expr.Constant(2))));builder.Behavior(new BehaviorDefinition("task",Behavior.Sequence(Behavior.Action("gain"),Behavior.Delay(5),Behavior.Action("gain"))));var definition=builder.Compile();var world=new EntityContext();world.RegisterTemplate(definition.Templates["wallet"]);definition.Templates["wallet"].Instantiate(world,"wallet");var runner=new BehaviorRunner(definition,world);runner.Start("task","wallet");runner.AdvanceTo(2);var data=new BehaviorSnapshotSection("jobs",runner).Capture();var snapshot=EntitySnapshots.Capture(world);var restoredWorld=new EntityContext();restoredWorld.RegisterTemplate(definition.Templates["wallet"]);EntitySnapshots.Restore(restoredWorld,snapshot);var restored=new BehaviorRunner(definition,restoredWorld);new BehaviorSnapshotSection("jobs",restored).Restore(1,data);restored.AdvanceTo(5);Assert.That(restoredWorld.Find("wallet").GetValue(Coins).Current,Is.EqualTo(4));Assert.That(restored.Jobs.Count,Is.Zero);}
        [Test]public void DeclarativeRuntimeSharesClockAndBudgetsWithProduction()
        {
            var builder=new GameDefinitionBuilder();builder.Template("actor").Value(Coins,0);
            builder.Action(new ActionDefinition("gain",null,Effects.Add(Coins,Expr.Constant(1))));
            builder.Behavior(new BehaviorDefinition("income",Behavior.Repeat(5,Behavior.Action("gain"))));
            var simulation=new Simulation(new ContentDatabase(new[]{new ResourceDefinition("gold")},new[]{new ProducerDefinition("worker",null,new[]{new ResourceAmount("gold",2)})}),eventBudget:1);
            simulation.Execute(new BuyProducerCommand("worker"));simulation.Schedule(new GrantResourceCommand("gold",3),2);simulation.Schedule(new GrantResourceCommand("gold",4),3);
            var definition=builder.Compile();var runtime=new DeclarativeRuntime(simulation,definition);var actor=definition.Templates["actor"].Instantiate(runtime.Entities,"actor");runtime.Behaviors.Start("income","actor");
            int calls=0;while(!runtime.AdvanceTo(10,2)){if(calls++>100)Assert.Fail("Shared advance did not converge.");}
            Assert.That(actor.GetValue(Coins).Current,Is.EqualTo(3));Assert.That(simulation.Time,Is.EqualTo(10));Assert.That(simulation.GetResource("gold").Balance.ToDouble(),Is.EqualTo(27));
        }
        [Test]public void DelayedTypedArgumentsAndModulePersistenceRoundTrip()
        {
            var declarations=new GameDefinitionBuilder();declarations.Template("actor").Value(Coins,0);
            declarations.Action(new ActionDefinition("gain",null,Effects.Add(Coins,Expr.Argument<int>("amount"))));
            declarations.Behavior(new BehaviorDefinition("task",Behavior.Sequence(Behavior.Delay(5),Behavior.Action("gain"))));
            var definition=declarations.Compile();var module=new DeclarativeModule(definition);var composition=new ModuleBuilder().Add(module);composition.Resource(new ResourceDefinition("gold"));
            var simulation=composition.Create();definition.Templates["actor"].Instantiate(simulation.Entities,"actor");module.Runtime.Behaviors.Start("task","actor",new Dictionary<string,object>{{"amount",7}});module.Runtime.AdvanceTo(2);
            var snapshot=SnapshotService.Deserialize(SnapshotService.Serialize(SnapshotService.Capture(simulation,new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc))));
            var fresh=new DeclarativeModule(definition);var restored=SnapshotService.Restore(simulation.InitialContent,snapshot,modules:new ModuleBuilder().Add(fresh));
            Assert.That(fresh.Runtime.AdvanceTo(5),Is.True);Assert.That(restored.Entities.Find("actor").GetValue(Coins).Current,Is.EqualTo(7));
        }
        private sealed class AdvancedEffect:IEffectHandler
        {public void Stage(EffectExecutionContext context,EntityTransaction tx){context.Set(tx,context.ActorId,Coins,context.Read(context.ActorId,Coins)+7);}}
        [Test]public void CustomEffectsUseTheSameAtomicExecutionContract()
        {var builder=new GameDefinitionBuilder();builder.Template("wallet").Value(Coins,10);builder.Extensions.Register<IEffectHandler>("advanced",new AdvancedEffect());builder.Action(new ActionDefinition("invoke",null,Effects.Custom("advanced")));var definition=builder.Compile();var entity=definition.Templates["wallet"].Instantiate(new EntityContext(),"wallet");Assert.That(definition.Actions["invoke"].Execute(new EvaluationContext(entity,definition.Extensions),out _),Is.True);Assert.That(entity.GetValue(Coins).Current,Is.EqualTo(17));}
        [Test]public void MissingCustomConditionIsRejectedDuringCompilation()
        {var builder=new GameDefinitionBuilder();builder.Template("wallet").Value(Coins,10);builder.Action(new ActionDefinition("invalid",Expr.Custom<bool>("missing"),Effects.Add(Coins,Expr.Constant(1))));Assert.Throws<ArgumentException>(()=>builder.Compile());}
        [Test]public void ClickerModuleUsesObservableValuesAndCommonEffects()
        {var simulation=new Simulation(new ContentDatabase(new[]{new ResourceDefinition("gold")}));var module=new ClickerModule(simulation,new ClickDefinition("gold",3));Assert.That(module.Click().Success,Is.True);Assert.That(simulation.GetResource("gold").Balance.ToDouble(),Is.EqualTo(3));Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(),Is.EqualTo(3));}
        [Test]public void StrategyTerritoriesAreObservableAndFollowDeclarativeConquestRules()
        {var territory=new TerritoryDefinition("territory:first","First","claim");var rule=territory.Conquest(Array.Empty<RequirementDefinition>(),new GrantResourceAction("gold",5));var simulation=new Simulation(new ContentDatabase(new[]{new ResourceDefinition("gold")},rules:new[]{rule}));using(var module=new StrategyModule(simulation,new[]{territory})){simulation.Execute(new TriggerRuleCommand("claim"));simulation.AdvanceTo(simulation.Time);Assert.That(module.Territories[0].GetValue<bool>("Claimed").Current,Is.True);Assert.That(simulation.GetResource("gold").Balance.ToDouble(),Is.EqualTo(5));}}
        [Test]public void SharedAssembliesCannotReferenceGenresAndGenresCannotReferenceEachOther()
        {
            foreach(var type in new[]{typeof(Entity),typeof(GameDefinitionBuilder),typeof(ContentDatabase),typeof(Simulation),typeof(SnapshotService)})
                Assert.That(type.Assembly.GetReferencedAssemblies().Any(x=>x.Name.StartsWith("IncrementalFramework.Games.",StringComparison.Ordinal)||x.Name.StartsWith("IncrementalFramework.Samples.",StringComparison.Ordinal)),Is.False,type.FullName);
            foreach(var type in new[]{typeof(GuildGame),typeof(ClickerModule),typeof(StrategyModule)})
                Assert.That(type.Assembly.GetReferencedAssemblies().Any(x=>(x.Name.StartsWith("IncrementalFramework.Games.",StringComparison.Ordinal)&&x.Name!=type.Assembly.GetName().Name)||x.Name.StartsWith("IncrementalFramework.Samples.",StringComparison.Ordinal)||x.Name.StartsWith("UnityEngine",StringComparison.Ordinal)),Is.False,type.FullName);
        }
    }
}
