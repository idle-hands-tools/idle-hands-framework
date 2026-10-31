using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.UI;

namespace IncrementalFramework.Tutorials
{
    /// <summary>A temporary rule for a semantic UI target. Null leaves the corresponding setting alone.</summary>
    public sealed class TutorialUiRule
    {
        public string TargetId { get; }
        public bool? Enabled { get; }
        public bool? Visible { get; }
        public bool? Highlighted { get; }
        public UiColor? HighlightColor { get; }
        public float? HighlightWidth { get; }
        public TutorialUiRule(string targetId,bool? enabled=null,bool? visible=null,bool? highlighted=null,
            UiColor? highlightColor=null,float? highlightWidth=null)
        {
            TargetId=TutorialDefinition.RequireId(targetId);Enabled=enabled;Visible=visible;Highlighted=highlighted;
            if(highlightWidth.HasValue&&(!Finite(highlightWidth.Value)||highlightWidth.Value<0))throw new ArgumentOutOfRangeException(nameof(highlightWidth));
            if(highlightColor.HasValue)
            {var c=highlightColor.Value;if(!Finite(c.R)||!Finite(c.G)||!Finite(c.B)||!Finite(c.A))throw new ArgumentException("Highlight color must be finite.");}
            HighlightColor=highlightColor;HighlightWidth=highlightWidth;
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
    public sealed class TutorialStep
    {
        public string NodeId { get; }
        public string Title { get; }
        public string Instructions { get; }
        public int Priority { get; }
        public IReadOnlyList<TutorialUiRule> Rules { get; }
        public TutorialStep(string nodeId,IEnumerable<TutorialUiRule> rules=null,string title=null,string instructions=null,int priority=0)
        {
            NodeId=TutorialDefinition.RequireId(nodeId);Title=title;Instructions=instructions;Priority=priority;
            Rules=TutorialDefinition.CopyRules(rules);
        }
    }
    /// <summary>Presentation content over an existing graph definition; progress remains owned by GraphModule.</summary>
    public sealed class TutorialDefinition
    {
        public string GraphId { get; }
        public IReadOnlyList<TutorialUiRule> Baseline { get; }
        public IReadOnlyList<TutorialStep> Steps { get; }
        public TutorialDefinition(string graphId,IEnumerable<TutorialStep> steps,IEnumerable<TutorialUiRule> baseline=null)
        {
            GraphId=RequireId(graphId);
            var values=(steps??throw new ArgumentNullException(nameof(steps))).ToArray();
            if(values.Length==0||values.Any(s=>s==null)||values.Select(s=>s.NodeId).Distinct(StringComparer.Ordinal).Count()!=values.Length)
                throw new ArgumentException("Tutorial requires uniquely identified steps.");
            Steps=Array.AsReadOnly(values);Baseline=CopyRules(baseline);
        }
        internal static string RequireId(string value)=>string.IsNullOrWhiteSpace(value)?throw new ArgumentException("Tutorial IDs must not be empty."):value;
        internal static IReadOnlyList<TutorialUiRule> CopyRules(IEnumerable<TutorialUiRule> rules)
        {
            var values=(rules??Array.Empty<TutorialUiRule>()).ToArray();
            if(values.Any(r=>r==null)||values.Select(r=>r.TargetId).Distinct(StringComparer.Ordinal).Count()!=values.Length)
                throw new ArgumentException("Duplicate or null tutorial UI target rules.");
            return Array.AsReadOnly(values);
        }
    }
    public sealed class TutorialStepView
    {
        public string InstanceId { get; }
        public long Run { get; }
        public string NodeId { get; }
        public string Title { get; }
        public string Instructions { get; }
        public int Priority { get; }
        internal TutorialStepView(string instance,long run,TutorialStep step,string title,string instructions)
        {InstanceId=instance;Run=run;NodeId=step.NodeId;Title=title;Instructions=instructions;Priority=step.Priority;}
    }
}
