using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IncrementalFramework.Unity.UI
{
    public sealed class UnityUiFactory:IUiElementFactory
    {
        private readonly Transform root;
        public UnityUiFactory(Transform root){this.root=root;}
        public UiElement Create(UiDefinition definition,UiContext context,UiElement parent)
        {
            Transform target=parent is IUnityUiElement display?display.View.Content:root;
            switch(definition)
            {
                case TextDefinition _:return new UnityText(target);
                case ButtonDefinition _:return new UnityButton(target);
                case ToggleDefinition _:return new UnityToggle(target);
                case SliderDefinition _:return new UnitySlider(target);
                case ProgressDefinition _:return new UnityProgress(target);
                case StackDefinition _:return new UnityStack(target);
                case ScrollDefinition _:return new UnityScrollPanel(target);
                case PanelDefinition _:return new UnityPanel(target);
                case ConditionalDefinition _:return new UnityConditionalPanel(target);
                case EntityListDefinition _:return new UnityEntityList(target);
                default:throw new ArgumentException("Unknown UI declaration: "+definition.GetType().Name);
            }
        }
        public static MountedUi Mount(Transform parent,UiDefinition definition,UiContext context,IUiElementFactory factory=null)
        {
            factory=factory??new UnityUiFactory(parent);var element=factory.Create(definition,context,null);
            try{element.Mount(definition,context,factory);return new MountedUi(element);}
            catch{element.Dispose();throw;}
        }
    }
    public sealed class MountedUi:IDisposable
    {
        public UiElement Element {get;}
        public RectTransform Root=>((IUnityUiElement)Element).View.Root;
        private readonly UiViewDriver driver;
        public MountedUi(UiElement element)
        {
            Element=element;
            var owner=Root.GetComponentInParent<Canvas>()?.gameObject??Root.root.gameObject;
            driver=owner.GetComponent<UiViewDriver>()??owner.AddComponent<UiViewDriver>();driver.Register(element);
            Root.gameObject.AddComponent<UiViewLifetime>().Element=element;
        }
        public void Refresh()=>Element.RefreshTree();
        public void Dispose(){driver.Unregister(Element);Element.Dispose();}
    }
    // Runs on the canvas, so a timed visibility binding can reveal an inactive element.
    public sealed class UiViewDriver:MonoBehaviour
    {
        private readonly HashSet<UiElement> roots=new HashSet<UiElement>();
        public void Register(UiElement element){roots.Add(element);}
        public void Unregister(UiElement element){roots.Remove(element);}
        private void Update(){foreach(var root in roots.ToArray()){if(root.IsDisposed)roots.Remove(root);else root.Tick(Time.unscaledTimeAsDouble);}}
        private void OnDestroy(){foreach(var root in roots.ToArray())root.Dispose();roots.Clear();}
    }
    public sealed class UiViewLifetime:MonoBehaviour
    { public UiElement Element; private void OnDestroy(){Element?.Dispose();} }
    public sealed class UnityText:UiText,IUnityUiElement
    {
        public UnityUiView View {get;}
        public UnityText(Transform parent){View=new UnityUiView(parent,"Label");}
        protected override void OnMount(){View.Text();}
        protected override void OnRender(){View.Apply(this);}
        protected override void OnDispose(){View.Dispose();}
    }
    public sealed class UnityButton:UiButton,IUnityUiElement
    {
        public UnityUiView View {get;} private Button button;
        public UnityButton(Transform parent){View=new UnityUiView(parent,"Button");}
        protected override void OnMount()
        {
            var image=View.Image();button=View.Surface.gameObject.AddComponent<Button>();button.targetGraphic=image;
            var colors=button.colors;colors.highlightedColor=new Color(.8f,.9f,1);colors.disabledColor=new Color(.45f,.48f,.50f,.65f);button.colors=colors;
            View.Text();button.onClick.AddListener(Click);
        }
        private void Click(){Activate();}
        protected override void OnRender(){View.Apply(this);View.Padding(((ButtonDefinition)Definition).ContentPadding);button.interactable=Enabled&&CanExecute&&!Faulted;}
        protected override void OnDispose(){if(button!=null)button.onClick.RemoveListener(Click);View.Dispose();}
    }
    public sealed class UnityToggle:UiToggle,IUnityUiElement
    {
        public UnityUiView View {get;} private Toggle toggle;
        public UnityToggle(Transform parent){View=new UnityUiView(parent,"Toggle");}
        protected override void OnMount()
        {
            var image=View.Image();toggle=View.Surface.gameObject.AddComponent<Toggle>();toggle.targetGraphic=image;
            var check=UnityUiView.Rect(View.Surface,"Check");check.anchorMin=new Vector2(0,.5f);check.anchorMax=new Vector2(0,.5f);check.sizeDelta=new Vector2(18,18);check.anchoredPosition=new Vector2(15,0);
            toggle.graphic=check.gameObject.AddComponent<Image>();toggle.graphic.color=UnityEngine.Color.white;
            View.Text();toggle.onValueChanged.AddListener(Change);
        }
        private void Change(bool value){RequestChange(value);}
        protected override void OnRender()
        {View.Apply(this);View.Padding(new UiInsets(36,8,3,3));toggle.interactable=Enabled&&CanExecute&&!Faulted;toggle.SetIsOnWithoutNotify(IsChecked);}
        protected override void OnDispose(){if(toggle!=null)toggle.onValueChanged.RemoveListener(Change);View.Dispose();}
    }
    public sealed class UnitySlider:UiSlider,IUnityUiElement
    {
        public UnityUiView View {get;} private Slider slider;private UiSliderInput input;
        public UnitySlider(Transform parent){View=new UnityUiView(parent,"Slider");}
        protected override void OnMount()
        {
            var d=(SliderDefinition)Definition;double min=d.Minimum.Read(Context),max=d.Maximum.Read(Context),step=d.Step.Read(Context);
            if(double.IsNaN(min)||double.IsNaN(max)||double.IsInfinity(min)||double.IsInfinity(max)||max<=min||step<0||double.IsNaN(step)||double.IsInfinity(step))throw new ArgumentException("Invalid slider bounds or step.");
            View.Image();input=View.Surface.gameObject.AddComponent<UiSliderInput>();slider=View.Surface.gameObject.AddComponent<Slider>();slider.minValue=0;slider.maxValue=1;
            slider.direction=d.Direction==UiDirection.Vertical?Slider.Direction.BottomToTop:Slider.Direction.LeftToRight;
            var fill=UnityUiView.Rect(View.Surface,"Fill");UnityUiView.Stretch(fill);fill.gameObject.AddComponent<Image>().color=new Color(.3f,.6f,.7f);slider.fillRect=fill;
            var handle=UnityUiView.Rect(View.Surface,"Handle");handle.sizeDelta=new Vector2(16,16);slider.targetGraphic=handle.gameObject.AddComponent<Image>();slider.handleRect=handle;
            View.Text();input.Commit=()=>{if(((SliderDefinition)Definition).CommitMode==UiCommitMode.OnRelease)Commit();};slider.onValueChanged.AddListener(Change);
        }
        private void Change(float value)
        {if(((SliderDefinition)Definition).CommitMode==UiCommitMode.Continuous||!input.Dragging)Commit();}
        private void Commit(){RequestChange(Minimum+slider.value*(Maximum-Minimum));}
        protected override void OnRender()
        {View.Apply(this);slider.interactable=Enabled&&CanExecute&&!Faulted;if(!Faulted&&(!input.Dragging||!slider.interactable))slider.SetValueWithoutNotify((float)((Value-Minimum)/(Maximum-Minimum)));}
        protected override void OnDispose(){if(slider!=null)slider.onValueChanged.RemoveListener(Change);if(input!=null)input.Commit=null;View.Dispose();}
    }
    public sealed class UiSliderInput:MonoBehaviour,IPointerDownHandler,IPointerUpHandler
    {
        public bool Dragging {get;private set;}public Action Commit;
        public void OnPointerDown(PointerEventData data){Dragging=true;}
        public void OnPointerUp(PointerEventData data){if(!Dragging)return;Dragging=false;Commit?.Invoke();}
        private void OnDisable(){Dragging=false;}
    }
    public sealed class UnityProgress:UiProgress,IUnityUiElement
    {
        public UnityUiView View {get;}private RectTransform fill;private Image image;
        public UnityProgress(Transform parent){View=new UnityUiView(parent,"Progress");}
        protected override void OnMount(){View.Image();fill=UnityUiView.Rect(View.Surface,"Fill");UnityUiView.Stretch(fill);image=fill.gameObject.AddComponent<Image>();image.raycastTarget=false;View.Text();}
        protected override void OnRender()
        {
            View.Apply(this);View.Image().color=UnityUiView.Color(TrackColor);image.color=UnityUiView.Color(FillColor);
            fill.anchorMax=((ProgressDefinition)Definition).Direction==UiDirection.Vertical?new Vector2(1,(float)Fraction):new Vector2((float)Fraction,1);
            View.Label.gameObject.SetActive(ShowCaption);
        }
        protected override void OnDispose(){View.Dispose();}
    }
    public sealed class UnityStack:UiStack,IUnityUiElement
    {
        public UnityUiView View {get;}
        public UnityStack(Transform parent){View=new UnityUiView(parent,"Stack");}
        protected override void OnMount(){var d=(StackDefinition)Definition;UnityUiView.Stack(View.Surface,d.Direction,d.Spacing,d.Padding,d.ChildAlignment);}
        protected override void OnRender(){View.Apply(this);}
        protected override void OnDispose(){View.Dispose();}
    }
    public sealed class UnityPanel:UiPanel,IUnityUiElement
    {
        public UnityUiView View {get;}
        public UnityPanel(Transform parent){View=new UnityUiView(parent,"Panel");}
        protected override void OnMount(){var d=(PanelDefinition)Definition;View.Image();UnityUiView.Stack(View.Surface,UiDirection.Vertical,d.Spacing,d.Padding,d.ChildAlignment);if(d.ClipContent)View.Surface.gameObject.AddComponent<RectMask2D>();}
        protected override void OnRender(){View.Apply(this);}
        protected override void OnDispose(){View.Dispose();}
    }
    public sealed class UnityConditionalPanel:UiConditionalPanel,IUnityUiElement
    {
        public UnityUiView View {get;}
        public UnityConditionalPanel(Transform parent){View=new UnityUiView(parent,"Conditional");}
        protected override void OnMount(){UnityUiView.Stack(View.Surface,UiDirection.Vertical,0,new UiInsets());}
        protected override void OnRender()
        {View.Apply(this);if(Content is IUnityUiElement content)content.View.Root.gameObject.SetActive(Condition&&Content.Visible);if(ElseContent is IUnityUiElement other)other.View.Root.gameObject.SetActive(!Condition&&ElseContent.Visible);}
        protected override void OnDispose(){View.Dispose();}
    }
    public sealed class UnityScrollPanel:UiScrollPanel,IUnityUiElement
    {
        public UnityUiView View {get;}private ScrollRect scroll;
        public UnityScrollPanel(Transform parent){View=new UnityUiView(parent,"Scroll");}
        protected override void OnMount()
        {
            var d=(ScrollDefinition)Definition;View.Image();scroll=View.Surface.gameObject.AddComponent<ScrollRect>();
            var viewport=UnityUiView.Rect(View.Surface,"Viewport");UnityUiView.Stretch(viewport);viewport.gameObject.AddComponent<RectMask2D>();viewport.gameObject.AddComponent<Image>().color=UnityEngine.Color.clear;
            var content=UnityUiView.Rect(viewport,"Content");content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            UnityUiView.Stack(content,d.Direction,d.Spacing,d.Padding,d.ChildAlignment);
            var fit=content.gameObject.AddComponent<ContentSizeFitter>();fit.verticalFit=d.Direction==UiDirection.Vertical?ContentSizeFitter.FitMode.PreferredSize:ContentSizeFitter.FitMode.Unconstrained;
            fit.horizontalFit=d.Direction==UiDirection.Horizontal?ContentSizeFitter.FitMode.PreferredSize:ContentSizeFitter.FitMode.Unconstrained;
            if(d.Direction==UiDirection.Horizontal){content.anchorMin=new Vector2(0,0);content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0, .5f);}
            View.Content=content;scroll.viewport=viewport;scroll.content=content;scroll.horizontal=d.Direction==UiDirection.Horizontal;scroll.vertical=!scroll.horizontal;scroll.scrollSensitivity=d.ScrollSensitivity;scroll.movementType=ScrollRect.MovementType.Clamped;
            if(d.ScrollbarVisibility!=UiScrollbarVisibility.Hidden)
            {
                var track=UnityUiView.Rect(View.Surface,"Scrollbar");bool vertical=d.Direction==UiDirection.Vertical;
                track.anchorMin=vertical?new Vector2(1,0):new Vector2(0,0);track.anchorMax=vertical?new Vector2(1,1):new Vector2(1,0);track.pivot=vertical?new Vector2(1,.5f):new Vector2(.5f,0);track.sizeDelta=vertical?new Vector2(10,0):new Vector2(0,10);
                track.gameObject.AddComponent<Image>().color=new Color(.05f,.08f,.1f);
                var bar=track.gameObject.AddComponent<Scrollbar>();var handle=UnityUiView.Rect(track,"Handle");UnityUiView.Stretch(handle);bar.targetGraphic=handle.gameObject.AddComponent<Image>();bar.handleRect=handle;bar.direction=vertical?Scrollbar.Direction.BottomToTop:Scrollbar.Direction.LeftToRight;
                var visibility=d.ScrollbarVisibility==UiScrollbarVisibility.Always?ScrollRect.ScrollbarVisibility.Permanent:ScrollRect.ScrollbarVisibility.AutoHide;
                if(vertical){scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=visibility;viewport.offsetMax=new Vector2(-12,0);}else{scroll.horizontalScrollbar=bar;scroll.horizontalScrollbarVisibility=visibility;viewport.offsetMin=new Vector2(0,12);}
            }
        }
        protected override void CreateChildren(string scope){base.CreateChildren(scope);scroll.normalizedPosition=((ScrollDefinition)Definition).Direction==UiDirection.Vertical?new Vector2(0,((ScrollDefinition)Definition).InitialPosition):new Vector2(((ScrollDefinition)Definition).InitialPosition,1);}
        protected override void OnRender()
        {View.Apply(this);scroll.enabled=Enabled&&!Faulted;if(scroll.verticalScrollbar!=null)scroll.verticalScrollbar.interactable=Enabled&&!Faulted;if(scroll.horizontalScrollbar!=null)scroll.horizontalScrollbar.interactable=Enabled&&!Faulted;}
        protected override void OnDispose(){View.Dispose();}
    }
    public sealed class UnityEntityList:UiEntityList,IUnityUiElement
    {
        public UnityUiView View {get;}
        public UnityEntityList(Transform parent){View=new UnityUiView(parent,"Entity list");}
        protected override void OnMount(){var d=(EntityListDefinition)Definition;UnityUiView.Stack(View.Surface,d.Direction,d.Spacing,d.Padding);}
        protected override void OnChildrenChanged()
        {
            for(int i=0;i<Children.Count;i++)if(Children[i] is IUnityUiElement display)display.View.Root.SetSiblingIndex(i);
        }
        protected override void OnRender()
        {
            View.Apply(this);var d=(EntityListDefinition)Definition;
            foreach(var pair in Rows)
            {
                var rect=((IUnityUiElement)pair.Value).View.Root;
                if(d.OnSelectionChanged!=null)
                {var click=rect.GetComponent<UiRowSelection>()??rect.gameObject.AddComponent<UiRowSelection>();string id=pair.Key;click.Select=()=>Select(id);}
            }
            foreach(var child in Children)if(!Rows.Values.Contains(child)&&child is IUnityUiElement empty)empty.View.Root.gameObject.SetActive(IsEmpty&&child.Visible);
        }
        protected override void OnDispose(){View.Dispose();}
    }
    public sealed class UiRowSelection:MonoBehaviour,IPointerClickHandler
    {public Action Select;public void OnPointerClick(PointerEventData data){Select?.Invoke();}}
}
