using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Progression.Graphs;
using IncrementalFramework.Tutorials;
using IncrementalFramework.UI;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    public enum TutorialToggle { Unchanged, On, Off }
    [Serializable]
    public sealed class TutorialUiRuleEntry
    {
        public string targetId;
        public TutorialToggle enabled;
        public TutorialToggle visible;
        public TutorialToggle highlighted;
        public bool overrideHighlightColor;
        public Color highlightColor=new Color(1,.78f,.16f);
        public bool overrideHighlightWidth;
        [Min(0)] public float highlightWidth=3;
        private static bool? Value(TutorialToggle value)
        {switch(value){case TutorialToggle.Unchanged:return null;case TutorialToggle.On:return true;case TutorialToggle.Off:return false;default:throw new ArgumentException("Invalid tutorial toggle.");}}
        public TutorialUiRule Compile()=>new TutorialUiRule(targetId,Value(enabled),Value(visible),Value(highlighted),
            overrideHighlightColor?new UiColor(highlightColor.r,highlightColor.g,highlightColor.b,highlightColor.a):(UiColor?)null,
            overrideHighlightWidth?highlightWidth:(float?)null);
    }
    [Serializable]
    public sealed class TutorialStepEntry
    {
        public string nodeId;
        public string title;
        [TextArea] public string instructions;
        public int priority;
        public TutorialUiRuleEntry[] rules=Array.Empty<TutorialUiRuleEntry>();
        public TutorialStep Compile()=>new TutorialStep(nodeId,TutorialAsset.Rules(rules),string.IsNullOrEmpty(title)?null:title,string.IsNullOrEmpty(instructions)?null:instructions,priority);
    }
    [CreateAssetMenu(menuName="Idle Hands/Tutorial")]
    public sealed class TutorialAsset : ScriptableObject
    {
        public GraphAsset graph;
        public TutorialUiRuleEntry[] baseline=Array.Empty<TutorialUiRuleEntry>();
        public TutorialStepEntry[] steps=Array.Empty<TutorialStepEntry>();
        internal static IEnumerable<TutorialUiRule> Rules(TutorialUiRuleEntry[] entries)=>(entries??throw new ArgumentException("Missing tutorial rules.")).Select(r=>(r??throw new ArgumentException("Null tutorial rule.")).Compile()).ToArray();
        public TutorialDefinition Definition()
        {
            if(graph==null)throw new ArgumentException("Tutorial requires a graph asset.");
            var definition=new TutorialDefinition(graph.id,(steps??throw new ArgumentException("Missing tutorial steps.")).Select(s=>(s??throw new ArgumentException("Null tutorial step.")).Compile()),Rules(baseline));
            var nodeIds=new HashSet<string>((graph.nodes??Array.Empty<GraphNodeEntry>()).Where(n=>n!=null).Select(n=>n.id),StringComparer.Ordinal);
            if(definition.Steps.Any(s=>!nodeIds.Contains(s.NodeId)))throw new ArgumentException("Tutorial step references an unknown graph node.");
            return definition;
        }
        public IReadOnlyList<string> Validate(GraphExtensions extensions=null)
        {
            try{Definition();graph.Compile(extensions);return Array.Empty<string>();}
            catch(Exception error) when(error is ArgumentException||error is ArithmeticException||error is FormatException){return new[]{error.Message};}
        }
        [ContextMenu("Validate Tutorial")]
        private void ValidateTutorial(){var errors=Validate();if(errors.Count==0)Debug.Log("Tutorial is valid: "+name,this);else Debug.LogError(string.Join("\n",errors),this);}
    }
}
