using System;
using System.Collections;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.UI;
using IncrementalFramework.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace IdleHands.Tests
{
    public sealed class BoundUiTests
    {
        private GameObject canvas;private EntityContext world;private EntityInstance state;private UiContext context;
        [SetUp]public void Setup()
        {canvas=new GameObject("Bound UI test",typeof(RectTransform),typeof(Canvas));canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;world=new EntityContext();state=new EntityInstance("ui-test");world.Add(state);context=new UiContext(world,state);}
        [TearDown]public void Cleanup(){Object.DestroyImmediate(canvas);}
        [UnityTest]public IEnumerator DisplaysTrackStateWithoutEmittingUserInputEvents()
        {
            var flag=state.Define("flag",false);int requests=0;
            var d=Ui.Toggle(new TextTemplate("Repeat: {n:000}",TextTemplate.Bind("n",UiBinding<int>.Constant(7))),UiBinding<bool>.From(flag),new UiAction((_,v)=>{requests++;flag.Current=(bool)v;return UiActionResult.Success;}));
            using(var mounted=UnityUiFactory.Mount(canvas.transform,d,context))
            {
                var control=mounted.Root.GetComponentInChildren<Toggle>();flag.Current=true;
                Assert.That(control.isOn,Is.True);Assert.That(requests,Is.Zero);Assert.That(control.GetComponentInChildren<Text>().text,Is.EqualTo("Repeat: 007"));
                control.isOn=false;Assert.That(requests,Is.EqualTo(1));Assert.That(flag.Current,Is.False);
                mounted.Dispose();flag.Current=true;Assert.That(requests,Is.EqualTo(1));yield return null;
            }
        }
        [UnityTest]public IEnumerator SliderCommitsOnReleaseAndRevertsRejectedChanges()
        {
            int requests=0;var value=state.Define("value",.25d);
            var d=Ui.Slider(UiBinding<double>.From(value),new UiAction((_,v)=>{requests++;return UiActionResult.Rejected("Locked");}));
            using(var mounted=UnityUiFactory.Mount(canvas.transform,d,context))
            {
                var slider=mounted.Root.GetComponentInChildren<Slider>();var input=slider.GetComponent<UiSliderInput>();
                input.OnPointerDown(null);slider.value=.75f;Assert.That(requests,Is.Zero);
                input.OnPointerUp(null);Assert.That(requests,Is.EqualTo(1));Assert.That(slider.value,Is.EqualTo(.25f));
                value.Current=.5;Assert.That(slider.value,Is.EqualTo(.5f));Assert.That(requests,Is.EqualTo(1));yield return null;
            }
        }
        [UnityTest]public IEnumerator TimedVisibilityCanRevealHiddenViewsAndDisposalStopsBindings()
        {
            bool show=false;int reads=0;var d=Ui.Text("Now visible");d.Visible=UiBinding<bool>.From(_=>{reads++;return show;},.01);
            using(var mounted=UnityUiFactory.Mount(canvas.transform,d,context))
            {Assert.That(mounted.Root.gameObject.activeSelf,Is.False);show=true;yield return new WaitForSecondsRealtime(.04f);Assert.That(mounted.Root.gameObject.activeSelf,Is.True);mounted.Dispose();int previous=reads;yield return new WaitForSecondsRealtime(.03f);Assert.That(reads,Is.EqualTo(previous));}
        }
        [UnityTest]public IEnumerator LayoutUsesAvailableWidthRatherThanFirstFrameTextHeight()
        {
            var title=Ui.Text("ROSTER");title.Layout.MinHeight=42;title.Style.FontSize=25;
            var description=Ui.Text("Hire with gold. Select a destination party below, then assign adventurers. Four slots per party.");description.Layout.MinHeight=62;
            var button=Ui.Button("Hire Guardian / Tank / 60 gold",UiAction.Callback(()=>{}));button.Layout.MinHeight=82;
            var d=Ui.Column(title,description,button);d.Spacing=12;
            using(var mounted=UnityUiFactory.Mount(canvas.transform,d,context))
            {
                UnityUiView.Stretch(mounted.Root);Canvas.ForceUpdateCanvases();yield return null;Canvas.ForceUpdateCanvases();
                var first=((IUnityUiElement)mounted.Element.Children[0]).View.Root;
                var second=((IUnityUiElement)mounted.Element.Children[1]).View.Root;
                Assert.That(first.rect.height,Is.EqualTo(42).Within(2));Assert.That(second.rect.height,Is.LessThanOrEqualTo(70));
            }
        }
        [UnityTest]public IEnumerator ParentDisableAlsoDisablesScrollingAndInvalidSlidersStayFinite()
        {
            var flag=state.Define("enabled",false);var d=Ui.Scroll(Ui.Text("Scroll content"));d.Enabled=UiBinding<bool>.From(flag);
            using(var mounted=UnityUiFactory.Mount(canvas.transform,d,context))
            {
                var scroll=mounted.Root.GetComponentInChildren<ScrollRect>();Assert.That(scroll.enabled,Is.False);Assert.That(scroll.verticalScrollbar.interactable,Is.False);
                flag.Current=true;Assert.That(scroll.enabled,Is.True);Assert.That(scroll.verticalScrollbar.interactable,Is.True);yield return null;
            }
            var maximum=state.Define("maximum",1d);var sliderDefinition=Ui.Slider(.5d,UiAction.Callback(()=>{}));sliderDefinition.Maximum=UiBinding<double>.From(maximum);
            using(var mounted=UnityUiFactory.Mount(canvas.transform,sliderDefinition,context))
            {
                var slider=mounted.Root.GetComponentInChildren<Slider>();maximum.Current=0;
                Assert.That(slider.interactable,Is.False);Assert.That(float.IsNaN(slider.value),Is.False);maximum.Current=1;Assert.That(slider.interactable,Is.True);yield return null;
            }
        }
        [UnityTest]public IEnumerator LayoutHonorsGrowthMaximumAndAlignment()
        {
            var first=Ui.Button("First",UiAction.Callback(()=>{}));first.Layout.Width=UiLength.Fill();first.Layout.MaxWidth=100;first.Layout.MinHeight=40;
            var second=Ui.Button("Second",UiAction.Callback(()=>{}));second.Layout.Width=UiLength.Fill();second.Layout.MinHeight=40;
            var row=Ui.Row(first,second);row.Spacing=10;
            using(var mounted=UnityUiFactory.Mount(canvas.transform,row,context))
            {
                mounted.Root.sizeDelta=new Vector2(400,50);Canvas.ForceUpdateCanvases();yield return null;Canvas.ForceUpdateCanvases();
                Assert.That(((IUnityUiElement)mounted.Element.Children[0]).View.Root.rect.width,Is.EqualTo(100).Within(1));
                Assert.That(((IUnityUiElement)mounted.Element.Children[1]).View.Root.rect.width,Is.EqualTo(290).Within(1));
            }
            var child=Ui.Text("Centered");child.Layout.Width=UiLength.Pixels(80);child.Layout.Alignment=UiAlignment.Center;
            using(var mounted=UnityUiFactory.Mount(canvas.transform,Ui.Column(child),context))
            {
                mounted.Root.sizeDelta=new Vector2(400,50);Canvas.ForceUpdateCanvases();yield return null;Canvas.ForceUpdateCanvases();
                var rect=((IUnityUiElement)mounted.Element.Children[0]).View.Root;
                Assert.That(rect.rect.width,Is.EqualTo(80).Within(1));Assert.That(rect.anchoredPosition.x-rect.rect.width*rect.pivot.x,Is.EqualTo(160).Within(1));
            }
        }
        [UnityTest]public IEnumerator ListsReuseRowsAndRemountAgainstReplacementState()
        {
            var a=new EntityInstance("a");var b=new EntityInstance("b");world.Add(a);world.Add(b);
            var ids=state.DefineList<string>("ids",new[]{"a","b"});
            var d=Ui.List(UiBinding<System.Collections.Generic.IReadOnlyList<string>>.From(ids),c=>Ui.Text(c.Item.Id));
            using(var mounted=UnityUiFactory.Mount(canvas.transform,d,context))
            {
                var list=(UiEntityList)mounted.Element;var first=list.Rows["a"];ids.Current=new[]{"b","a"};
                Assert.That(list.Rows["a"],Is.SameAs(first));Assert.That(((IUnityUiElement)first).View.Root.GetSiblingIndex(),Is.EqualTo(1));
                world.Remove("a");Assert.That(first.IsDisposed,Is.True);yield return null;
            }
            var other=new EntityContext();var root=new EntityInstance("new");other.Add(root);var text=root.Define("name","Replacement");
            using(var mounted=UnityUiFactory.Mount(canvas.transform,Ui.Text(TextTemplate.Read(_=>text.Current)),new UiContext(other,root)))
            {Assert.That(mounted.Root.GetComponentInChildren<Text>().text,Is.EqualTo("Replacement"));yield return null;}
        }
    }
}
