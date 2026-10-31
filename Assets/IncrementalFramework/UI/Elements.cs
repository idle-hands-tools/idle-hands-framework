using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;

namespace IncrementalFramework.UI
{
    public interface IUiElementFactory { UiElement Create(UiDefinition definition,UiContext context,UiElement parent); }
    public abstract class UiElement : IDisposable
    {
        private HashSet<Value> dependencies=new HashSet<Value>();
        private bool refreshing,again,disposed;
        private double nextRefresh,interval;
        private readonly List<UiElement> children=new List<UiElement>();
        private HashSet<string> identities;
        private string identity;
        public UiDefinition Definition {get;private set;}
        public UiContext Context {get;private set;}
        public UiElement Parent {get;private set;}
        public IReadOnlyList<UiElement> Children=>children;
        protected IUiElementFactory Factory {get;private set;}
        protected string IdentityScope {get;private set;}
        protected readonly string anonymousId=Guid.NewGuid().ToString("N");
        public bool Visible {get;private set;}
        public bool Enabled {get;private set;}
        public bool Highlighted {get;private set;}
        public UiColor HighlightColor {get;private set;}
        public float HighlightWidth {get;private set;}
        public bool Faulted {get;private set;}
        public bool IsDisposed=>disposed;
        public bool IsVisibleInTree=>Visible&&(Parent==null||(Parent.IsVisibleInTree&&Parent.AllowsChild(this)));
        protected virtual bool AllowsChild(UiElement child)=>true;
        public string Tooltip {get;private set;}
        public string Text {get;protected set;}="";
        public UiColor Color {get;private set;}
        public UiColor Background {get;private set;}
        public UiColor BorderColor {get;private set;}
        public float BorderWidth {get;private set;}
        public string Font {get;private set;}
        public int FontSize {get;private set;}
        public UiFontStyle FontStyle {get;private set;}
        public UiAlignment TextAlignment {get;private set;}
        public bool Wrap {get;private set;}
        public UiOverflow Overflow {get;private set;}
        public void Mount(UiDefinition definition,UiContext context,IUiElementFactory factory,UiElement parent=null,string scope="")
        {
            if(Definition!=null)throw new InvalidOperationException("Element already mounted.");
            Definition=definition??throw new ArgumentNullException(nameof(definition));Context=context;Factory=factory;Parent=parent;IdentityScope=scope;
            identities=parent?.identities??new HashSet<string>(StringComparer.Ordinal);
            identity=string.IsNullOrEmpty(definition.Id)?null:scope+definition.Id;
            if(identity!=null&&!identities.Add(identity))throw new ArgumentException("Duplicate UI identity: "+identity);
            try
            {
                if(definition is NumericInputDefinition numeric)
                {
                    double min=numeric.Minimum.Read(context),max=numeric.Maximum.Read(context),step=numeric.Step.Read(context);
                    if(double.IsNaN(min)||double.IsInfinity(min)||double.IsNaN(max)||double.IsInfinity(max)||max<=min||step<0||double.IsNaN(step)||double.IsInfinity(step))throw new ArgumentException("Invalid numeric input bounds or step.");
                }
                OnMount();CreateChildren(scope);
                Context.World.ValueChanged+=ValueChanged;Context.World.Added+=LifecycleChanged;Context.World.Removed+=LifecycleChanged;
                if(Context.Presentation!=null)Context.Presentation.Changed+=Refresh;
                Refresh();
            }
            catch {Dispose();throw;}
        }
        protected virtual void OnMount(){}
        protected virtual void CreateChildren(string scope){}
        protected UiElement Add(UiDefinition definition,UiContext context=null,string scope="")
        {
            if(definition==null)return null;
            var child=Factory.Create(definition,context??Context,this);
            // Add before mounting so a failed subtree is disposed by its owner.
            children.Add(child);
            child.Mount(definition,context??Context,Factory,this,scope);return child;
        }
        protected void Remove(UiElement element){if(element==null)return;children.Remove(element);element.Dispose();}
        protected void OrderChildren(IEnumerable<UiElement> order)
        {var ordered=order.ToArray();children.Clear();children.AddRange(ordered);OnChildrenChanged();}
        protected virtual void OnChildrenChanged(){}
        private void ValueChanged(Value value,object before,object after){if(dependencies.Contains(value))Refresh();}
        private void LifecycleChanged(EntityInstance entity){Refresh();}
        protected T Read<T>(UiBinding<T> binding)
        {if(binding==null)throw new ArgumentException("Missing UI binding.");TrackInterval(binding.RefreshInterval);return binding.Read(Context);}
        protected string Read(TextTemplate text){if(text==null)return "";TrackInterval(text.RefreshInterval);return text.Evaluate(Context);}
        private void TrackInterval(double value){if(value>0&&(interval==0||value<interval))interval=value;}
        public void Refresh()
        {
            if(disposed)return;if(refreshing){again=true;return;}refreshing=true;
            try
            {
                int attempts=0;
                do
                {
                    again=false;interval=0;bool previousEnabled=Enabled;
                    var reads=Context.World.ObserveReads(()=>
                    {
                        try
                        {
                            Faulted=false;
                            var presentation=Context.Presentation?.Resolve(Definition.TargetId??Definition.Id)??default;
                            Visible=Read(Definition.Visible)&&(presentation.Visible??true);
                            Enabled=Read(Definition.Enabled)&&(presentation.Enabled??true)&&(Parent?.Enabled??true);
                            Highlighted=presentation.Highlighted??Read(Definition.Highlighted);
                            HighlightColor=presentation.HighlightColor??Read(Definition.HighlightColor);
                            HighlightWidth=presentation.HighlightWidth??Read(Definition.HighlightWidth);
                            if(float.IsNaN(HighlightWidth)||float.IsInfinity(HighlightWidth)||HighlightWidth<0)throw new ArgumentException("Invalid highlight width.");
                            Tooltip=Read(Definition.Tooltip);
                            var style=Definition.Style??new UiStyle();var overrides=Definition.StyleOverrides;
                            Color=Read(overrides?.Color??style.Color);Background=Read(overrides?.Background??style.Background);BorderColor=Read(overrides?.BorderColor??style.BorderColor);BorderWidth=Read(overrides?.BorderWidth??style.BorderWidth);
                            Font=Read(overrides?.Font??style.Font);FontSize=Read(overrides?.FontSize??style.FontSize);FontStyle=Read(overrides?.FontStyle??style.FontStyle);TextAlignment=Read(overrides?.TextAlignment??style.TextAlignment);Wrap=Read(overrides?.Wrap??style.Wrap);Overflow=Read(overrides?.Overflow??style.Overflow);
                            Evaluate();
                        }
                        catch(Exception error){Faulted=true;Enabled=false;Highlighted=false;Context.Diagnostic?.Invoke("UI "+(Definition.Id??GetType().Name)+": "+error.Message);}
                    });
                    dependencies=new HashSet<Value>(reads);OnRender();
                    if(previousEnabled!=Enabled)foreach(var child in children.ToArray())child.Refresh();
                }while(again&&!disposed&&++attempts<8);
                if(again)Context.Diagnostic?.Invoke("UI refresh cascade exceeded eight passes.");
            }
            finally{refreshing=false;}
        }
        public void RefreshTree(){Refresh();foreach(var child in children.ToArray())child.RefreshTree();}
        public void Tick(double elapsedTime)
        {
            if(disposed)return;if(interval>0&&elapsedTime>=nextRefresh){nextRefresh=elapsedTime+interval;Refresh();}
            foreach(var child in children.ToArray())child.Tick(elapsedTime);
        }
        protected virtual void Evaluate(){}
        protected abstract void OnRender();
        protected virtual void OnDispose(){}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(Context!=null){Context.World.ValueChanged-=ValueChanged;Context.World.Added-=LifecycleChanged;Context.World.Removed-=LifecycleChanged;}
            if(Context?.Presentation!=null)Context.Presentation.Changed-=Refresh;
            foreach(var child in children.ToArray())child.Dispose();children.Clear();dependencies.Clear();
            if(identity!=null)identities?.Remove(identity);OnDispose();
        }
        protected UiActionResult Dispatch(UiAction action,object input=null)
        {
            UiActionResult result;
            try{result=action==null?UiActionResult.Rejected("No action is configured."):action.Execute(Context,input);}
            catch(Exception error){Context.Diagnostic?.Invoke(error.Message);result=UiActionResult.Rejected(error.Message);}
            if(!result.Accepted)Context.ActionRejected?.Invoke(result.Reason);
            if(!disposed)RefreshTree();return result;
        }
    }
    public abstract class UiContainer:UiElement
    {
        protected override void CreateChildren(string scope)
        {foreach(var child in ((ContainerDefinition)Definition).Children)Add(child,scope:scope);}
    }
    public abstract class UiStack:UiContainer{}
    public abstract class UiPanel:UiContainer{}
    public abstract class UiScrollPanel:UiContainer
    {protected override void CreateChildren(string scope){Add(((ScrollDefinition)Definition).Content,scope:scope);}}
    public abstract class UiConditionalPanel:UiElement
    {
        public UiElement Content {get;private set;} public UiElement ElseContent {get;private set;}
        public bool Condition {get;private set;}
        protected override void CreateChildren(string scope)
        {var d=(ConditionalDefinition)Definition;Content=Add(d.Content,scope:scope);ElseContent=Add(d.ElseContent,scope:scope);}
        protected override void Evaluate(){bool before=Condition;Condition=Read(((ConditionalDefinition)Definition).Condition);if(before!=Condition)foreach(var child in Children)child.RefreshTree();}
        protected override bool AllowsChild(UiElement child)=>ReferenceEquals(child,Content)?Condition:!Condition;
    }
    public abstract class UiText:UiElement
    {protected override void Evaluate(){Text=Read(((TextDefinition)Definition).Text);}}
    public abstract class UiProgress:UiElement
    {
        public double Fraction {get;private set;}
        public bool ShowCaption {get;private set;}
        public UiColor FillColor {get;private set;} public UiColor TrackColor {get;private set;}
        protected override void Evaluate()
        {
            var d=(ProgressDefinition)Definition;double min=Read(d.Minimum),max=Read(d.Maximum),value=Read(d.Value);
            bool valid=Finite(min)&&Finite(max)&&Finite(value)&&max>min;
            Fraction=valid?Math.Max(0,Math.Min(1,(value-min)/(max-min))):0;
            if(!valid)Context.Diagnostic?.Invoke("Invalid progress range or value.");
            Text=Read(d.Caption);ShowCaption=Read(d.ShowCaption);FillColor=Read(d.FillColor);TrackColor=Read(d.TrackColor);
        }
        protected static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
    public abstract class UiInteractiveElement:UiElement
    {
        public bool CanExecute {get;private set;}
        public string DisabledReason {get;private set;}
        protected override void Evaluate()
        {var d=(InteractiveDefinition)Definition;Text=Read(d.Caption);CanExecute=Read(d.CanExecute);DisabledReason=Read(d.DisabledReason);EvaluateInput();}
        protected virtual void EvaluateInput(){}
        protected UiActionResult Request(object input=null)
        {
            if(IsDisposed)return UiActionResult.Rejected("Element is disposed.");
            // Parent availability can have changed through a custom reader without a Value notification.
            for(var ancestor=Parent;ancestor!=null;ancestor=ancestor.Parent)ancestor.Refresh();
            Refresh();
            if(IsDisposed||!Enabled||!CanExecute||!IsVisibleInTree||Faulted)
            {var result=UiActionResult.Rejected(DisabledReason??"Unavailable");Context.ActionRejected?.Invoke(result.Reason);return result;}
            return Dispatch(((InteractiveDefinition)Definition).Action,input);
        }
    }
    public abstract class UiButton:UiInteractiveElement {public UiActionResult Activate()=>Request();}
    public abstract class UiToggle:UiInteractiveElement
    {
        public bool IsChecked {get;private set;}
        protected override void EvaluateInput(){IsChecked=Read(((ToggleDefinition)Definition).IsChecked);}
        public UiActionResult RequestChange(bool value)=>Request(value);
    }
    public abstract class UiNumericInput:UiInteractiveElement
    {
        public double Value {get;private set;} public double Minimum {get;private set;} public double Maximum {get;private set;} public double Step {get;private set;}
        protected override void EvaluateInput()
        {
            var d=(NumericInputDefinition)Definition;Value=Read(d.Value);Minimum=Read(d.Minimum);Maximum=Read(d.Maximum);Step=Read(d.Step);
            if(!Finite(Value)||!Finite(Minimum)||!Finite(Maximum)||!Finite(Step)||Maximum<=Minimum||Step<0)throw new ArgumentException("Invalid slider value, bounds or step.");
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        public UiActionResult RequestChange(double value)
        {
            Refresh();if(!Finite(value)||Faulted)return UiActionResult.Rejected("Invalid numeric input.");
            value=Math.Max(Minimum,Math.Min(Maximum,value));if(Step>0)value=Math.Min(Maximum,Minimum+Math.Round((value-Minimum)/Step,MidpointRounding.AwayFromZero)*Step);
            return Request(value);
        }
    }
    public abstract class UiSlider:UiNumericInput{}
    public abstract class UiEntityList:UiElement
    {
        private readonly Dictionary<string,UiElement> rows=new Dictionary<string,UiElement>(StringComparer.Ordinal);
        private UiElement empty;
        public IReadOnlyDictionary<string,UiElement> Rows=>rows;
        public string SelectedId {get;private set;}
        protected override void CreateChildren(string scope){empty=Add(((EntityListDefinition)Definition).EmptyContent,scope:scope);}
        protected override void Evaluate()
        {
            var d=(EntityListDefinition)Definition;
            var items=Read(d.Items);if(items==null||d.ItemTemplate==null)throw new ArgumentException("Entity list requires items and an item template.");
            var unique=new HashSet<string>(StringComparer.Ordinal);var ordered=new List<string>();
            foreach(string id in items){if(id==null||!unique.Add(id))throw new ArgumentException("Duplicate or null entity list ID.");if(Context.World.Entities.ContainsKey(id))ordered.Add(id);}
            foreach(string id in rows.Keys.Where(id=>!ordered.Contains(id)||!ReferenceEquals(rows[id].Context.Item,Context.World.Find(id))).ToArray()){Remove(rows[id]);rows.Remove(id);}
            foreach(string id in ordered)if(!rows.ContainsKey(id))
            {var context=Context.ForItem(Context.World.Find(id));rows.Add(id,Add(d.ItemTemplate(context),context,IdentityScope+(Definition.Id??anonymousId)+"/"+id+"/"));}
            SelectedId=Read(d.SelectedId);empty?.RefreshTree();
            OrderChildren(ordered.Select(id=>rows[id]).Concat(empty==null?Enumerable.Empty<UiElement>():new[]{empty}));
        }
        protected override bool AllowsChild(UiElement child)=>!ReferenceEquals(child,empty)||IsEmpty;
        public bool IsEmpty=>rows.Count==0;
        public UiActionResult Select(string id)
        {
            for(var ancestor=Parent;ancestor!=null;ancestor=ancestor.Parent)ancestor.Refresh();
            Refresh();if(IsDisposed||Faulted||!Enabled||!IsVisibleInTree||!rows.ContainsKey(id))return UiActionResult.Rejected("Unavailable list selection.");
            return Dispatch(((EntityListDefinition)Definition).OnSelectionChanged,id);
        }
    }
}
