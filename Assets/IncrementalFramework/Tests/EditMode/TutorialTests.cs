using System;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;
using IncrementalFramework.Persistence;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Graphs;
using IncrementalFramework.Tutorials;
using IncrementalFramework.UI;
using NUnit.Framework;
#if UNITY_EDITOR
using IncrementalFramework.Unity;
using UnityEngine;
#endif

namespace IdleHands.Tests
{
    public sealed class TutorialTests
    {
        private sealed class ButtonDisplay:UiButton {public int Renders;protected override void OnRender(){Renders++;}}
        private sealed class StackDisplay:UiStack {protected override void OnRender(){}}
        private sealed class Factory:IUiElementFactory
        {
            public UiElement Create(UiDefinition d,UiContext context,UiElement parent)=>d is ButtonDefinition?(UiElement)new ButtonDisplay():new StackDisplay();
        }
        private static readonly DateTime Utc=new DateTime(2026,10,31,0,0,0,DateTimeKind.Utc);
        private static GraphDefinition Graph(bool maintained=false)=>new GraphDefinition("intro",new[]{
            new GraphNodeDefinition("click",GraphNodeKind.Event,trigger:GraphRequirements.Signal("clicked")),
            new GraphNodeDefinition("buy",GraphNodeKind.Event,isEnd:true,prerequisites:new[]{"click"},trigger:GraphRequirements.Signal("bought"))},
            activation:maintained?GraphRequirements.State("enabled",Expr.Property(new PropertyKey<bool>("Enabled"))):null,
            activationMode:maintained?GraphActivationMode.Maintained:GraphActivationMode.Latched);
        private static TutorialDefinition Tutorial()=>new TutorialDefinition("intro",new[]{
            new TutorialStep("click",new[]{new TutorialUiRule("click",enabled:true,highlighted:true,highlightWidth:6)},instructions:"Click to begin"),
            new TutorialStep("buy",new[]{new TutorialUiRule("buy",enabled:true,highlighted:true)},instructions:"Buy a worker")},
            new[]{new TutorialUiRule("click",enabled:false),new TutorialUiRule("buy",enabled:false)});
        private static EntityTemplate Owner(){var t=new EntityTemplate("player");t.Define("Enabled",true);return t;}
        private static ModuleBuilder Builder(GraphModule module){var b=new ModuleBuilder();b.Resource(new ResourceDefinition("gold",10));b.Entity(Owner());b.Add(module);return b;}
        private static (Simulation,GraphModule) Create(GraphDefinition graph=null)
        {
            var module=new GraphModule(new[]{graph??Graph()});var sim=Builder(module).Create();Owner().Instantiate(sim.Entities,"player:1");Owner().Instantiate(sim.Entities,"player:2");return(sim,module);
        }
        private static void Drain(Simulation sim){double target=sim.Time;int steps=0;while(!sim.AdvanceTo(target).Completed)Assert.That(++steps,Is.LessThan(1000));}
        private static void Start(Simulation sim,string instance="intro:1",string owner="player:1"){Assert.That(sim.Execute(new StartGraphCommand(instance,"intro",owner)).Success,Is.True);Drain(sim);}
        private static void Signal(Simulation sim,string key){Assert.That(sim.Execute(new PublishGraphSignalCommand("player:1",key)).Success,Is.True);Drain(sim);}
        private static UiElement Mount(UiDefinition definition,EntityContext world,TutorialController tutorial)
        {var factory=new Factory();var context=new UiContext(world,presentation:tutorial);var element=factory.Create(definition,context,null);element.Mount(definition,context,factory);return element;}
        private static ButtonDefinition Button(string target,Action action=null){var b=Ui.Button(target,UiAction.Callback(action??(()=>{})));b.Id=target;return b;}

        [Test]
        public void ActiveStepsEnableAndHighlightTargetsThenRestoreOriginalUi()
        {
            var(sim,module)=Create();using var tutorial=new TutorialController(module.Runtime,"player:1",new[]{Tutorial()});Start(sim);
            var click=Button("click");var buy=Button("buy");using var root=Mount(Ui.Column(click,buy),sim.Entities,tutorial);
            var a=(UiButton)root.Children[0];var b=(UiButton)root.Children[1];
            Assert.That(a.Enabled,Is.True);Assert.That(a.Highlighted,Is.True);Assert.That(a.HighlightWidth,Is.EqualTo(6));Assert.That(b.Enabled,Is.False);Assert.That(tutorial.ActiveSteps.Single().Instructions,Is.EqualTo("Click to begin"));
            Signal(sim,"clicked");Assert.That(a.Enabled,Is.False);Assert.That(a.Highlighted,Is.False);Assert.That(b.Enabled,Is.True);Assert.That(b.Highlighted,Is.True);
            Signal(sim,"bought");Assert.That(a.Enabled,Is.True);Assert.That(b.Enabled,Is.True);Assert.That(b.Highlighted,Is.False);Assert.That(tutorial.ActiveSteps,Is.Empty);
            Assert.That(click.Enabled.Read(new UiContext(sim.Entities)),Is.True);Assert.That(click.Highlighted.Read(new UiContext(sim.Entities)),Is.False);
        }
        [Test]
        public void TutorialNeverBypassesGameplayOrParentEligibility()
        {
            var(sim,module)=Create();Start(sim);using var tutorial=new TutorialController(module.Runtime,"player:1",new[]{Tutorial()});
            int requests=0;var d=Button("click",()=>requests++);d.Enabled=false;using var root=Mount(Ui.Column(d),sim.Entities,tutorial);var button=(UiButton)root.Children[0];Assert.That(button.Activate().Accepted,Is.False);
            d.Enabled=true;d.CanExecute=false;Assert.That(button.Activate().Accepted,Is.False);d.CanExecute=true;root.Definition.Enabled=false;Assert.That(button.Activate().Accepted,Is.False);Assert.That(requests,Is.Zero);
            root.Definition.Enabled=true;Assert.That(button.Activate().Accepted,Is.True);Assert.That(requests,Is.EqualTo(1));
        }
        [Test]
        public void SuspendedTutorialReleasesPresentationAndResumesSameStep()
        {
            var(sim,module)=Create(Graph(true));Start(sim);using var tutorial=new TutorialController(module.Runtime,"player:1",new[]{Tutorial()});using var root=Mount(Ui.Column(Button("click"),Button("buy")),sim.Entities,tutorial);
            var enabled=sim.Entities.Find("player:1").GetValue<bool>("Enabled");enabled.Current=false;Drain(sim);Assert.That(tutorial.ActiveSteps,Is.Empty);Assert.That(root.Children[1].Enabled,Is.True);Assert.That(root.Children[0].Highlighted,Is.False);
            enabled.Current=true;Drain(sim);Assert.That(root.Children[1].Enabled,Is.False);Assert.That(root.Children[0].Highlighted,Is.True);
        }
        [Test]
        public void DisposingControllerRestoresUiAndDisposingUiUnsubscribes()
        {
            var(sim,module)=Create();Start(sim);var tutorial=new TutorialController(module.Runtime,"player:1",new[]{Tutorial()});var root=Mount(Ui.Column(Button("click"),Button("buy")),sim.Entities,tutorial);
            var click=(ButtonDisplay)root.Children[0];tutorial.Dispose();Assert.That(click.Highlighted,Is.False);Assert.That(root.Children[1].Enabled,Is.True);Assert.That(tutorial.Resolve("buy").Enabled,Is.Null);
            tutorial.Dispose();root.Dispose();int renders=click.Renders;Signal(sim,"clicked");Assert.That(click.Renders,Is.EqualTo(renders));
            using var next=new TutorialController(module.Runtime,"player:1",new[]{Tutorial()});var mounted=(ButtonDisplay)Mount(Button("buy"),sim.Entities,next);mounted.Dispose();renders=mounted.Renders;Signal(sim,"bought");Assert.That(mounted.Renders,Is.EqualTo(renders));
        }
        [Test]
        public void SaveLoadDerivesCurrentStepWithoutSavingUiObjects()
        {
            var(sim,module)=Create();Start(sim);Signal(sim,"clicked");var snapshot=SnapshotService.Deserialize(SnapshotService.Serialize(SnapshotService.Capture(sim,Utc)));
            Assert.That(snapshot.Sections.Count,Is.EqualTo(1));var restored=new GraphModule(new[]{Graph()});var clone=SnapshotService.Restore(sim.InitialContent,snapshot,modules:Builder(restored));
            using var tutorial=new TutorialController(restored.Runtime,"player:1",new[]{Tutorial()});using var root=Mount(Ui.Column(Button("click"),Button("buy")),clone.Entities,tutorial);
            Assert.That(root.Children[0].Enabled,Is.False);Assert.That(root.Children[1].Highlighted,Is.True);Assert.That(tutorial.ActiveSteps.Single().NodeId,Is.EqualTo("buy"));
        }
        [Test]
        public void OwnersAreIsolatedAndMissingTargetsCanMountLater()
        {
            var(sim,module)=Create();Start(sim,"other","player:2");using var tutorial=new TutorialController(module.Runtime,"player:1",new[]{Tutorial()});Assert.That(tutorial.Resolve("click").Highlighted,Is.Null);
            Start(sim);using var target=Mount(Button("click"),sim.Entities,tutorial);Assert.That(target.Highlighted,Is.True);using var unmatched=Mount(Button("unrelated"),sim.Entities,tutorial);Assert.That(unmatched.Enabled,Is.True);Assert.That(unmatched.Highlighted,Is.False);
        }
        [Test]
        public void ParallelStepsMergeConservativelyWithDeterministicHighlightStyle()
        {
            var graph=new GraphDefinition("intro",new[]{new GraphNodeDefinition("a"),new GraphNodeDefinition("b"),new GraphNodeDefinition("end",isEnd:true,prerequisites:new[]{"a","b"})});var(sim,module)=Create(graph);Start(sim);
            var d=new TutorialDefinition("intro",new[]{new TutorialStep("a",new[]{new TutorialUiRule("target",enabled:true,highlighted:true,highlightWidth:5)},priority:10),new TutorialStep("b",new[]{new TutorialUiRule("target",enabled:false,highlighted:true,highlightWidth:2)},priority:1)});
            using var tutorial=new TutorialController(module.Runtime,"player:1",new[]{d});Assert.That(tutorial.Resolve("target").Enabled,Is.False);Assert.That(tutorial.Resolve("target").HighlightWidth,Is.EqualTo(5));
            sim.Execute(new CompleteGraphNodeCommand("intro:1","b",1));Drain(sim);Assert.That(tutorial.Resolve("target").Enabled,Is.True);
        }
        [Test]
        public void VisibilityAndAuthoredHighlightStyleRestoreWhenStepEnds()
        {
            var(sim,module)=Create();Start(sim);var d=new TutorialDefinition("intro",new[]{new TutorialStep("click",new[]{new TutorialUiRule("target",visible:false,highlighted:false)})});using var tutorial=new TutorialController(module.Runtime,"player:1",new[]{d});
            var button=Button("target");button.Highlighted=true;button.HighlightWidth=8;using var root=Mount(button,sim.Entities,tutorial);Assert.That(root.Visible,Is.False);Assert.That(root.Highlighted,Is.False);Signal(sim,"clicked");Assert.That(root.Visible,Is.True);Assert.That(root.Highlighted,Is.True);Assert.That(root.HighlightWidth,Is.EqualTo(8));
        }
        [Test]
        public void SemanticTargetsAreIndependentOfInstanceIdsAndFlowIntoItemContexts()
        {
            var(sim,module)=Create();Start(sim);using var tutorial=new TutorialController(module.Runtime,"player:1",new[]{Tutorial()});var button=Button("row-17");button.TargetId="click";using var root=Mount(button,sim.Entities,tutorial);Assert.That(root.Highlighted,Is.True);
            var context=new UiContext(sim.Entities,presentation:tutorial).ForItem(sim.Entities.Find("player:1"));Assert.That(context.Presentation,Is.SameAs(tutorial));
        }
        [Test]
        public void DefinitionsRejectInvalidTargetsMissingNodesAndInvalidAppearance()
        {
            Assert.Throws<ArgumentException>(()=>new TutorialUiRule(""));Assert.Throws<ArgumentOutOfRangeException>(()=>new TutorialUiRule("x",highlightWidth:float.NaN));
            Assert.Throws<ArgumentException>(()=>new TutorialStep("step",new[]{new TutorialUiRule("x"),new TutorialUiRule("x")}));
            var(sim,module)=Create();Assert.Throws<ArgumentException>(()=>new TutorialController(module.Runtime,"player:1",new[]{new TutorialDefinition("intro",new[]{new TutorialStep("missing")})}));
            Assert.Throws<ArgumentException>(()=>new TutorialController(module.Runtime,"player:1",new[]{new TutorialDefinition("missing",new[]{new TutorialStep("missing")})}));
        }
#if UNITY_EDITOR
        [Test]
        public void UnityTutorialAssetCompilesAndValidatesGraphReferences()
        {
            var graph=ScriptableObject.CreateInstance<GraphAsset>();var asset=ScriptableObject.CreateInstance<TutorialAsset>();
            try
            {
                graph.id="intro";graph.nodes=new[]{new GraphNodeEntry{id="end",isEnd=true}};asset.graph=graph;asset.steps=new[]{new TutorialStepEntry{nodeId="end",rules=new[]{new TutorialUiRuleEntry{targetId="buy",enabled=TutorialToggle.Off,highlighted=TutorialToggle.On}}}};
                Assert.That(asset.Validate(),Is.Empty);var rule=asset.Definition().Steps.Single().Rules.Single();Assert.That(rule.Enabled,Is.False);Assert.That(rule.Highlighted,Is.True);
                asset.steps[0].nodeId="missing";Assert.That(asset.Validate(),Is.Not.Empty);
            }
            finally{UnityEngine.Object.DestroyImmediate(asset);UnityEngine.Object.DestroyImmediate(graph);}
        }
#endif
    }
}
