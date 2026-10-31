using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;
using IncrementalFramework.Persistence;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Graphs;
using IncrementalFramework.Progression.Timing;
using NUnit.Framework;
#if UNITY_EDITOR
using IncrementalFramework.Unity;
using UnityEngine;
#endif

namespace IdleHands.Tests
{
    public sealed class GraphTests
    {
        private static readonly DateTime Utc=new DateTime(2026,10,30,0,0,0,DateTimeKind.Utc);
        private static GraphRequirement Enabled()=>GraphRequirements.State("enabled",Expr.Property(new PropertyKey<bool>("Enabled")));
        private static GraphEffect Reward(double amount)=>GraphEffect.Native(new GrantResourceAction("gold",amount));
        private static EntityTemplate Owner()
        {var owner=new EntityTemplate("player");owner.Define("Enabled",true);owner.Define("Points",0);return owner;}
        private static ModuleBuilder Builder(GraphModule module,double gold=10)
        {
            var b=new ModuleBuilder();b.Resource(new ResourceDefinition("gold",gold));
            b.Producer(new ProducerDefinition("worker",null,new[]{new IncrementalFramework.Economy.ResourceAmount("gold",1)}));
            b.Entity(Owner());b.Add(module);return b;
        }
        private static (Simulation Sim,GraphModule Module) Create(GraphDefinition definition,int budget=10000,double gold=10,GraphExtensions extensions=null)
        {
            var module=new GraphModule(new[]{definition},extensions);var sim=Builder(module,gold).Create(eventBudget:budget);
            Owner().Instantiate(sim.Entities,"player:1");Owner().Instantiate(sim.Entities,"player:2");return(sim,module);
        }
        private static void Drain(Simulation sim,double target)
        {int calls=0;while(!sim.AdvanceTo(target).Completed)Assert.That(++calls,Is.LessThan(20000),"Graph work did not settle.");}
        private static void Start(Simulation sim,string instance="quest",string owner="player:1",string definition="test")
        {Assert.That(sim.Execute(new StartGraphCommand(instance,definition,owner)).Success,Is.True);Drain(sim,sim.Time);}
        private static GraphNodeState Node(GraphModule module,string id,string instance="quest")=>module.Runtime.Instance(instance).Nodes.Single(n=>n.Id==id);
        private static void Signal(Simulation sim,string key,long count=1,string owner="player:1")
        {Assert.That(sim.Execute(new PublishGraphSignalCommand(owner,key,count)).Success,Is.True);Drain(sim,sim.Time);}
        private static void SetEnabled(Simulation sim,bool value)
        {sim.Entities.Find("player:1").GetValue<bool>("Enabled").Current=value;Drain(sim,sim.Time);}
        private static GraphDefinition Single(GraphRequirement trigger=null,GraphRequirement activation=null,GraphActivationMode mode=GraphActivationMode.Latched,
            GraphNodeKind kind=GraphNodeKind.Objective,IEnumerable<GraphEffect> complete=null,IEnumerable<GraphEffect> activate=null,double? cooldown=null,ResetPolicy reset=ResetPolicy.Preserve)
            =>new GraphDefinition("test",new[]{new GraphNodeDefinition("end",kind,true,activation:activation,trigger:trigger,activationMode:mode,onActivate:activate,onComplete:complete)},restartCooldown:cooldown,prestigePolicy:reset);

        [Test]
        public void MissingRequirementsAndKindDefaults()
        {
            var (sim,module)=Create(Single());Start(sim);
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));
            Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.True);
            Assert.That(module.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Completed));
            var (automatic,events)=Create(Single(kind:GraphNodeKind.Event,complete:new[]{Reward(2)}));Start(automatic);
            Assert.That(events.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Completed));Assert.That(automatic.GetResource("gold").Balance,Is.EqualTo(new BigDouble(12)));
        }
        [Test]
        public void ActivationLatchesButReadinessReverts()
        {
            var trigger=GraphRequirements.State("points",Expr.AtLeast(Expr.Property(new PropertyKey<int>("Points")),Expr.Constant(2)));
            var(sim,module)=Create(Single(trigger,Enabled()));Start(sim);SetEnabled(sim,false);
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Active));
            var points=sim.Entities.Find("player:1").GetValue<int>("Points");points.Current=2;Drain(sim,0);Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));
            points.Current=0;Drain(sim,0);Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Active));
            Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.False);
        }
        [Test]
        public void MaintainedNodeSuspendsAndDoesNotRepeatActivationEffects()
        {
            var(sim,module)=Create(Single(GraphRequirements.Signal("hit",2),Enabled(),GraphActivationMode.Maintained,activate:new[]{Reward(3)}));Start(sim);Signal(sim,"hit");SetEnabled(sim,false);Signal(sim,"hit",10);
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Suspended));SetEnabled(sim,true);Signal(sim,"hit");
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(13)));
        }
        [Test]
        public void GraphSuspensionPausesActiveTimeAndGraphEffectsRunOnce()
        {
            var definition=new GraphDefinition("test",new[]{new GraphNodeDefinition("end",isEnd:true,trigger:GraphRequirements.Time(5,GraphTimeScope.ActiveElapsed))},activation:Enabled(),activationMode:GraphActivationMode.Maintained,onActivate:new[]{Reward(1)});
            var(sim,module)=Create(definition);Start(sim);Drain(sim,2);SetEnabled(sim,false);Drain(sim,100);
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Suspended));SetEnabled(sim,true);Drain(sim,102);
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Active));Drain(sim,103);Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(11)));
        }
        [Test]
        public void ParallelAllJoinAndAnyJoin()
        {
            foreach(var mode in new[]{GraphPrerequisiteMode.All,GraphPrerequisiteMode.Any})
            {
                var definition=new GraphDefinition("test",new[]{new GraphNodeDefinition("a"),new GraphNodeDefinition("b"),new GraphNodeDefinition("end",GraphNodeKind.Event,true,new[]{"a","b"},mode)});
                var(sim,module)=Create(definition);Start(sim);Assert.That(Node(module,"a").Status,Is.EqualTo(GraphNodeStatus.Ready));Assert.That(Node(module,"b").Status,Is.EqualTo(GraphNodeStatus.Ready));
                Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","a",1)).Success,Is.True);Drain(sim,0);
                if(mode==GraphPrerequisiteMode.All){Assert.That(module.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Active));sim.Execute(new CompleteGraphNodeCommand("quest","b",1));Drain(sim,0);}
                else Assert.That(Node(module,"b").Status,Is.EqualTo(GraphNodeStatus.Skipped));
                Assert.That(module.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Completed));
            }
        }
        [Test]
        public void CompetingEndNodesHaveStableOrderAndOnlyOneReward()
        {
            var definition=new GraphDefinition("test",new[]{new GraphNodeDefinition("z",GraphNodeKind.Event,true,onComplete:new[]{Reward(100)}),new GraphNodeDefinition("a",GraphNodeKind.Event,true,onComplete:new[]{Reward(1)})},onComplete:new[]{Reward(2)});
            var(sim,module)=Create(definition,1);Start(sim);Assert.That(module.Runtime.Instance("quest").Ending,Is.EqualTo("a"));Assert.That(Node(module,"z").Status,Is.EqualTo(GraphNodeStatus.Skipped));Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(13)));
        }
        [Test]
        public void SignalScopesCreditEarlierRunProgressButNotEarlierActiveProgress()
        {
            foreach(var scope in new[]{GraphProgressScope.Run,GraphProgressScope.Active})
            {
                var(sim,module)=Create(Single(GraphRequirements.Signal("hit",2,scope),Enabled()));SetEnabled(sim,false);Start(sim);Signal(sim,"hit",2);SetEnabled(sim,true);
                Assert.That(Node(module,"end").Status,Is.EqualTo(scope==GraphProgressScope.Run?GraphNodeStatus.Ready:GraphNodeStatus.Active));
            }
        }
        [Test]
        public void OwnersInstancesAndDetachedExportsAreIsolated()
        {
            var(sim,module)=Create(Single(GraphRequirements.Signal("hit")));Start(sim);Start(sim,"other","player:2");Signal(sim,"hit");
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));Assert.That(Node(module,"end","other").Status,Is.EqualTo(GraphNodeStatus.Active));
            module.Runtime.Instance("quest").Nodes[0].Status=GraphNodeStatus.Completed;module.Runtime.Export().Instances.Clear();Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));
        }
        [Test]
        public void AutomaticAndExplicitFailuresKeepRewardsAndCompletionAtomic()
        {
            foreach(var kind in new[]{GraphNodeKind.Event,GraphNodeKind.Objective})
            {
                var(sim,module)=Create(Single(kind:kind,complete:new[]{GraphEffect.Native(new SpendResourceAction("gold",20)),Reward(100)}));Start(sim);
                if(kind==GraphNodeKind.Objective)Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.False);
                Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));Assert.That(Node(module,"end").LastError,Is.Not.Null.And.Not.Empty);Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(10)));
                sim.Execute(new GrantResourceCommand("gold",10));Drain(sim,sim.Time);
                if(kind==GraphNodeKind.Objective)Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.True);
                Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(100)));Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.False);
            }
        }
        [Test]
        public void CompletionArithmeticFailureRecordsErrorWithoutPartialState()
        {
            var huge=BigDouble.FromMantissaExponentNoNormalize(6,long.MaxValue);
            var definition=Single(complete:new[]{GraphEffect.Native(new GrantResourceAction("gold",huge))});
            var module=new GraphModule(new[]{definition});var builder=Builder(module,0);var content=builder.Compile();
            var state=new Simulation(content).ExportState();state.Resources.Single(r=>r.Id=="gold").LifetimeEarned=huge;
            var sim=Simulation.FromState(content,state);builder.Attach(sim);Owner().Instantiate(sim.Entities,"player:1");Start(sim);
            Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.False);
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));Assert.That(Node(module,"end").LastError,Is.Not.Null.And.Not.Empty);
            Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(BigDouble.Zero));Assert.That(sim.GetResource("gold").LifetimeEarned,Is.EqualTo(huge));
        }
        [Test]
        public void GraphCompletionEffectsRollBackNodeCompletionTogether()
        {
            var definition=new GraphDefinition("test",new[]{new GraphNodeDefinition("end",isEnd:true,onComplete:new[]{Reward(100)})},onComplete:new[]{GraphEffect.Native(new SpendResourceAction("gold",20))});
            var(sim,module)=Create(definition);Start(sim);Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.False);Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(10)));
        }
        [Test]
        public void ResourceAndTimeDeadlinesAreExactAndNativeEventsWinTies()
        {
            var(sim,module)=Create(Single(GraphRequirements.Resource("gold",5),kind:GraphNodeKind.Event,complete:new[]{Reward(10)}),gold:0);
            sim.Execute(new BuyProducerCommand("worker"));Start(sim);Drain(sim,4.9);Assert.That(module.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Active));Drain(sim,5);Assert.That(module.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Completed));
            Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(15)));
            var definition=Single(GraphRequirements.Time(5),GraphRequirements.Not(GraphRequirements.Resource("gold",10)),GraphActivationMode.Maintained,GraphNodeKind.Event,new[]{Reward(100)});
            var(tie,tieModule)=Create(definition,gold:0);tie.Execute(new BuyProducerCommand("worker"));Start(tie);tie.Schedule(new GrantResourceCommand("gold",10),5);Drain(tie,5);Assert.That(tieModule.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Active));Assert.That(Node(tieModule,"end").Status,Is.EqualTo(GraphNodeStatus.Suspended));Assert.That(tie.GetResource("gold").Balance,Is.EqualTo(new BigDouble(15)));
        }
        [Test]
        public void RestartRunsClearProgressRejectStaleCommandsAndHonorCooldown()
        {
            var(sim,module)=Create(Single(GraphRequirements.Signal("hit"),cooldown:5));Start(sim);Signal(sim,"hit");sim.Execute(new CompleteGraphNodeCommand("quest","end",1));
            Assert.That(sim.Execute(new RestartGraphCommand("quest",1)).Success,Is.False);Drain(sim,5);
            Assert.That(module.Runtime.Instance("quest").Run,Is.EqualTo(2));Assert.That(module.Runtime.Instance("quest").PreviousEnding,Is.EqualTo("end"));Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Active));Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.False);
            var(manual,manualModule)=Create(Single());Start(manual);manual.Execute(new CompleteGraphNodeCommand("quest","end",1));Assert.That(manual.Execute(new RestartGraphCommand("quest",1)).Success,Is.True);Drain(manual,0);Assert.That(manualModule.Runtime.Instance("quest").Run,Is.EqualTo(2));
        }
        [Test]
        public void SaveLoadPendingWorkAndEveryLifecycleBoundaryDoNotReplayRewards()
        {
            var definition=Single(GraphRequirements.Time(5),Enabled(),GraphActivationMode.Maintained,complete:new[]{Reward(10)},activate:new[]{Reward(1)});
            var(sim,module)=Create(definition,1);sim.Execute(new StartGraphCommand("quest","test","player:1"));Assert.That(sim.AdvanceTo(2).Completed,Is.False);
            GraphModule restored=null;
            Simulation Reload(Simulation source)
            {var snapshot=SnapshotService.Deserialize(SnapshotService.Serialize(SnapshotService.Capture(source,Utc)));restored=new GraphModule(new[]{definition});return SnapshotService.Restore(source.InitialContent,snapshot,eventBudget:1,modules:Builder(restored));}
            sim=Reload(sim);Drain(sim,2);Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(11)));SetEnabled(sim,false);sim=Reload(sim);Assert.That(Node(restored,"end").Status,Is.EqualTo(GraphNodeStatus.Suspended));SetEnabled(sim,true);Drain(sim,5);sim=Reload(sim);Assert.That(Node(restored,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));
            Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.True);sim=Reload(sim);Drain(sim,10);Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(21)));Assert.That(restored.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Completed));
        }
        [Test]
        public void SnapshotRejectsDifferentContentAndCorruptedState()
        {
            var(sim,module)=Create(Single());Start(sim);var snapshot=module.Runtime.Export();snapshot.Instances[0].Nodes[0].ActiveSeconds=double.NaN;Assert.Throws<ArgumentException>(()=>module.Runtime.Import(snapshot));
            var different=new GraphModule(new[]{Single(GraphRequirements.Time(2))});Assert.Throws<ArgumentException>(()=>different.Validate(1,module.Capture()));
        }
        [Test]
        public void ContinuousChunkedOfflineAndBoostedAdvancementAgree()
        {
            var definition=Single(GraphRequirements.Time(8),kind:GraphNodeKind.Event,complete:new[]{Reward(3)});
            var(full,fm)=Create(definition);Start(full);Drain(full,8);
            var(chunked,cm)=Create(definition,1);Start(chunked);for(int t=1;t<=8;t++)Drain(chunked,t);
            Assert.That(cm.Runtime.Instance("quest").Status,Is.EqualTo(fm.Runtime.Instance("quest").Status));Assert.That(chunked.GetResource("gold").Balance,Is.EqualTo(full.GetResource("gold").Balance));
            var(banked,bm)=Create(definition,1);Start(banked);var timing=TimingSnapshotService.Attach(new SimulationTimeline(banked),new TimingOptions{OfflineMode=OfflineProgressMode.BankTime,WorkBudget=1});timing.ClaimOffline(Utc,Utc.AddSeconds(8));timing.SetBoostMultiplier(4);timing.AdvanceOnline(2);int calls=0;while(timing.HasPendingWork){timing.Continue(1);Assert.That(++calls,Is.LessThan(1000));}Assert.That(banked.Time,Is.EqualTo(8).Within(1e-9));Assert.That(bm.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Completed));
            var(offline,om)=Create(definition);Start(offline);var controller=TimingSnapshotService.Attach(new SimulationTimeline(offline));controller.ClaimOffline(Utc,Utc.AddSeconds(8));while(controller.HasPendingWork)controller.Continue();Assert.That(offline.GetResource("gold").Balance,Is.EqualTo(full.GetResource("gold").Balance));
        }
        [Test]
        public void ObservableViewUpdatesAcrossRestoreWithoutRepeatingSubscriptions()
        {
            var definition=Single(GraphRequirements.Signal("hit"));var(sim,module)=Create(definition);Start(sim);var view=module.Runtime.View("quest","end");int changes=0;view.CanComplete.Changed+=_=>changes++;Signal(sim,"hit");Assert.That(view.CanComplete.Current,Is.True);Assert.That(changes,Is.EqualTo(1));
            var snapshot=SnapshotService.Capture(sim,Utc);var restored=new GraphModule(new[]{definition});var clone=SnapshotService.Restore(sim.InitialContent,snapshot,modules:Builder(restored));var next=restored.Runtime.View("quest","end");Assert.That(next.CanComplete.Current,Is.True);clone.Execute(new CompleteGraphNodeCommand("quest","end",1));Assert.That(next.Status.Current,Is.EqualTo(GraphNodeStatus.Completed));Assert.That(changes,Is.EqualTo(1));
        }
        [Test]
        public void EmptyRuntimeDoesNotConsumeWorkAndInvalidRequestsReject()
        {
            var(sim,module)=Create(Single());Assert.That(sim.AdvanceTo(100).ProcessedWork,Is.Zero);Assert.That(sim.Execute(new StartGraphCommand("quest","missing","player:1")).Success,Is.False);Assert.That(sim.Execute(new StartGraphCommand("quest","test","missing")).Success,Is.False);Start(sim);Assert.That(sim.Execute(new StartGraphCommand("quest","test","player:1")).Success,Is.False);Assert.That(sim.Execute(new PublishGraphSignalCommand("player:1","hit",0)).Success,Is.False);
        }
        [Test]
        public void CompilerRejectsInvalidGraphsAndMissingExtensions()
        {
            var invalid=new[]{new GraphDefinition("empty",Array.Empty<GraphNodeDefinition>()),new GraphDefinition("duplicate",new[]{new GraphNodeDefinition("x",isEnd:true),new GraphNodeDefinition("x",isEnd:true)}),
                new GraphDefinition("missing",new[]{new GraphNodeDefinition("x",isEnd:true,prerequisites:new[]{"no"})}),new GraphDefinition("cycle",new[]{new GraphNodeDefinition("x",prerequisites:new[]{"y"}),new GraphNodeDefinition("y",isEnd:true,prerequisites:new[]{"x"})}),
                new GraphDefinition("no-end",new[]{new GraphNodeDefinition("x")}),new GraphDefinition("orphan",new[]{new GraphNodeDefinition("x"),new GraphNodeDefinition("end",isEnd:true)}),Single(GraphRequirements.Custom("missing")),Single(complete:new[]{GraphEffect.Custom("missing")}),Single(GraphRequirements.NodeCompleted("end")),new GraphDefinition("test",new[]{new GraphNodeDefinition("end",isEnd:true)},restartCooldown:0)};
            foreach(var definition in invalid)Assert.Throws<ArgumentException>(()=>GraphCompiler.Compile(definition),definition.Id);
        }
        [Test]
        public void FutureCancelledEventsCannotHideCurrentGraphWork()
        {
            var(sim,module)=Create(Single(GraphRequirements.Time(5),kind:GraphNodeKind.Event),1);
            var token=sim.Schedule(new GrantResourceCommand("gold",1),1000);sim.Cancel(token);
            Start(sim);Drain(sim,5);Assert.That(module.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Completed));
        }
        private sealed class TimedRequirement : IGraphRequirementHandler
        {
            public IReadOnlyCollection<PropertyReference> Dependencies=>Array.Empty<PropertyReference>();
            public bool Evaluate(GraphEvaluationContext context)=>context.Time>=3;
            public double? NextEvaluationTime(GraphEvaluationContext context)=>context.Time<3?3:(double?)null;
        }
        private sealed class CounterEffect : IGraphEffectHandler
        {
            public readonly DenseStateStore<int> Store=new DenseStateStore<int>();
            public bool Fail;
            public CounterEffect(){Store.Register("count",0);}
            public void Stage(GraphEvaluationContext context,SimulationTransaction transaction)
            {transaction.Set(Store,0,Store[0]+1);if(Fail)throw new InvalidOperationException("Test extension rejected.");}
        }
        [Test]
        public void CustomRequirementsAndEffectsUseSavedDeadlinesAndAtomicStoreWrites()
        {
            var effect=new CounterEffect{Fail=true};var extensions=new GraphExtensions().Requirement("after-three",new TimedRequirement()).Effect("counter",effect);
            var definition=Single(GraphRequirements.Custom("after-three"),complete:new[]{GraphEffect.Custom("counter"),Reward(2)});
            var module=new GraphModule(new[]{definition},extensions);var builder=Builder(module);builder.State("counter",effect.Store);var sim=builder.Create();Owner().Instantiate(sim.Entities,"player:1");Start(sim);Drain(sim,3);
            Assert.That(Node(module,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.False);Assert.That(effect.Store[0],Is.Zero);Assert.That(Node(module,"end").LastError,Does.Contain("extension rejected"));
            effect.Fail=false;Assert.That(sim.Execute(new CompleteGraphNodeCommand("quest","end",1)).Success,Is.True);Assert.That(effect.Store[0],Is.EqualTo(1));Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(12)));
        }
        private sealed class VetoReset : IPrestigeResetParticipant
        {
            public ResetPolicy Policy=>ResetPolicy.Transform;
            public CommandResult Prepare(SimulationReadModel model,StateTransaction transaction)=>CommandResult.Fail("Reset vetoed.");
        }
        [Test]
        public void PrestigeResetsOnlyConfiguredGraphsAndVetoLeavesStateUntouched()
        {
            var definitions=new[]{new GraphDefinition("preserve",new[]{new GraphNodeDefinition("end",isEnd:true)}),new GraphDefinition("reset",new[]{new GraphNodeDefinition("end",isEnd:true)},prestigePolicy:ResetPolicy.Reset)};
            var module=new GraphModule(definitions);var builder=Builder(module);builder.Prestige(new PrestigeDefinition("gold",1,2));var sim=builder.Create();Owner().Instantiate(sim.Entities,"player:1");Start(sim,"keep",definition:"preserve");Start(sim,"clear",definition:"reset");sim.Execute(new CompleteGraphNodeCommand("keep","end",1));sim.Execute(new CompleteGraphNodeCommand("clear","end",1));
            Assert.That(sim.Execute(new PrestigeCommand()).Success,Is.True);Drain(sim,0);Assert.That(module.Runtime.Instance("keep").Run,Is.EqualTo(1));Assert.That(module.Runtime.Instance("keep").Status,Is.EqualTo(GraphStatus.Completed));Assert.That(module.Runtime.Instance("clear").Run,Is.EqualTo(2));Assert.That(module.Runtime.Instance("clear").Status,Is.EqualTo(GraphStatus.Active));
            var vetoModule=new GraphModule(definitions);var vetoBuilder=Builder(vetoModule);vetoBuilder.Prestige(new PrestigeDefinition("gold",1,2));vetoBuilder.Reset(new VetoReset());var veto=vetoBuilder.Create();Owner().Instantiate(veto.Entities,"player:1");Start(veto,"clear",definition:"reset");
            Assert.That(veto.Execute(new PrestigeCommand()).Success,Is.False);Assert.That(vetoModule.Runtime.Instance("clear").Run,Is.EqualTo(1));Assert.That(veto.GetResource("gold").Balance,Is.EqualTo(new BigDouble(10)));

        }
        [Test]
        public void NativeLifecycleActionsUseFlagsUnlocksScheduledGrantsAndModifiers()
        {
            var modifier=new ModifierDefinition("bonus",ModifierTarget.GlobalProduction,null,2);
            var definition=Single(GraphRequirements.Time(1),kind:GraphNodeKind.Event,complete:new[]{
                GraphEffect.Native(new SetFlagAction("finished",true)),GraphEffect.Native(new UnlockProducerAction("worker")),
                GraphEffect.Native(new UnlockUpgradeAction("upgrade")),GraphEffect.Native(new SetModifierAction(modifier)),
                GraphEffect.Native(new ScheduleGrantResourceAction("gold",3,2))});
            var module=new GraphModule(new[]{definition});var b=new ModuleBuilder();b.Resource(new ResourceDefinition("gold"));b.Flag(new FlagDefinition("finished"));b.Producer(new ProducerDefinition("worker",null,new[]{new IncrementalFramework.Economy.ResourceAmount("gold",1)},initiallyUnlocked:false));b.Upgrade(new UpgradeDefinition("upgrade",null,null,initiallyUnlocked:false));b.Entity(Owner());b.Add(module);var sim=b.Create();Owner().Instantiate(sim.Entities,"player:1");Start(sim);Drain(sim,1);
            Assert.That(sim.GetFlag("finished"),Is.True);Assert.That(sim.GetProducer("worker").Unlocked,Is.True);Assert.That(sim.GetUpgrade("upgrade").Unlocked,Is.True);sim.Execute(new BuyProducerCommand("worker"));Drain(sim,3);Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(7)));
        }
        [Test]
        public void CompletionInOneInstanceImmediatelyWakesOtherInstances()
        {
            var trigger=GraphRequirements.Any(GraphRequirements.Resource("gold",20),GraphRequirements.All(Enabled(),GraphRequirements.Time(1)));
            var definition=new GraphDefinition("test",new[]{new GraphNodeDefinition("end",GraphNodeKind.Event,true,trigger:trigger)},onComplete:new[]{Reward(10)});
            var(sim,module)=Create(definition);Start(sim);sim.Entities.Find("player:2").GetValue<bool>("Enabled").Current=false;Start(sim,"other","player:2");Drain(sim,10);Assert.That(module.Runtime.Instance("other").Status,Is.EqualTo(GraphStatus.Completed));Assert.That(sim.GetResource("gold").Balance,Is.EqualTo(new BigDouble(30)));
        }
        [Test]
        public void MissingOwnerSuspendsSafelyAndCanBeSavedAndRestored()
        {
            var definition=Single(GraphRequirements.Time(10));var(sim,module)=Create(definition);Start(sim);sim.Entities.Remove("player:1");Drain(sim,1);Assert.That(module.Runtime.Instance("quest").Status,Is.EqualTo(GraphStatus.Suspended));
            var restored=new GraphModule(new[]{definition});var clone=SnapshotService.Restore(sim.InitialContent,SnapshotService.Capture(sim,Utc),modules:Builder(restored));Owner().Instantiate(clone.Entities,"player:1");Drain(clone,10);Assert.That(Node(restored,"end").Status,Is.EqualTo(GraphNodeStatus.Ready));
        }
#if UNITY_EDITOR
        [Test]
        public void UnityAssetMatchesCSharpDefinitionAndRejectsTreeCycles()
        {
            var asset=ScriptableObject.CreateInstance<GraphAsset>();
            try
            {
                asset.id="test";asset.title="test";asset.nodes=new[]{new GraphNodeEntry{id="end",isEnd=true,trigger=new GraphRequirementTree{root=0,entries=new[]{new GraphRequirementEntry{kind=GraphAssetRequirementKind.Signal,key="hit",count=2}}}}};
                Assert.That(asset.Compile().Fingerprint,Is.EqualTo(GraphCompiler.Compile(Single(GraphRequirements.Signal("hit",2))).Fingerprint));Assert.That(asset.Validate(),Is.Empty);
                asset.nodes[0].trigger.entries=new[]{new GraphRequirementEntry{kind=GraphAssetRequirementKind.Not,children=new[]{0}}};Assert.That(asset.Validate(),Is.Not.Empty);
            }
            finally{UnityEngine.Object.DestroyImmediate(asset);}
        }
#endif
    }
}
