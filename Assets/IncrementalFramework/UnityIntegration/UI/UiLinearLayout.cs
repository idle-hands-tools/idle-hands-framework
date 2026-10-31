using System;
using System.Linq;
using IncrementalFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace IncrementalFramework.Unity.UI
{
    /// <summary>Linear layout with per-element maximum sizes, growth weights and cross-axis alignment.</summary>
    public sealed class UiLinearLayout:LayoutGroup
    {
        public UiDirection Direction;
        public float Spacing;
        public UiAlignment Alignment=UiAlignment.Stretch;
        private bool Main(int axis)=>(Direction==UiDirection.Horizontal?0:1)==axis;
        public override void CalculateLayoutInputHorizontal(){base.CalculateLayoutInputHorizontal();Measure(0);}
        public override void CalculateLayoutInputVertical(){Measure(1);}
        private float Padding(int axis)=>axis==0?padding.horizontal:padding.vertical;
        private float LeadingPadding(int axis)=>axis==0?padding.left:padding.top;
        private static float Maximum(RectTransform child,int axis)
        {var layout=child.GetComponent<UiLayoutMetrics>()?.Layout;return layout==null?float.PositiveInfinity:axis==0?layout.MaxWidth:layout.MaxHeight;}
        private void Measure(int axis)
        {
            float minimum=0,preferred=0,flexible=0;
            foreach(var child in rectChildren)
            {
                float min=LayoutUtility.GetMinSize(child,axis),pref=Math.Max(min,LayoutUtility.GetPreferredSize(child,axis));
                if(Main(axis)){minimum+=min;preferred+=pref;flexible+=LayoutUtility.GetFlexibleSize(child,axis);}
                else{minimum=Math.Max(minimum,min);preferred=Math.Max(preferred,pref);}
            }
            float extra=Padding(axis)+(Main(axis)?Spacing*Math.Max(0,rectChildren.Count-1):0);
            SetLayoutInputForAxis(minimum+extra,preferred+extra,flexible,axis);
        }
        public override void SetLayoutHorizontal(){Arrange(0);}
        public override void SetLayoutVertical(){Arrange(1);}
        private void Arrange(int axis)
        {
            float available=Math.Max(0,rectTransform.rect.size[axis]-Padding(axis));
            if(!Main(axis))
            {
                foreach(var child in rectChildren)
                {
                    var local=child.GetComponent<UiLayoutMetrics>()?.Layout;var alignment=local==null||local.Alignment==UiAlignment.Stretch?Alignment:local.Alignment;
                    float preferred=LayoutUtility.GetPreferredSize(child,axis),minimum=LayoutUtility.GetMinSize(child,axis);
                    float size=Math.Min(Maximum(child,axis),alignment==UiAlignment.Stretch?available:Math.Min(available,Math.Max(minimum,preferred)));
                    float offset=alignment==UiAlignment.Center?(available-size)/2:alignment==UiAlignment.End?available-size:0;
                    SetChildAlongAxis(child,axis,LeadingPadding(axis)+Math.Max(0,offset),Math.Max(0,size));
                }
                return;
            }
            available=Math.Max(0,available-Spacing*Math.Max(0,rectChildren.Count-1));
            int count=rectChildren.Count;var sizes=new float[count];var minimums=new float[count];var weights=new float[count];var maximums=new float[count];
            for(int i=0;i<count;i++)
            {var child=rectChildren[i];minimums[i]=LayoutUtility.GetMinSize(child,axis);maximums[i]=Maximum(child,axis);sizes[i]=Math.Min(maximums[i],Math.Max(minimums[i],LayoutUtility.GetPreferredSize(child,axis)));weights[i]=Math.Max(0,LayoutUtility.GetFlexibleSize(child,axis));}
            float total=sizes.Sum();
            if(total>available)
            {
                float minTotal=minimums.Sum();float fraction=total>minTotal?Mathf.Clamp01((available-minTotal)/(total-minTotal)):0;
                for(int i=0;i<count;i++)sizes[i]=minimums[i]+(sizes[i]-minimums[i])*fraction;
            }
            else
            {
                // Redistribute growth when an element reaches its maximum.
                float remainder=available-total;
                for(int pass=0;pass<count&&remainder>.01f;pass++)
                {
                    float weight=0;for(int i=0;i<count;i++)if(sizes[i]<maximums[i])weight+=weights[i];if(weight<=0)break;
                    float consumed=0;
                    for(int i=0;i<count;i++)if(weights[i]>0&&sizes[i]<maximums[i])
                    {float growth=Math.Min(maximums[i]-sizes[i],remainder*weights[i]/weight);sizes[i]+=growth;consumed+=growth;}
                    if(consumed<=0)break;remainder-=consumed;
                }
            }
            float position=LeadingPadding(axis);
            for(int i=0;i<count;i++){SetChildAlongAxis(rectChildren[i],axis,position,sizes[i]);position+=sizes[i]+Spacing;}
        }
    }
}
