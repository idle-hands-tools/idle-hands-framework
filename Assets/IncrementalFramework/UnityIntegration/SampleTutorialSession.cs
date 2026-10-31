using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Persistence;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Graphs;
using IncrementalFramework.Tutorials;
using IncrementalFramework.UI;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    /// <summary>Sample onboarding uses its own saved graph, leaving existing gameplay save schemas intact.</summary>
    public sealed class SampleTutorialSession : IUiPresentationPolicy, IDisposable
    {
        public sealed class Stage
        {
            public string Key { get; } public string Target { get; } public string Title { get; } public string Instructions { get; }
            public IReadOnlyList<string> Actions { get; }
            public Stage(string key,string target,string title,string instructions,params string[] actions)
            {Key=key;Target=target;Title=title;Instructions=instructions;Actions=Array.AsReadOnly(actions.Concat(new[]{key}).Distinct().ToArray());}
        }
        private const string InstanceId="onboarding";
        private const string OwnerId="tutorial-player";
        private readonly string key;
        private readonly Stage[] stages;
        private Simulation simulation;
        private GraphModule module;
        private TutorialController controller;
        public event Action Changed;
        public bool Completed=>module.Runtime.Instance(InstanceId).Status==GraphStatus.Completed;
        public string CurrentStage=>controller==null||controller.ActiveSteps.Count==0?null:stages[int.Parse(controller.ActiveSteps[0].NodeId.Substring(5))].Key;
        public string Caption
        {
            get
            {
                if(Completed)return "Tutorial complete · Explore freely.";
                var step=controller?.ActiveSteps.FirstOrDefault();
                return step==null?"Preparing tutorial…":"TUTORIAL "+(int.Parse(step.NodeId.Substring(5))+1)+"/"+stages.Length+" · "+step.Title+"\n"+step.Instructions;
            }
        }
        public SampleTutorialSession(string saveIdentity,IEnumerable<Stage> stages)
        {key=saveIdentity+".tutorial.v1";this.stages=stages.ToArray();if(this.stages.Length==0)throw new ArgumentException("Tutorial needs stages.");Reset(null);}
        private GraphDefinition Graph()
        {
            var nodes=stages.Select((stage,i)=>new GraphNodeDefinition("step-"+i,GraphNodeKind.Event,i==stages.Length-1,
                prerequisites:i==0?null:new[]{"step-"+(i-1)},trigger:GraphRequirements.Signal("stage:"+stage.Key),title:stage.Title,description:stage.Instructions)).ToList();
            nodes.Add(new GraphNodeDefinition("!skip",GraphNodeKind.Event,true,activation:GraphRequirements.State("skip-requested",Expr.Property(new PropertyKey<bool>("SkipRequested")))));
            return new GraphDefinition("sample-onboarding",nodes);
        }
        private ModuleBuilder Builder(GraphModule graph)
        {var b=new ModuleBuilder();var t=new EntityTemplate("tutorial-player");t.Define("SkipRequested",false);b.Entity(t);b.Add(graph);return b;}
        private void Reset(string json)
        {
            var nextModule=new GraphModule(new[]{Graph()});var b=Builder(nextModule);Simulation next;
            if(json==null)
            {
                next=b.Create();var owner=new EntityTemplate("tutorial-player");owner.Define("SkipRequested",false);owner.Instantiate(next.Entities,OwnerId);
                next.Execute(new StartGraphCommand(InstanceId,"sample-onboarding",OwnerId));
            }
            else next=SnapshotService.Restore(b.Compile(),SnapshotService.Deserialize(json),modules:b);
            var savedInstance=nextModule.Runtime.Instances.SingleOrDefault();
            if(savedInstance==null||savedInstance.Id!=InstanceId||savedInstance.OwnerId!=OwnerId||savedInstance.DefinitionId!="sample-onboarding")throw new ArgumentException("Invalid sample tutorial instance.");
            double target=next.PendingTarget??next.Time;int work=0;while(!next.AdvanceTo(target).Completed)if(++work>1000)throw new InvalidOperationException("Tutorial did not settle.");
            var definition=new TutorialDefinition("sample-onboarding",stages.Select((s,i)=>new TutorialStep("step-"+i,
                (s.Target=="game-action:quest"?new[]{new TutorialUiRule(s.Target,enabled:true,highlighted:true),new TutorialUiRule("nav:QUESTS",highlighted:true)}:new[]{new TutorialUiRule(s.Target,enabled:true,highlighted:true)}),s.Title,s.Instructions)),
                new[]{new TutorialUiRule("prestige",enabled:false),new TutorialUiRule("game-action:replay",enabled:false)});
            var nextController=new TutorialController(nextModule.Runtime,OwnerId,new[]{definition});
            if(controller!=null){controller.Changed-=Notify;controller.Dispose();}
            simulation=next;module=nextModule;controller=nextController;controller.Changed+=Notify;Notify();
        }
        public UiPresentation Resolve(string targetId)=>controller?.Resolve(targetId)??default;
        public void RecordAction(string action)
        {
            if(Completed)return;var current=CurrentStage;if(current==null)return;
            var stage=stages.Single(s=>s.Key==current);if(!stage.Actions.Contains(action))return;
            var result=simulation.Execute(new PublishGraphSignalCommand(OwnerId,"stage:"+stage.Key));
            if(result.Success){double target=simulation.Time;while(!simulation.AdvanceTo(target).Completed){}}
        }
        public void Poll(Func<string,bool> completed)
        {for(int i=0;i<stages.Length&&!Completed;i++){var stage=CurrentStage;if(stage==null||!completed(stage))break;RecordAction(stage);}}
        public void Skip()
        {simulation.Entities.Find(OwnerId).GetValue<bool>("SkipRequested").Current=true;double target=simulation.Time;while(!simulation.AdvanceTo(target).Completed){}}
        public string Capture()=>SnapshotService.Serialize(SnapshotService.Capture(simulation,DateTime.UtcNow));
        public void Save(){PlayerPrefs.SetString(key,Capture());PlayerPrefs.Save();}
        public void Load()
        {
            try{Reset(PlayerPrefs.HasKey(key)?PlayerPrefs.GetString(key):null);}
            catch(Exception error){Debug.LogWarning("Tutorial progress reset: "+error.Message);Reset(null);}
        }
        private void Notify(){Changed?.Invoke();}
        public void Dispose(){if(controller!=null){controller.Changed-=Notify;controller.Dispose();controller=null;}Notify();Changed=null;}
        public static Stage[] Expanded(string sample)
        {
            if(sample=="OrbitalFoundry")return new[]{new Stage("priority","game-action:priority","Allocate work","Raise a machine's priority to decide which batches receive scarce inputs first."),new Stage("recipe","game-action:recipe","Choose a recipe","Select a recipe. Changes affect the next batch, not inputs already consumed."),new Stage("deliver","game-action:deliver","Deliver a section","Let batches finish, then deliver an affordable spacecraft section. Scroll down to SPACECRAFT.")};
            if(sample=="LastlightRailway")return new[]{new Stage("track","game-action:track","Connect stations","Open an affordable track so trains can reach new stations."),new Stage("route","game-action:route","Apply an itinerary","Edit a train's stops and loading limits, then apply its itinerary."),new Stage("contract","game-action:contract","Complete a delivery","Let cargo reach the requested station, then fulfill a delivery contract.")};
            if(sample=="MarshlightReserve")return new[]{new Stage("restore","game-action:restore","Restore habitat","Use free baseline restoration to begin rebuilding a habitat."),new Stage("water","game-action:water","Manage water","Raise habitat water, watching the healthy 0.40–0.85 band."),new Stage("filter","game-action:filter","Improve purity","Filter the water, then monitor population changes before introducing predators.")};
            if(sample=="TideboundExchange")return new[]{new Stage("buy","game-action:buy","Buy cargo","Buy an affordable commodity at your current port. Check the total price and hold capacity."),new Stage("travel","game-action:travel","Sail to market","Choose another port; cargo stays aboard while you travel."),new Stage("sell","game-action:sell","Sell your cargo","After arriving, sell the commodity you bought. Compare proceeds with costs.")};
            return new[]{new Stage("read","game-action:story","Read and respond","Read the passage and continue or select an available response. Locked options explain their requirements.","continue","choose"),new Stage("consequence","game-action:story","Follow the consequences","Read the next passage and respond again. The journal records evidence and earlier decisions.","continue","choose")};
        }
    }
}
