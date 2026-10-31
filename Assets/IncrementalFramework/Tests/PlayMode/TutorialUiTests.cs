using System;
using System.Collections;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Persistence;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Graphs;
using IncrementalFramework.Tutorials;
using IncrementalFramework.UI;
using IncrementalFramework.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace IdleHands.Tests
{
    public sealed class TutorialUiTests
    {
        [UnityTest]
        public IEnumerator HighlightIsNonBlockingPreservesBorderAndClearsAfterCompletion()
        {
            var canvas=new GameObject("Tutorial UI",typeof(RectTransform),typeof(Canvas));canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var graph=new GraphDefinition("intro",new[]{new GraphNodeDefinition("end",GraphNodeKind.Event,true,trigger:GraphRequirements.Signal("clicked"))});
            var module=new GraphModule(new[]{graph});var sim=new ModuleBuilder().Add(module).Create();new EntityTemplate("owner").Instantiate(sim.Entities,"owner:1");sim.Execute(new StartGraphCommand("intro:1","intro","owner:1"));while(!sim.AdvanceTo(0).Completed){}
            using var tutorial=new TutorialController(module.Runtime,"owner:1",new[]{new TutorialDefinition("intro",new[]{new TutorialStep("end",new[]{new TutorialUiRule("click",highlighted:true,highlightWidth:5),new TutorialUiRule("blocked",enabled:false)})})});
            int rejectedRequests=0,accepted=0;var button=Ui.Button("Continue",UiAction.Callback(()=>{accepted++;sim.Execute(new PublishGraphSignalCommand("owner:1","clicked"));while(!sim.AdvanceTo(0).Completed){}}));button.Id="click";button.Style.BorderWidth=2;button.Style.BorderColor=new UiColor(0,0,1);button.Layout.MinHeight=60;
            var blocked=Ui.Button("Blocked",UiAction.Callback(()=>rejectedRequests++));blocked.Id="blocked";
            try
            {
                using var mounted=UnityUiFactory.Mount(canvas.transform,Ui.Column(button,blocked),new UiContext(sim.Entities,presentation:tutorial));
                Canvas.ForceUpdateCanvases();yield return null;
                var first=((IUnityUiElement)mounted.Element.Children[0]).View;var second=((IUnityUiElement)mounted.Element.Children[1]).View;
                var highlight=first.Surface.Find("UiHighlight");Assert.That(highlight.gameObject.activeSelf,Is.True);Assert.That(highlight.GetComponentsInChildren<Image>().Length,Is.EqualTo(4));Assert.That(highlight.GetComponentsInChildren<Image>().All(i=>!i.raycastTarget),Is.True);
                Assert.That(first.Surface.GetComponent<Outline>().effectDistance.x,Is.EqualTo(2));Assert.That(second.Surface.GetComponent<Button>().interactable,Is.False);second.Surface.GetComponent<Button>().onClick.Invoke();Assert.That(rejectedRequests,Is.Zero);
                first.Surface.GetComponent<Button>().onClick.Invoke();Assert.That(accepted,Is.EqualTo(1));Assert.That(highlight.gameObject.activeSelf,Is.False);Assert.That(second.Surface.GetComponent<Button>().interactable,Is.True);Assert.That(first.Surface.GetComponent<Outline>().enabled,Is.True);Assert.That(first.Surface.GetComponent<Outline>().effectDistance.x,Is.EqualTo(2));
            }
            finally{Object.DestroyImmediate(canvas);}
        }
    }
}
