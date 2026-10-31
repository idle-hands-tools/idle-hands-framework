using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using NUnit.Framework;

namespace IncrementalFramework.Tests
{
    public sealed class EntityFoundationTests
    {
        private static readonly PropertyKey<double> Health=new PropertyKey<double>("Health");
        private static readonly PropertyKey<double> Power=new PropertyKey<double>("Power");
        private static readonly PropertyKey<int> Coins=new PropertyKey<int>("Coins");
        private sealed class Hero:EntityInstance
        {public double Health{get=>Get<double>(nameof(Health));set=>Set(nameof(Health),value);}public Hero(EntityContext context=null):base("hero","hero",context){Define(nameof(Health),100d);}}
        [Test]public void OrdinaryPropertiesRetainWrapperAndPublishOldAndNewValues()
        {var hero=new Hero();var wrapper=hero.GetValue(Health);var changes=new List<ValueChange<double>>();wrapper.Changed+=changes.Add;hero.Health-=10;hero.Health=90;Assert.That(hero.Health,Is.EqualTo(90));Assert.That(hero.GetValue(Health),Is.SameAs(wrapper));Assert.That(changes.Count,Is.EqualTo(1));Assert.That(changes[0].OldValue,Is.EqualTo(100));Assert.That(changes[0].NewValue,Is.EqualTo(90));}
        [Test]public void TemplatesClonePropertiesCollectionsAndListeners()
        {var template=new EntityTemplate("hero");template.Define(Health,100d);template.DefineList("Tags",new[]{"original"});var world=new EntityContext();var a=template.Instantiate(world,"a");var b=template.Instantiate(world,"b");int changed=0;a.GetValue(Health).Changed+=_=>changed++;a.GetValue(Health).Current=5;((ValueList<string>)a.GetValue("Tags")).Add("a");template.GetValue(Health).Current=200;var c=template.Instantiate(world,"c");Assert.That(b.GetValue(Health).Current,Is.EqualTo(100));Assert.That(c.GetValue(Health).Current,Is.EqualTo(200));Assert.That(((ValueList<string>)b.GetValue("Tags")).Current.Count,Is.EqualTo(1));Assert.That(changed,Is.EqualTo(1));}
        [Test]public void CalculatedAssignmentsWarnAndLastUntilARecalculation()
        {var entity=new EntityInstance("x");entity.Define(Health,10d);var power=entity.Define(Power,0d);power.Calculate(owner=>owner.GetValue(Health).Current*2,"Health");var codes=new List<string>();entity.Context.Diagnostic+=x=>codes.Add(x.Code);power.Current=99;Assert.That(power.Current,Is.EqualTo(99));Assert.That(codes,Contains.Item("calculated-write"));entity.GetValue(Health).Current=20;Assert.That(power.Current,Is.EqualTo(40));power.Current=55;power.Recalculate();Assert.That(power.Current,Is.EqualTo(40));}
        [Test]public void DerivedValuesAreCoherentInsideListenersAndFailuresAreContained()
        {var entity=new EntityInstance("x");var root=entity.Define(Health,10d);var power=entity.Define(Power,0d);power.Calculate(owner=>owner.GetValue(Health).Current*2,"Health");int called=0;root.Changed+=_=>throw new Exception("observer failed");root.Changed+=_=>{Assert.That(power.Current,Is.EqualTo(40));called++;};root.Current=20;Assert.That(called,Is.EqualTo(1));}
        [Test]public void NotificationCyclesAreBounded()
        {var entity=new Hero();string diagnostic=null;entity.Context.Diagnostic+=x=>diagnostic=x.Code;entity.GetValue(Health).Changed+=_=>entity.Health++;entity.Health=1;Assert.That(diagnostic,Is.EqualTo("notification-budget"));}
        [Test]public void TransactionCommitsAllPropertiesBeforeNotificationsAndRejectsFailedRequirements()
        {var entity=new EntityInstance("x");var coins=entity.Define(Coins,100);var health=entity.Define(Health,10d);coins.Changed+=_=>Assert.That(health.Current,Is.EqualTo(30));var tx=new EntityTransaction();tx.Set(coins,50);tx.Set(health,30d);Assert.That(tx.Commit(),Is.True);tx=new EntityTransaction();tx.Set(coins,0);tx.Require(()=>false);Assert.That(tx.Commit(),Is.False);Assert.That(coins.Current,Is.EqualTo(50));}
        [Test]public void ModifierStackingAndSourceRemovalDoNotCompound()
        {var entity=new EntityInstance("x");var value=entity.Define(Health,10d);var tx=new EntityTransaction();new NumericModifier<double>("flat","weapon",Health,NumericOperation.Flat,5).Stage(entity,tx);new NumericModifier<double>("percent","training",Health,NumericOperation.AddPercent,.5).Stage(entity,tx);new NumericModifier<double>("multiply","buff",Health,NumericOperation.Multiply,2).Stage(entity,tx);Assert.That(tx.Commit(),Is.True);Assert.That(value.Current,Is.EqualTo(45));entity.Context.RemoveModifiers("buff");Assert.That(value.Current,Is.EqualTo(22.5));entity.Context.RemoveModifiers("weapon");Assert.That(value.Current,Is.EqualTo(15));entity.Context.RemoveModifiers("training");Assert.That(value.Current,Is.EqualTo(10));}
        [Test]public void DirectEconomyWritesUpdateCanonicalStateAndProductionCaches()
        {var sim=new Simulation(new ContentDatabase(new[]{new ResourceDefinition("gold",100)},new[]{new ProducerDefinition("worker",new[]{new ResourceAmount("gold",10)},new[]{new ResourceAmount("gold",2)})}));var gold=sim.GetResource("gold");int changes=0;gold.GetValue<BreakInfinity.BigDouble>("Balance").Changed+=_=>changes++;gold.Balance=-5;Assert.That(gold.Balance,Is.EqualTo(new BreakInfinity.BigDouble(-5)));Assert.That(changes,Is.EqualTo(1));sim.GetProducer("worker").Owned=3;Assert.That(gold.Rate,Is.EqualTo(new BreakInfinity.BigDouble(6)));}
        [Test]public void DeclaredRecipeSpendsAndGrantsAtomically()
        {var builder=new GameDefinitionBuilder();builder.Template("wallet").Value(Coins,50).Value(Health,10d);var recipe=new ActionDefinition("heal",null,Effects.Spend(Coins,Expr.Constant(20)),Effects.Add(Health,Expr.Constant(15d)));builder.Action(recipe);var definition=builder.Compile();var wallet=definition.Templates["wallet"].Instantiate(new EntityContext(),"wallet");var c=new EvaluationContext(wallet);Assert.That(recipe.Execute(c,out _),Is.True);Assert.That(recipe.Execute(c,out _),Is.True);Assert.That(recipe.Execute(c,out _),Is.False);Assert.That(wallet.GetValue(Coins).Current,Is.EqualTo(10));Assert.That(wallet.GetValue(Health).Current,Is.EqualTo(40));}
        [Test]public void DeclaredCalculatedPropertiesAndInvalidReferencesAreValidated()
        {var builder=new GameDefinitionBuilder();builder.Template("hero").Value(Health,10d).Calculated(Power,Expr.Multiply(Expr.Property(Health),Expr.Constant(2d)));var definition=builder.Compile();var hero=definition.Templates["hero"].Instantiate(new EntityContext(),"hero");Assert.That(hero.GetValue(Power).Current,Is.EqualTo(20));hero.GetValue(Health).Current=15;Assert.That(hero.GetValue(Power).Current,Is.EqualTo(30));builder.Action(new ActionDefinition("bad",null,Effects.Set(new PropertyKey<int>("missing"),Expr.Constant(1))));Assert.Throws<ArgumentException>(()=>builder.Compile());}
        [Test]public void CalculatedDependencyCyclesAreRejected()
        {var builder=new GameDefinitionBuilder();builder.Template("cycle").Calculated(Health,Expr.Property(Power)).Calculated(Power,Expr.Property(Health));Assert.Throws<ArgumentException>(()=>builder.Compile());}
        [Test]public void DelayedBehaviorsResumeExactlyWithABoundedBudget()
        {var builder=new GameDefinitionBuilder();builder.Template("wallet").Value(Coins,0);builder.Action(new ActionDefinition("gain",null,Effects.Add(Coins,Expr.Constant(1))));builder.Behavior(new BehaviorDefinition("income",Behavior.Repeat(5,Behavior.Action("gain"))));var definition=builder.Compile();var world=new EntityContext();var wallet=definition.Templates["wallet"].Instantiate(world,"wallet");var runner=new BehaviorRunner(definition,world);runner.Start("income","wallet");int calls=0;while(!runner.AdvanceTo(20,2)){if(calls++>100)Assert.Fail();}Assert.That(wallet.GetValue(Coins).Current,Is.EqualTo(5));Assert.That(runner.Jobs[0].WakeTime,Is.EqualTo(25));}
        private sealed class AdvancedCalculation:ICalculation<double>{public double Evaluate(ReadOnlyEvaluationContext context)=>context.Read(context.ActorId,Health)*3;}
        [Test]public void RegisteredCodeCanExtendDeclarations()
        {var entity=new Hero();var registry=new ExtensionRegistry().Register<ICalculation<double>>("advanced",new AdvancedCalculation());Assert.That(Expr.Custom<double>("advanced").Evaluate(new EvaluationContext(entity,registry)),Is.EqualTo(300));Assert.Throws<ArgumentException>(()=>Expr.Custom<double>("missing").Evaluate(new EvaluationContext(entity,registry)));}
        [Test]public void SelectorsFilterOrderAndBreakTiesByStableIdentity()
        {var world=new EntityContext();var template=new EntityTemplate("hero");template.Define(Health,100d);var b=template.Instantiate(world,"b");var a=template.Instantiate(world,"a");var q=TargetQuery.All().Where(Expr.AtLeast(Expr.Property(Health,EntityScope.Target),Expr.Constant(1d))).OrderBy(Expr.Property(Health,EntityScope.Target)).Take(1);Assert.That(q.Select(new EvaluationContext(b))[0],Is.SameAs(a));}
    }
}
