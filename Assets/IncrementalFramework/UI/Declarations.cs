using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.UI
{
    public enum UiDirection { Horizontal, Vertical }
    public enum UiAlignment { Start, Center, End, Stretch }
    public enum UiOverflow { Clip, Overflow }
    public enum UiFontStyle { Normal, Bold, Italic, BoldItalic }
    public enum UiCommitMode { OnRelease, Continuous }
    public enum UiScrollbarVisibility { Auto, Always, Hidden }
    public enum UiLengthKind { Auto, Pixels, Fill }
    public readonly struct UiLength
    {
        public UiLengthKind Kind { get; }
        public float Amount { get; }
        private UiLength(UiLengthKind kind,float amount){Kind=kind;Amount=amount;}
        public static UiLength Auto => new UiLength(UiLengthKind.Auto,0);
        public static UiLength Pixels(float value)=>new UiLength(UiLengthKind.Pixels,value);
        public static UiLength Fill(float weight=1)=>new UiLength(UiLengthKind.Fill,weight);
    }
    public readonly struct UiInsets
    {
        public float Left {get;} public float Right {get;} public float Top {get;} public float Bottom {get;}
        public UiInsets(float all):this(all,all,all,all){}
        public UiInsets(float left,float right,float top,float bottom){Left=left;Right=right;Top=top;Bottom=bottom;}
    }
    public readonly struct UiColor
    {
        public float R {get;} public float G {get;} public float B {get;} public float A {get;}
        public UiColor(float r,float g,float b,float a=1){R=r;G=g;B=b;A=a;}
        public static UiColor White=>new UiColor(1,1,1);
        public static UiColor Clear=>new UiColor(0,0,0,0);
    }
    public sealed class UiLayout
    {
        public UiLength Width {get;set;}=UiLength.Auto;
        public UiLength Height {get;set;}=UiLength.Auto;
        public float MinWidth {get;set;} public float MinHeight {get;set;}
        public float MaxWidth {get;set;}=float.PositiveInfinity;
        public float MaxHeight {get;set;}=float.PositiveInfinity;
        public UiInsets Margin {get;set;}
        public UiAlignment Alignment {get;set;}=UiAlignment.Stretch;
    }
    public sealed class UiStyle
    {
        public UiBinding<UiColor> Color {get;set;}=UiColor.White;
        public UiBinding<UiColor> Background {get;set;}=new UiColor(.10f,.16f,.20f);
        public UiBinding<UiColor> BorderColor {get;set;}=UiColor.Clear;
        public UiBinding<float> BorderWidth {get;set;}=0f;
        public UiBinding<string> Font {get;set;}="LegacyRuntime.ttf";
        public UiBinding<int> FontSize {get;set;}=16;
        public UiBinding<UiFontStyle> FontStyle {get;set;}=UiFontStyle.Normal;
        public UiBinding<UiAlignment> TextAlignment {get;set;}=UiAlignment.Start;
        public UiBinding<bool> Wrap {get;set;}=true;
        public UiBinding<UiOverflow> Overflow {get;set;}=UiOverflow.Overflow;
    }
    public sealed class UiStyleOverrides
    {
        public UiBinding<UiColor> Color {get;set;}
        public UiBinding<UiColor> Background {get;set;}
        public UiBinding<UiColor> BorderColor {get;set;}
        public UiBinding<float> BorderWidth {get;set;}
        public UiBinding<string> Font {get;set;}
        public UiBinding<int> FontSize {get;set;}
        public UiBinding<UiFontStyle> FontStyle {get;set;}
        public UiBinding<UiAlignment> TextAlignment {get;set;}
        public UiBinding<bool> Wrap {get;set;}
        public UiBinding<UiOverflow> Overflow {get;set;}
    }
    public sealed class UiAction
    {
        private readonly Func<UiContext,object,UiActionResult> execute;
        public UiAction(Func<UiContext,object,UiActionResult> execute){this.execute=execute??throw new ArgumentNullException(nameof(execute));}
        public UiActionResult Execute(UiContext context,object input=null)=>execute(context,input);
        public static UiAction Callback(Action action)=>new UiAction((_,__)=>{action();return UiActionResult.Success;});
        public static UiAction Definition(ActionDefinition action)=>new UiAction((c,input)=>
        {var evaluation=c.Evaluation();if(input!=null)evaluation.Arguments["value"]=input;return action.Execute(evaluation,out var error)?UiActionResult.Success:UiActionResult.Rejected(error);});
    }
    public readonly struct UiActionResult
    {
        public bool Accepted {get;} public string Reason {get;}
        private UiActionResult(bool accepted,string reason){Accepted=accepted;Reason=reason;}
        public static UiActionResult Success=>new UiActionResult(true,null);
        public static UiActionResult Rejected(string reason)=>new UiActionResult(false,reason);
    }
    public abstract class UiDefinition
    {
        public string Id {get;set;}
        /// <summary>Stable semantic target for tutorial/presentation rules; defaults to Id.</summary>
        public string TargetId {get;set;}
        public UiBinding<bool> Highlighted {get;set;}=false;
        public UiBinding<UiColor> HighlightColor {get;set;}=new UiColor(1f,.78f,.16f);
        public UiBinding<float> HighlightWidth {get;set;}=3f;
        public UiBinding<bool> Visible {get;set;}=true;
        public UiBinding<bool> Enabled {get;set;}=true;
        public UiLayout Layout {get;set;}=new UiLayout();
        public UiStyle Style {get;set;}=new UiStyle();
        public UiStyleOverrides StyleOverrides {get;set;}=new UiStyleOverrides();
        public TextTemplate Tooltip {get;set;}="";
    }
    public abstract class ContainerDefinition:UiDefinition
    {
        public IReadOnlyList<UiDefinition> Children {get;set;}=Array.Empty<UiDefinition>();
        public UiInsets Padding {get;set;}
        public float Spacing {get;set;}
        public UiAlignment ChildAlignment {get;set;}=UiAlignment.Stretch;
    }
    public sealed class StackDefinition:ContainerDefinition {public UiDirection Direction {get;set;}=UiDirection.Vertical;}
    public class PanelDefinition:ContainerDefinition {public bool ClipContent {get;set;}}
    public sealed class ScrollDefinition:ContainerDefinition
    {
        public UiDefinition Content {get;set;}
        public UiDirection Direction {get;set;}=UiDirection.Vertical;
        public UiScrollbarVisibility ScrollbarVisibility {get;set;}=UiScrollbarVisibility.Auto;
        public float ScrollSensitivity {get;set;}=30;
        public float InitialPosition {get;set;}=1;
    }
    public sealed class ConditionalDefinition:UiDefinition
    {
        public UiBinding<bool> Condition {get;set;}=true;
        public UiDefinition Content {get;set;}
        public UiDefinition ElseContent {get;set;}
    }
    public sealed class TextDefinition:UiDefinition {public TextTemplate Text {get;set;}="";}
    public sealed class ProgressDefinition:UiDefinition
    {
        public UiBinding<double> Value {get;set;}=0d;
        public UiBinding<double> Minimum {get;set;}=0d;
        public UiBinding<double> Maximum {get;set;}=1d;
        public TextTemplate Caption {get;set;}="";
        public UiBinding<bool> ShowCaption {get;set;}=true;
        public UiBinding<UiColor> FillColor {get;set;}=new UiColor(.23f,.58f,.45f);
        public UiBinding<UiColor> TrackColor {get;set;}=new UiColor(.04f,.07f,.09f);
        public UiDirection Direction {get;set;}
    }
    public abstract class InteractiveDefinition:UiDefinition
    {
        public TextTemplate Caption {get;set;}="";
        public UiBinding<bool> CanExecute {get;set;}=true;
        public TextTemplate DisabledReason {get;set;}="Unavailable";
        public UiAction Action {get;set;}
        public UiInsets ContentPadding {get;set;}=new UiInsets(12,12,3,3);
    }
    public sealed class ButtonDefinition:InteractiveDefinition{}
    public sealed class ToggleDefinition:InteractiveDefinition {public UiBinding<bool> IsChecked {get;set;}=false;}
    public abstract class NumericInputDefinition:InteractiveDefinition
    {
        public UiBinding<double> Value {get;set;}=0d;
        public UiBinding<double> Minimum {get;set;}=0d;
        public UiBinding<double> Maximum {get;set;}=1d;
        public UiBinding<double> Step {get;set;}=0d;
    }
    public sealed class SliderDefinition:NumericInputDefinition
    {
        public UiCommitMode CommitMode {get;set;}=UiCommitMode.OnRelease;
        public UiDirection Direction {get;set;}
    }
    public sealed class EntityListDefinition:UiDefinition
    {
        public UiBinding<IReadOnlyList<string>> Items {get;set;}
        public Func<UiContext,UiDefinition> ItemTemplate {get;set;}
        public UiDefinition EmptyContent {get;set;}
        public UiDirection Direction {get;set;}=UiDirection.Vertical;
        public float Spacing {get;set;}
        public UiInsets Padding {get;set;}
        public UiBinding<string> SelectedId {get;set;}="";
        public UiAction OnSelectionChanged {get;set;}
    }
    public static class Ui
    {
        public static TextDefinition Text(TextTemplate text)=>new TextDefinition{Text=text};
        public static StackDefinition Row(params UiDefinition[] children)=>new StackDefinition{Direction=UiDirection.Horizontal,Children=children};
        public static StackDefinition Column(params UiDefinition[] children)=>new StackDefinition{Children=children};
        public static PanelDefinition Panel(params UiDefinition[] children)=>new PanelDefinition{Children=children};
        public static ScrollDefinition Scroll(UiDefinition content)=>new ScrollDefinition{Content=content};
        public static ConditionalDefinition When(UiBinding<bool> condition,UiDefinition content,UiDefinition otherwise=null)=>new ConditionalDefinition{Condition=condition,Content=content,ElseContent=otherwise};
        public static ProgressDefinition Progress(UiBinding<double> value,TextTemplate caption=null)=>new ProgressDefinition{Value=value,Caption=caption??""};
        public static ButtonDefinition Button(TextTemplate caption,UiAction action)=>new ButtonDefinition{Caption=caption,Action=action};
        public static ToggleDefinition Toggle(TextTemplate caption,UiBinding<bool> value,UiAction action)=>new ToggleDefinition{Caption=caption,IsChecked=value,Action=action};
        public static SliderDefinition Slider(UiBinding<double> value,UiAction action)=>new SliderDefinition{Value=value,Action=action};
        public static EntityListDefinition List(UiBinding<IReadOnlyList<string>> items,Func<UiContext,UiDefinition> row)=>new EntityListDefinition{Items=items,ItemTemplate=row};
        public static UiBinding<IReadOnlyList<string>> Query(TargetQuery query)=>UiBinding<IReadOnlyList<string>>.From(c=>query.Select(c.Evaluation()).Select(e=>e.Id).ToArray());
    }
}
