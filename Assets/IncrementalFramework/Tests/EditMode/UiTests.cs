using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.UI;
using NUnit.Framework;

namespace IncrementalFramework.Tests
{
    public sealed class UiTests
    {
        private sealed class TextDisplay:UiText {public int Renders;protected override void OnRender(){Renders++;}}
        private sealed class ButtonDisplay:UiButton {protected override void OnRender(){}}
        private sealed class IconButtonDisplay:UiButton {protected override void OnRender(){}}
        private sealed class ToggleDisplay:UiToggle {protected override void OnRender(){}}
        private sealed class SliderDisplay:UiSlider {protected override void OnRender(){}}
        private sealed class ProgressDisplay:UiProgress {protected override void OnRender(){}}
        private sealed class StackDisplay:UiStack {protected override void OnRender(){}}
        private sealed class ConditionalDisplay:UiConditionalPanel {protected override void OnRender(){}}
        private sealed class ListDisplay:UiEntityList {protected override void OnRender(){}}
        private sealed class Factory:IUiElementFactory
        {
            public UiElement Create(UiDefinition d,UiContext c,UiElement p)
            {
                if(d is TextDefinition)return new TextDisplay();if(d is ButtonDefinition)return new ButtonDisplay();
                if(d is ToggleDefinition)return new ToggleDisplay();if(d is SliderDefinition)return new SliderDisplay();
                if(d is ProgressDefinition)return new ProgressDisplay();if(d is StackDefinition)return new StackDisplay();
                if(d is ConditionalDefinition)return new ConditionalDisplay();if(d is EntityListDefinition)return new ListDisplay();
                throw new ArgumentException();
            }
        }
        private EntityContext world;private EntityInstance entity;private UiContext context;private Factory factory;
        [SetUp]public void Setup(){world=new EntityContext();entity=new EntityInstance("root");world.Add(entity);context=new UiContext(world,entity);factory=new Factory();}
        private T Mount<T>(UiDefinition definition)where T:UiElement
        {var element=(T)factory.Create(definition,context,null);element.Mount(definition,context,factory);return element;}
        [TestCase("000",7,"007")][TestCase("0.00",7,"7.00")][TestCase("0.##",7.5,"7.5")]
        [TestCase("000.00",7.5,"007.50")][TestCase("#,##0.00",1234.5,"1,234.50")]
        [TestCase("0.00",9.999,"10.00")][TestCase("0.00",-.004,"0.00")][TestCase("#.##",.5,".5")]
        public void NamedTemplatesFormatOrdinaryAndBigNumbers(string format,double input,string expected)
        {
            foreach(IUiBinding value in new IUiBinding[]{UiBinding<double>.Constant(input),UiBinding<BigDouble>.Constant(new BigDouble(input))})
                Assert.That(new TextTemplate("{n:"+format+"}",new TextArgument("n",value)).Evaluate(context),Is.EqualTo(expected));
        }
        [TestCase("compact",12345,"12.3k")]
        [TestCase("compact",1234567,"1.2M")]
        [TestCase("compact",1234567890,"1.2B")]
        [TestCase("compact",1234567890000,"1.2T")]
        [TestCase("compact",12000,"12k")]
        [TestCase("compact:0.00",12000,"12.00k")]
        [TestCase("compact:0.##",12345,"12.35k")]
        [TestCase("compact:000.00",12345,"012.35k")]
        [TestCase("compact",999950,"1M")]
        [TestCase("compact:0.0",999950,"1.0M")]
        [TestCase("compact",999.99,"1k")]
        [TestCase("compact",-12345,"-12.3k")]
        [TestCase("compact",0,"0")]
        [TestCase("compact:0.00",0,"0.00")]
        [TestCase("compact",12.34,"12.3")]
        [TestCase("compact",-.004,"0")]
        public void CompactTemplatesFormatNumbersAndPromoteRoundedSuffixes(string format,double input,string expected)
        {
            foreach(IUiBinding value in new IUiBinding[]{UiBinding<double>.Constant(input),UiBinding<BigDouble>.Constant(new BigDouble(input))})
                Assert.That(new TextTemplate("{n:"+format+"}",new TextArgument("n",value)).Evaluate(context),Is.EqualTo(expected));
        }
        [Test]public void CompactTemplatesSupportIntegralDecimalAndExtremeBigNumbers()
        {
            foreach(IUiBinding value in new IUiBinding[]{UiBinding<int>.Constant(12345),UiBinding<long>.Constant(12345),UiBinding<decimal>.Constant(12345m)})
                Assert.That(new TextTemplate("{n:compact}",new TextArgument("n",value)).Evaluate(context),Is.EqualTo("12.3k"));
            var text=new TextTemplate("{n:compact:0.00}",TextTemplate.Bind("n",UiBinding<BigDouble>.Constant(new BigDouble(1.2345,400))));
            Assert.That(text.Evaluate(context),Is.EqualTo("12.35e399"));
        }
        [Test]public void CompactTemplatesRejectInvalidFormatsAndNonNumericBindings()
        {
            foreach(string format in new[]{"compact:","compactX","compact:F2"})
                Assert.Throws<FormatException>(()=>new TextTemplate("{n:"+format+"}",TextTemplate.Bind("n",UiBinding<int>.Constant(1))).Evaluate(context));
            Assert.Throws<FormatException>(()=>new TextTemplate("{n:compact}",TextTemplate.Bind("n",UiBinding<string>.Constant("12345"))).Evaluate(context));
        }
        [Test]public void CompactTextRefreshesWhenItsBoundValueChanges()
        {
            var amount=entity.Define("amount",12345d);
            using(var view=Mount<TextDisplay>(Ui.Text(new TextTemplate("Gold: {n:compact}",TextTemplate.Bind("n",UiBinding<double>.From(amount))))))
            {Assert.That(view.Text,Is.EqualTo("Gold: 12.3k"));amount.Current=2000000;Assert.That(view.Text,Is.EqualTo("Gold: 2M"));}
        }
        [Test]public void LargeCurrenciesKeepTheirExponentAndLeadingZeros()
        {
            var value=new BigDouble(1.25,400);
            string text=new TextTemplate("{n:0.00}",TextTemplate.Bind("n",UiBinding<BigDouble>.Constant(value))).Evaluate(context);
            Assert.That(text,Is.EqualTo("125"+new string('0',398)+".00"));
        }
        [Test]public void TextSyntaxAndMissingBindingsAreRejectedAndBracesEscape()
        {
            Assert.Throws<FormatException>(()=>new TextTemplate("{missing}"));Assert.Throws<FormatException>(()=>new TextTemplate("x}"));
            Assert.That(new TextTemplate("{{gold}} {value:0.00}",TextTemplate.Bind("value",UiBinding<double>.Constant(2))).Evaluate(context),Is.EqualTo("{gold} 2.00"));
            Assert.That(TextTemplate.Literal("{not a binding}").Evaluate(context),Is.EqualTo("{not a binding}"));
        }
        [Test]public void BindingDependenciesSwitchAndDisposalUnsubscribes()
        {
            var choose=entity.Define("choose",true);var a=entity.Define("a",1);var b=entity.Define("b",2);
            var d=Ui.Text(TextTemplate.Read(_=>(choose.Current?a.Current:b.Current).ToString()));
            var view=Mount<TextDisplay>(d);Assert.That(view.Text,Is.EqualTo("1"));
            choose.Current=false;Assert.That(view.Text,Is.EqualTo("2"));int rendered=view.Renders;
            a.Current=9;Assert.That(view.Renders,Is.EqualTo(rendered));b.Current=4;Assert.That(view.Text,Is.EqualTo("4"));
            view.Dispose();rendered=view.Renders;b.Current=5;Assert.That(view.Renders,Is.EqualTo(rendered));
        }
        [Test]public void ExpressionsAndCalculatedValuesDriveTextAndAppearance()
        {
            var a=entity.Define("a",2);var total=entity.Define("total",0);total.Calculate(e=>e.GetValue<int>("a").Current*2);
            var d=Ui.Text(new TextTemplate("{n:000}",TextTemplate.Bind("n",UiBinding<int>.From(Expr.Property(new PropertyKey<int>("total"))))));
            d.Style.FontSize=UiBinding<int>.From(_=>a.Current+10);
            using(var view=Mount<TextDisplay>(d)){Assert.That(view.Text,Is.EqualTo("004"));a.Current=3;Assert.That(view.Text,Is.EqualTo("006"));Assert.That(view.FontSize,Is.EqualTo(13));}
        }
        [Test]public void DifferentDisplaysShareActivationAndRecheckCustomAvailability()
        {
            bool available=true;int calls=0;var d=Ui.Button("Buy",UiAction.Callback(()=>calls++));d.CanExecute=UiBinding<bool>.From(_=>available);
            using(var first=Mount<ButtonDisplay>(d))using(var second=new IconButtonDisplay())
            {
                second.Mount(d,context,factory);Assert.That(first.Activate().Accepted,Is.True);Assert.That(second.Activate().Accepted,Is.True);
                available=false;Assert.That(second.Activate().Accepted,Is.False);Assert.That(calls,Is.EqualTo(2));
            }
        }
        [Test]public void InputsRequestActionsWithoutAssigningStateAndRejectionRestoresValue()
        {
            var flag=entity.Define("flag",false);int calls=0;
            var d=Ui.Toggle("Repeat",UiBinding<bool>.From(flag),new UiAction((_,input)=>{calls++;return UiActionResult.Rejected("Busy");}));
            using(var view=Mount<ToggleDisplay>(d)){Assert.That(view.RequestChange(true).Accepted,Is.False);Assert.That(flag.Current,Is.False);Assert.That(view.IsChecked,Is.False);Assert.That(calls,Is.EqualTo(1));}
        }
        [Test]public void SliderClampsSnapsAndReceivesAuthoritativeResult()
        {
            var value=entity.Define("value",2d);var d=Ui.Slider(UiBinding<double>.From(value),new UiAction((_,input)=>{value.Current=(double)input;return UiActionResult.Success;}));
            d.Minimum=0d;d.Maximum=10d;d.Step=2d;
            using(var view=Mount<SliderDisplay>(d)){view.RequestChange(7.1);Assert.That(value.Current,Is.EqualTo(8));view.RequestChange(100);Assert.That(view.Value,Is.EqualTo(10));}
        }
        [Test]public void ProgressClampsAndReportsInvalidRanges()
        {
            var value=entity.Define("value",10d);var d=Ui.Progress(UiBinding<double>.From(value));
            using(var view=Mount<ProgressDisplay>(d)){Assert.That(view.Fraction,Is.EqualTo(1));value.Current=-1;Assert.That(view.Fraction,Is.Zero);d.Maximum=0d;view.Refresh();Assert.That(view.Fraction,Is.Zero);}
        }
        [Test]public void ConditionalPanelsRetainChildrenAndPreventHiddenActions()
        {
            var flag=entity.Define("flag",true);int calls=0;
            using(var view=Mount<ConditionalDisplay>(Ui.When(UiBinding<bool>.From(flag),Ui.Button("Go",UiAction.Callback(()=>calls++)),Ui.Text("No"))))
            {var child=(UiButton)view.Content;flag.Current=false;Assert.That(view.Content,Is.SameAs(child));Assert.That(child.Activate().Accepted,Is.False);flag.Current=true;Assert.That(child.Activate().Accepted,Is.True);Assert.That(calls,Is.EqualTo(1));}
        }
        [Test]public void ParentDisablePropagatesAndDuplicateIdsAreRejected()
        {
            var flag=entity.Define("flag",false);var d=Ui.Column(Ui.Button("Go",UiAction.Callback(()=>{})));d.Enabled=UiBinding<bool>.From(flag);
            using(var view=Mount<StackDisplay>(d)){Assert.That(((UiButton)view.Children[0]).Activate().Accepted,Is.False);flag.Current=true;Assert.That(((UiButton)view.Children[0]).Activate().Accepted,Is.True);}
            var a=Ui.Text("A");a.Id="same";var b=Ui.Text("B");b.Id="same";Assert.Throws<ArgumentException>(()=>Mount<StackDisplay>(Ui.Column(a,b)));
        }
        [Test]public void EntityListsReconcileByIdentityAndIgnoreRemovedEntities()
        {
            var a=new EntityInstance("a");var b=new EntityInstance("b");world.Add(a);world.Add(b);
            var ids=entity.DefineList<string>("ids",new[]{"a","b"});
            var d=Ui.List(UiBinding<IReadOnlyList<string>>.From(ids),c=>{var text=Ui.Text(c.Item.Id);text.Id="name";return text;});d.EmptyContent=Ui.Text("Empty");
            using(var view=Mount<ListDisplay>(d))
            {
                var first=view.Rows["a"];ids.Current=new[]{"b","a"};Assert.That(view.Rows["a"],Is.SameAs(first));Assert.That(view.Children[1],Is.SameAs(first));
                world.Remove("a");Assert.That(first.IsDisposed,Is.True);Assert.That(view.Rows.Keys,Is.EquivalentTo(new[]{"b"}));world.Remove("b");Assert.That(view.IsEmpty,Is.True);
            }
        }
        [Test]public void QueryListsObserveEntityAdditionAndLocalStyleOverrides()
        {
            var d=Ui.List(Ui.Query(TargetQuery.All().OfKind("hero")),c=>Ui.Text(c.Item.Id));
            using(var view=Mount<ListDisplay>(d))
            {Assert.That(view.IsEmpty,Is.True);world.Add(new EntityInstance("new-hero",kind:"hero"));Assert.That(view.Rows.ContainsKey("new-hero"),Is.True);}
            var style=new UiStyle{FontSize=30};var text=Ui.Text("Shared style");text.Style=style;text.StyleOverrides.FontSize=12;
            using(var view=Mount<TextDisplay>(text)){Assert.That(view.FontSize,Is.EqualTo(12));Assert.That(style.FontSize.Read(context),Is.EqualTo(30));}
        }
        [Test]public void ActivationCannotDispatchAfterItsRowIsRemovedDuringAvailabilityRefresh()
        {
            var hero=new EntityInstance("hero");world.Add(hero);bool included=true;int calls=0;
            var d=Ui.List(UiBinding<IReadOnlyList<string>>.From(_=>included?new[]{"hero"}:Array.Empty<string>()),c=>Ui.Button("Go",UiAction.Callback(()=>calls++)));
            using(var list=Mount<ListDisplay>(d))
            {var button=(UiButton)list.Rows["hero"];included=false;Assert.That(button.Activate().Accepted,Is.False);Assert.That(button.IsDisposed,Is.True);Assert.That(calls,Is.Zero);}
        }
        [Test]public void InvalidSliderBoundsRejectMountAndBindingErrorsDisableInteraction()
        {
            var slider=Ui.Slider(0d,UiAction.Callback(()=>{}));slider.Maximum=0d;
            Assert.Throws<ArgumentException>(()=>Mount<SliderDisplay>(slider));
            var button=Ui.Button("Broken",UiAction.Callback(()=>Assert.Fail("Must not execute")));
            button.CanExecute=UiBinding<bool>.From(_=>throw new InvalidOperationException("Broken binding"));
            using(var view=Mount<ButtonDisplay>(button)){Assert.That(view.Faulted,Is.True);Assert.That(view.Activate().Accepted,Is.False);}
        }
        [Test]public void TimedBindingsTickOnlyWhenDueAndStateReplacementIsIndependent()
        {
            double time=0;context=new UiContext(world,entity,()=>time);
            using(var view=Mount<TextDisplay>(Ui.Text(TextTemplate.Read(c=>c.Clock().ToString(),.5))))
            {time=1;view.Tick(0);Assert.That(view.Text,Is.EqualTo("1"));time=2;view.Tick(.1);Assert.That(view.Text,Is.EqualTo("1"));view.Tick(.5);Assert.That(view.Text,Is.EqualTo("2"));}
        }
    }
}
