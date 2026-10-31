using System;
using IncrementalFramework.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace IncrementalFramework.Unity.UI
{
    public interface IUnityUiElement { UnityUiView View {get;} }
    public sealed class UnityUiView
    {
        public RectTransform Root {get;}
        public RectTransform Surface {get;}
        public RectTransform Content {get;set;}
        public Text Label {get;private set;}
        private Outline outline;
        private Image image;
        private RectTransform highlight;
        private readonly Image[] highlightEdges=new Image[4];
        private UiTooltip tooltip;
        public UnityUiView(Transform parent,string name)
        {
            Root=Rect(parent,name);Surface=Rect(Root,"Surface");Stretch(Surface);Content=Surface;
            Root.gameObject.AddComponent<UiLayoutMetrics>().Surface=Surface;
            tooltip=Surface.gameObject.AddComponent<UiTooltip>();
        }
        public Image Image()=>image??(image=Surface.GetComponent<Image>()??Surface.gameObject.AddComponent<Image>());
        public Text Text(bool fill=true)
        {
            if(Label!=null)return Label;
            var rect=Rect(Surface,"Text");if(fill)Stretch(rect);
            Label=rect.gameObject.AddComponent<Text>();Label.raycastTarget=false;return Label;
        }
        public void Apply(UiElement element)
        {
            Root.gameObject.SetActive(element.IsVisibleInTree);
            var d=element.Definition;var m=d.Layout.Margin;
            Surface.offsetMin=new Vector2(m.Left,m.Bottom);Surface.offsetMax=new Vector2(-m.Right,-m.Top);
            Root.GetComponent<UiLayoutMetrics>().Layout=d.Layout;
            if(image!=null)image.color=Color(element.Background);
            tooltip.Message=element.Tooltip;
            if(element is UiInteractiveElement interactive && (!element.Enabled||!interactive.CanExecute))
                tooltip.Message=string.IsNullOrEmpty(interactive.DisabledReason)?element.Tooltip:interactive.DisabledReason;
            if(Label!=null)
            {
                Label.text=element.Text;Label.color=Color(element.Color);Label.font=Resources.GetBuiltinResource<Font>(element.Font);
                Label.fontSize=element.FontSize;Label.fontStyle=(FontStyle)element.FontStyle;
                Label.alignment=element.TextAlignment==UiAlignment.Center?TextAnchor.MiddleCenter:element.TextAlignment==UiAlignment.End?TextAnchor.MiddleRight:TextAnchor.MiddleLeft;
                Label.horizontalOverflow=element.Wrap?HorizontalWrapMode.Wrap:HorizontalWrapMode.Overflow;
                Label.verticalOverflow=element.Overflow==UiOverflow.Clip?VerticalWrapMode.Truncate:VerticalWrapMode.Overflow;

            }
            if(!string.IsNullOrEmpty(tooltip.Message)&&Surface.GetComponent<Graphic>()==null)
            {var hit=Surface.gameObject.AddComponent<Image>();hit.color=UnityEngine.Color.clear;}
            if(element.BorderWidth>0)
            {
                if(image==null)Image().color=Color(element.Background);outline=outline??Surface.gameObject.GetComponent<Outline>()??Surface.gameObject.AddComponent<Outline>();
                outline.enabled=true;outline.effectColor=Color(element.BorderColor);outline.effectDistance=new Vector2(element.BorderWidth,-element.BorderWidth);
            }
            else if(outline!=null)outline.enabled=false;
            ApplyHighlight(element);
            LayoutRebuilder.MarkLayoutForRebuild(Root);
        }
        private void ApplyHighlight(UiElement element)
        {
            bool show=element.Highlighted&&element.HighlightWidth>0&&element.HighlightColor.A>0;
            if(!show){if(highlight!=null)highlight.gameObject.SetActive(false);return;}
            if(highlight==null)
            {
                highlight=Rect(Surface,"UiHighlight");Stretch(highlight);
                highlight.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
                for(int i=0;i<4;i++)
                {
                    var edge=Rect(highlight,new[]{"Top","Bottom","Left","Right"}[i]);
                    var graphic=edge.gameObject.AddComponent<Image>();graphic.raycastTarget=false;
                    highlightEdges[i]=graphic;
                }
            }
            highlight.gameObject.SetActive(true);highlight.SetAsLastSibling();
            float width=element.HighlightWidth;
            highlight.offsetMin=new Vector2(-width,-width);highlight.offsetMax=new Vector2(width,width);
            for(int i=0;i<4;i++)
            {
                var edge=highlightEdges[i].rectTransform;
                edge.anchorMin=i==0?new Vector2(0,1):i==3?new Vector2(1,0):Vector2.zero;
                edge.anchorMax=i==1?new Vector2(1,0):i==2?new Vector2(0,1):Vector2.one;
                edge.pivot=i==0?new Vector2(.5f,1):i==1?new Vector2(.5f,0):i==2?new Vector2(0,.5f):new Vector2(1,.5f);
                edge.sizeDelta=i<2?new Vector2(0,width):new Vector2(width,0);edge.anchoredPosition=Vector2.zero;
                highlightEdges[i].color=Color(element.HighlightColor);
            }
        }
        public void Padding(UiInsets padding)
        {if(Label==null)return;Root.GetComponent<UiLayoutMetrics>().TextPadding=padding;Label.rectTransform.offsetMin=new Vector2(padding.Left,padding.Bottom);Label.rectTransform.offsetMax=new Vector2(-padding.Right,-padding.Top);}
        public void Dispose(){if(Root!=null){Root.gameObject.SetActive(false);if(Application.isPlaying)Object.Destroy(Root.gameObject);else Object.DestroyImmediate(Root.gameObject);}}
        public static RectTransform Rect(Transform parent,string name)
        {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);return rect;}
        public static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        public static Color Color(UiColor value)=>new Color(value.R,value.G,value.B,value.A);
        public static void Stack(RectTransform rect,UiDirection direction,float spacing,UiInsets padding,UiAlignment alignment=UiAlignment.Stretch)
        {
            var group=rect.gameObject.AddComponent<UiLinearLayout>();
            group.Direction=direction;group.Spacing=spacing;group.Alignment=alignment;
            group.padding=new RectOffset((int)padding.Left,(int)padding.Right,(int)padding.Top,(int)padding.Bottom);
        }
    }
    public sealed class UiLayoutMetrics:MonoBehaviour,ILayoutElement
    {
        public UiLayout Layout=new UiLayout();public RectTransform Surface;public UiInsets TextPadding;
        public void CalculateLayoutInputHorizontal(){} public void CalculateLayoutInputVertical(){}
        public int layoutPriority=>2;
        public float minWidth=>Math.Min(Layout.MaxWidth,Layout.MinWidth+Layout.Margin.Left+Layout.Margin.Right);
        public float minHeight=>Math.Min(Layout.MaxHeight,Layout.MinHeight+Layout.Margin.Top+Layout.Margin.Bottom);
        public float preferredWidth=>Preferred(true);
        public float preferredHeight=>Preferred(false);
        public float flexibleWidth=>Layout.Width.Kind==UiLengthKind.Fill?Layout.Width.Amount:0;
        public float flexibleHeight=>Layout.Height.Kind==UiLengthKind.Fill?Layout.Height.Amount:0;
        private float Preferred(bool horizontal)
        {
            var length=horizontal?Layout.Width:Layout.Height;float margin=horizontal?Layout.Margin.Left+Layout.Margin.Right:Layout.Margin.Top+Layout.Margin.Bottom;
            float natural=Surface==null?0:LayoutUtility.GetPreferredSize(Surface,horizontal?0:1);
            if(Surface!=null&&Surface.GetComponent<LayoutGroup>()==null)
            {var text=Surface.GetComponentInChildren<Text>();if(text!=null)natural=horizontal?text.preferredWidth+TextPadding.Left+TextPadding.Right:text.preferredHeight+TextPadding.Top+TextPadding.Bottom;}
            return Mathf.Clamp((length.Kind==UiLengthKind.Pixels?length.Amount:length.Kind==UiLengthKind.Fill?0:natural)+margin,horizontal?minWidth:minHeight,horizontal?Layout.MaxWidth:Layout.MaxHeight);
        }
    }
    public sealed class UiTooltip:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
    {
        public string Message;private bool hovered;
        public void OnPointerEnter(PointerEventData data){hovered=true;}
        public void OnPointerExit(PointerEventData data){hovered=false;}
        private void OnGUI()
        {
            if(!hovered||string.IsNullOrEmpty(Message))return;
            var mouse=Input.mousePosition;var rect=new Rect(Mathf.Clamp(mouse.x+12,0,Mathf.Max(0,Screen.width-300)),Mathf.Clamp(Screen.height-mouse.y+12,0,Mathf.Max(0,Screen.height-80)),300,80);
            GUI.Box(rect,Message);
        }
    }
}
