using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Progression.Graphs;
using IncrementalFramework.UI;

namespace IncrementalFramework.Tutorials
{
    /// <summary>Owner-scoped, disposable UI projection. No gameplay state or UI declarations are mutated.</summary>
    public sealed class TutorialController : IUiPresentationPolicy, IDisposable
    {
        private readonly GraphRuntime runtime;
        private readonly Dictionary<string,TutorialDefinition> definitions;
        private Dictionary<string,UiPresentation> presentation=new Dictionary<string,UiPresentation>(StringComparer.Ordinal);
        private bool disposed;
        public string OwnerId { get; }
        public IReadOnlyList<TutorialStepView> ActiveSteps { get; private set; }=Array.Empty<TutorialStepView>();
        public event Action Changed;
        public Exception LastObserverException { get; private set; }
        private sealed class Candidate
        {
            public UiPresentation Value;
            public int Priority;
            public string Instance;
            public string Node;
        }
        public TutorialController(GraphRuntime runtime,string ownerId,IEnumerable<TutorialDefinition> definitions)
        {
            this.runtime=runtime??throw new ArgumentNullException(nameof(runtime));OwnerId=TutorialDefinition.RequireId(ownerId);
            var values=(definitions??throw new ArgumentNullException(nameof(definitions))).ToArray();
            if(values.Any(d=>d==null)||values.Select(d=>d.GraphId).Distinct(StringComparer.Ordinal).Count()!=values.Length)throw new ArgumentException("Duplicate or null tutorial definitions.");
            this.definitions=values.ToDictionary(d=>d.GraphId,StringComparer.Ordinal);
            foreach(var d in values)
            {
                var graph=runtime.Definition(d.GraphId);
                foreach(var step in d.Steps)if(!graph.Nodes.ContainsKey(step.NodeId))throw new ArgumentException("Unknown tutorial graph node: "+step.NodeId);
            }
            Rebuild();runtime.Changed+=Rebuild;
        }
        public UiPresentation Resolve(string targetId)=>!disposed&&targetId!=null&&presentation.TryGetValue(targetId,out var value)?value:default;
        // A disabled parallel step wins over an enabled sibling; explicit step values override baseline values.
        private static bool? Permission(IEnumerable<bool?> values,bool? fallback=null)
        {var specified=values.Where(v=>v.HasValue).Select(v=>v.Value).ToArray();return specified.Length==0?fallback:specified.All(v=>v);}
        private static bool? Highlight(IEnumerable<bool?> values,bool? fallback=null)
        {var specified=values.Where(v=>v.HasValue).Select(v=>v.Value).ToArray();return specified.Length==0?fallback:specified.Any(v=>v);}
        private void Rebuild()
        {
            if(disposed)return;
            var candidates=new Dictionary<string,List<Candidate>>(StringComparer.Ordinal);
            var active=new List<TutorialStepView>();
            foreach(var instance in runtime.Instances.Where(i=>i.OwnerId==OwnerId&&i.Status==GraphStatus.Active).OrderBy(i=>i.Id,StringComparer.Ordinal))
            {
                if(!definitions.TryGetValue(instance.DefinitionId,out var d))continue;
                var steps=d.Steps.Where(s=>instance.Nodes.Any(n=>n.Id==s.NodeId&&(n.Status==GraphNodeStatus.Active||n.Status==GraphNodeStatus.Ready)))
                    .OrderByDescending(s=>s.Priority).ThenBy(s=>s.NodeId,StringComparer.Ordinal).ToArray();
                foreach(var step in steps)
                {
                    var node=runtime.Definition(d.GraphId).Nodes[step.NodeId];
                    active.Add(new TutorialStepView(instance.Id,instance.Run,step,step.Title??node.Title,step.Instructions??node.Description));
                }
                foreach(var target in d.Baseline.Select(r=>r.TargetId).Concat(steps.SelectMany(s=>s.Rules.Select(r=>r.TargetId))).Distinct(StringComparer.Ordinal))
                {
                    var baseline=d.Baseline.SingleOrDefault(r=>r.TargetId==target);
                    var overrides=steps.SelectMany(s=>s.Rules.Where(r=>r.TargetId==target).Select(r=>(Step:s,Rule:r))).ToArray();
                    var color=overrides.Select(o=>o.Rule.HighlightColor).FirstOrDefault(c=>c.HasValue)??baseline?.HighlightColor;
                    var width=overrides.Select(o=>o.Rule.HighlightWidth).FirstOrDefault(c=>c.HasValue)??baseline?.HighlightWidth;
                    var item=new Candidate{Instance=instance.Id,Node=overrides.FirstOrDefault().Step?.NodeId??"",Priority=overrides.Length==0?int.MinValue:overrides[0].Step.Priority,
                        Value=new UiPresentation(Permission(overrides.Select(o=>o.Rule.Enabled),baseline?.Enabled),Permission(overrides.Select(o=>o.Rule.Visible),baseline?.Visible),
                            Highlight(overrides.Select(o=>o.Rule.Highlighted),baseline?.Highlighted),color,width)};
                    if(!candidates.TryGetValue(target,out var list)){list=new List<Candidate>();candidates.Add(target,list);}list.Add(item);
                }
            }
            var next=new Dictionary<string,UiPresentation>(StringComparer.Ordinal);
            foreach(var pair in candidates)
            {
                var ordered=pair.Value.OrderByDescending(c=>c.Priority).ThenBy(c=>c.Instance,StringComparer.Ordinal).ThenBy(c=>c.Node,StringComparer.Ordinal).ToArray();
                next.Add(pair.Key,new UiPresentation(Permission(ordered.Select(c=>c.Value.Enabled)),Permission(ordered.Select(c=>c.Value.Visible)),Highlight(ordered.Select(c=>c.Value.Highlighted)),
                    ordered.Select(c=>c.Value.HighlightColor).FirstOrDefault(c=>c.HasValue),ordered.Select(c=>c.Value.HighlightWidth).FirstOrDefault(c=>c.HasValue)));
            }
            presentation=next;ActiveSteps=Array.AsReadOnly(active.OrderByDescending(s=>s.Priority).ThenBy(s=>s.InstanceId,StringComparer.Ordinal).ThenBy(s=>s.NodeId,StringComparer.Ordinal).ToArray());Notify();
        }
        private void Notify()
        {if(Changed!=null)foreach(Action listener in Changed.GetInvocationList())try{listener();}catch(Exception error){LastObserverException=error;}}
        public void Dispose()
        {
            if(disposed)return;disposed=true;runtime.Changed-=Rebuild;presentation.Clear();ActiveSteps=Array.Empty<TutorialStepView>();Notify();Changed=null;
        }
    }
}
