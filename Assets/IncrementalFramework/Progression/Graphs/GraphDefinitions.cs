using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;

namespace IncrementalFramework.Progression.Graphs
{
    public enum GraphNodeKind { Event, Objective }
    public enum GraphCompletionMode { Automatic, Explicit }
    public enum GraphActivationMode { Latched, Maintained }
    public enum GraphPrerequisiteMode { All, Any }
    public enum GraphNodeStatus { Dormant, Active, Ready, Suspended, Completed, Skipped }
    public enum GraphStatus { Dormant, Active, Suspended, Completed }
    public enum GraphProgressScope { Run, Active }
    public enum GraphTimeScope { Absolute, RunElapsed, ActiveElapsed }
    public enum GraphRequirementKind { All, Any, Not, State, Signal, Time, NodeCompleted, Resource, Flag, Custom }

    public sealed class GraphRequirement
    {
        public GraphRequirementKind Kind { get; }
        public string Key { get; }
        public double Seconds { get; }
        public long Count { get; }
        public BigDouble Amount { get; }
        public bool Lifetime { get; }
        public bool Expected { get; }
        public GraphProgressScope ProgressScope { get; }
        public GraphTimeScope TimeScope { get; }
        public Expression<bool> Expression { get; }
        public IReadOnlyList<GraphRequirement> Children { get; }
        internal GraphRequirement(GraphRequirementKind kind, string key = null, double seconds = 0, long count = 0,
            BigDouble amount = default, bool lifetime = false, bool expected = true, GraphProgressScope progress = GraphProgressScope.Run,
            GraphTimeScope time = GraphTimeScope.Absolute, Expression<bool> expression = null, params GraphRequirement[] children)
        {
            Kind=kind; Key=key; Seconds=seconds; Count=count; Amount=amount; Lifetime=lifetime; Expected=expected;
            ProgressScope=progress; TimeScope=time; Expression=expression;
            Children=Array.AsReadOnly((children ?? Array.Empty<GraphRequirement>()).ToArray());
        }
        internal IEnumerable<GraphRequirement> Walk() => new[]{this}.Concat(Children.SelectMany(c=>c.Walk()));
        internal string Signature => GraphCompiler.Pack(Kind.ToString(),Key,Seconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
            Count.ToString(),Amount.Mantissa.ToString("R",System.Globalization.CultureInfo.InvariantCulture),Amount.Exponent.ToString(),
            Lifetime.ToString(),Expected.ToString(),ProgressScope.ToString(),TimeScope.ToString(),GraphCompiler.Pack(Children.Select(c=>c.Signature).ToArray()));
    }
    public static class GraphRequirements
    {
        public static GraphRequirement All(params GraphRequirement[] children) => Composite(GraphRequirementKind.All,children);
        public static GraphRequirement Any(params GraphRequirement[] children) => Composite(GraphRequirementKind.Any,children);
        public static GraphRequirement Not(GraphRequirement child) => Composite(GraphRequirementKind.Not,new[]{child});
        private static GraphRequirement Composite(GraphRequirementKind kind, GraphRequirement[] children)
        { if(children==null || children.Any(c=>c==null))throw new ArgumentException("Null requirement.");return new GraphRequirement(kind,children:children); }
        /// <summary>The stable key plus graph content version identify code-authored expression semantics.</summary>
        public static GraphRequirement State(string key, Expression<bool> expression) => new GraphRequirement(GraphRequirementKind.State,GraphCompiler.Id(key),expression:expression??throw new ArgumentNullException(nameof(expression)));
        public static GraphRequirement Signal(string key,long count=1,GraphProgressScope scope=GraphProgressScope.Active)
        { if(count<1 || !Enum.IsDefined(typeof(GraphProgressScope),scope))throw new ArgumentOutOfRangeException(nameof(count));return new GraphRequirement(GraphRequirementKind.Signal,GraphCompiler.Id(key),count:count,progress:scope); }
        public static GraphRequirement Time(double seconds,GraphTimeScope scope=GraphTimeScope.RunElapsed)
        { if(!GraphCompiler.Finite(seconds)||seconds<0||!Enum.IsDefined(typeof(GraphTimeScope),scope))throw new ArgumentOutOfRangeException(nameof(seconds));return new GraphRequirement(GraphRequirementKind.Time,seconds:seconds,time:scope); }
        public static GraphRequirement NodeCompleted(string nodeId) => new GraphRequirement(GraphRequirementKind.NodeCompleted,GraphCompiler.Id(nodeId));
        public static GraphRequirement Resource(string id,BigDouble amount,bool lifetime=false)
        { Number.NonNegative(amount,nameof(amount));return new GraphRequirement(GraphRequirementKind.Resource,GraphCompiler.Id(id),amount:amount,lifetime:lifetime); }
        public static GraphRequirement Flag(string id,bool expected=true) => new GraphRequirement(GraphRequirementKind.Flag,GraphCompiler.Id(id),expected:expected);
        public static GraphRequirement Custom(string id) => new GraphRequirement(GraphRequirementKind.Custom,GraphCompiler.Id(id));
    }

    public interface IGraphRequirementHandler
    {
        IReadOnlyCollection<PropertyReference> Dependencies { get; }
        bool Evaluate(GraphEvaluationContext context);
        double? NextEvaluationTime(GraphEvaluationContext context);
    }
    public interface IGraphEffectHandler
    { void Stage(GraphEvaluationContext context, SimulationTransaction transaction); }
    public sealed class GraphExtensions
    {
        private readonly Dictionary<string,IGraphRequirementHandler> requirements=new Dictionary<string,IGraphRequirementHandler>(StringComparer.Ordinal);
        private readonly Dictionary<string,IGraphEffectHandler> effects=new Dictionary<string,IGraphEffectHandler>(StringComparer.Ordinal);
        public ExtensionRegistry Expressions { get; }=new ExtensionRegistry();
        public GraphExtensions Requirement(string id,IGraphRequirementHandler handler)
        { requirements.Add(GraphCompiler.Id(id),handler??throw new ArgumentNullException(nameof(handler)));return this; }
        public GraphExtensions Effect(string id,IGraphEffectHandler handler)
        { effects.Add(GraphCompiler.Id(id),handler??throw new ArgumentNullException(nameof(handler)));return this; }
        internal IGraphRequirementHandler Requirement(string id)=>requirements.TryGetValue(id,out var result)?result:throw new ArgumentException("Missing graph requirement: "+id);
        internal IGraphEffectHandler Effect(string id)=>effects.TryGetValue(id,out var result)?result:throw new ArgumentException("Missing graph effect: "+id);
    }
    public sealed class GraphEffect
    {
        public RuleAction Action { get; }
        public string ExtensionId { get; }
        private GraphEffect(RuleAction action,string id){Action=action;ExtensionId=id;}
        public static GraphEffect Native(RuleAction action)=>new GraphEffect(action??throw new ArgumentNullException(nameof(action)),null);
        public static GraphEffect Custom(string id)=>new GraphEffect(null,GraphCompiler.Id(id));
        internal string Signature
        {
            get
            {
                if(Action==null)return GraphCompiler.Pack("custom",ExtensionId);
                string Amount(BigDouble value)=>value.Mantissa.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+value.Exponent;
                if(Action is GrantResourceAction g)return GraphCompiler.Pack("grant",g.ResourceId,Amount(g.Amount));
                if(Action is SpendResourceAction s)return GraphCompiler.Pack("spend",s.ResourceId,Amount(s.Amount));
                if(Action is SetFlagAction f)return GraphCompiler.Pack("flag",f.FlagId,f.Value.ToString());
                if(Action is UnlockProducerAction p)return GraphCompiler.Pack("producer",p.ProducerId);
                if(Action is UnlockUpgradeAction u)return GraphCompiler.Pack("upgrade",u.UpgradeId);
                if(Action is SetModifierAction m)return GraphCompiler.Pack("modifier",m.ModifierId,m.Enabled.ToString());
                if(Action is ScheduleGrantResourceAction d)return GraphCompiler.Pack("schedule",d.ResourceId,Amount(d.Amount),d.Delay.ToString("R",System.Globalization.CultureInfo.InvariantCulture),d.Priority.ToString());
                throw new ArgumentException("Unsupported graph action.");
            }
        }
    }
    public sealed class GraphNodeDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public GraphNodeKind Kind { get; }
        public GraphCompletionMode Completion { get; }
        public GraphActivationMode ActivationMode { get; }
        public GraphPrerequisiteMode PrerequisiteMode { get; }
        public IReadOnlyList<string> Prerequisites { get; }
        public GraphRequirement Activation { get; }
        public GraphRequirement Trigger { get; }
        public IReadOnlyList<GraphEffect> OnActivate { get; }
        public IReadOnlyList<GraphEffect> OnComplete { get; }
        public bool IsEnd { get; }
        public GraphNodeDefinition(string id, GraphNodeKind kind=GraphNodeKind.Objective, bool isEnd=false,
            IEnumerable<string> prerequisites=null, GraphPrerequisiteMode prerequisiteMode=GraphPrerequisiteMode.All,
            GraphRequirement activation=null, GraphRequirement trigger=null, GraphCompletionMode? completion=null,
            GraphActivationMode activationMode=GraphActivationMode.Latched, IEnumerable<GraphEffect> onActivate=null,
            IEnumerable<GraphEffect> onComplete=null, string title=null,string description=null)
        {
            Id=GraphCompiler.Id(id);Title=title??id;Description=description??"";Kind=kind;IsEnd=isEnd;
            Completion=completion??(kind==GraphNodeKind.Event?GraphCompletionMode.Automatic:GraphCompletionMode.Explicit);
            ActivationMode=activationMode;PrerequisiteMode=prerequisiteMode;
            Prerequisites=Array.AsReadOnly((prerequisites??Array.Empty<string>()).ToArray());
            Activation=activation;Trigger=trigger;
            OnActivate=Array.AsReadOnly((onActivate??Array.Empty<GraphEffect>()).ToArray());
            OnComplete=Array.AsReadOnly((onComplete??Array.Empty<GraphEffect>()).ToArray());
        }
    }
    public sealed class GraphDefinition
    {
        public string Id { get; }
        public string ContentVersion { get; }
        public string Title { get; }
        public IReadOnlyList<GraphNodeDefinition> Nodes { get; }
        public GraphRequirement Activation { get; }
        public GraphActivationMode ActivationMode { get; }
        public IReadOnlyList<GraphEffect> OnActivate { get; }
        public IReadOnlyList<GraphEffect> OnComplete { get; }
        public ResetPolicy PrestigePolicy { get; }
        public double? RestartCooldown { get; }
        public double EvaluationInterval { get; }
        public GraphDefinition(string id,IEnumerable<GraphNodeDefinition> nodes,string contentVersion="1",string title=null,
            GraphRequirement activation=null,GraphActivationMode activationMode=GraphActivationMode.Latched,
            IEnumerable<GraphEffect> onActivate=null,IEnumerable<GraphEffect> onComplete=null,
            ResetPolicy prestigePolicy=ResetPolicy.Preserve,double? restartCooldown=null,double evaluationInterval=1)
        {
            Id=GraphCompiler.Id(id);ContentVersion=GraphCompiler.Id(contentVersion);Title=title??id;
            Nodes=Array.AsReadOnly((nodes??throw new ArgumentNullException(nameof(nodes))).ToArray());
            Activation=activation;ActivationMode=activationMode;
            OnActivate=Array.AsReadOnly((onActivate??Array.Empty<GraphEffect>()).ToArray());
            OnComplete=Array.AsReadOnly((onComplete??Array.Empty<GraphEffect>()).ToArray());
            PrestigePolicy=prestigePolicy;RestartCooldown=restartCooldown;EvaluationInterval=evaluationInterval;
        }
    }
    public sealed class CompiledGraph
    {
        public GraphDefinition Definition { get; }
        public IReadOnlyDictionary<string,GraphNodeDefinition> Nodes { get; }
        public string Fingerprint { get; }
        internal CompiledGraph(GraphDefinition definition,string hash)
        {Definition=definition;Nodes=new System.Collections.ObjectModel.ReadOnlyDictionary<string,GraphNodeDefinition>(definition.Nodes.ToDictionary(n=>n.Id,StringComparer.Ordinal));Fingerprint=hash;}
    }
    public static class GraphCompiler
    {
        internal static string Id(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("ID must not be empty.") : value;
        internal static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        internal static string Pack(params string[] values)=>string.Concat(values.Select(v=>(v??"").Length+":"+(v??"")));
        public static CompiledGraph Compile(GraphDefinition definition,GraphExtensions extensions=null)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            extensions=extensions??new GraphExtensions();
            if(definition.Nodes.Count==0||definition.Nodes.Count>10000||definition.Nodes.Any(n=>n==null)||definition.Nodes.Select(n=>n.Id).Distinct(StringComparer.Ordinal).Count()!=definition.Nodes.Count)
                throw new ArgumentException("Graph requires one or more uniquely identified nodes (maximum 10000).");
            if(!Finite(definition.EvaluationInterval)||definition.EvaluationInterval<=0||definition.RestartCooldown.HasValue&&(!Finite(definition.RestartCooldown.Value)||definition.RestartCooldown.Value<=0))throw new ArgumentException("Invalid graph timing.");
            if(definition.PrestigePolicy!=ResetPolicy.Preserve&&definition.PrestigePolicy!=ResetPolicy.Reset||!Enum.IsDefined(typeof(GraphActivationMode),definition.ActivationMode))throw new ArgumentException("Invalid graph policy.");
            var nodes=definition.Nodes.ToDictionary(n=>n.Id,StringComparer.Ordinal);
            void Requirements(GraphRequirement requirement)
            {
                foreach(var r in requirement?.Walk()??Enumerable.Empty<GraphRequirement>())
                {
                    if(r.Kind==GraphRequirementKind.NodeCompleted&&!nodes.ContainsKey(r.Key))throw new ArgumentException("Unknown completion reference: "+r.Key);
                    if(r.Kind==GraphRequirementKind.Custom)_=extensions.Requirement(r.Key);
                    if(r.Kind==GraphRequirementKind.State)foreach(var id in r.Expression.RequiredExtensions)if(!extensions.Expressions.Contains(id))throw new ArgumentException("Missing expression extension: "+id);
                }
            }
            void Effects(IEnumerable<GraphEffect> effects)
            {foreach(var effect in effects){if(effect==null)throw new ArgumentException("Null graph effect.");_=effect.Signature;if(effect.ExtensionId!=null)_=extensions.Effect(effect.ExtensionId);}}
            Requirements(definition.Activation);Effects(definition.OnActivate);Effects(definition.OnComplete);
            foreach(var n in definition.Nodes)
            {
                if(!Enum.IsDefined(typeof(GraphNodeKind),n.Kind)||!Enum.IsDefined(typeof(GraphCompletionMode),n.Completion)||!Enum.IsDefined(typeof(GraphActivationMode),n.ActivationMode)||!Enum.IsDefined(typeof(GraphPrerequisiteMode),n.PrerequisiteMode))throw new ArgumentException("Invalid node policy.");
                if(n.Prerequisites.Any(p=>p==null||!nodes.ContainsKey(p))||n.Prerequisites.Distinct().Count()!=n.Prerequisites.Count)throw new ArgumentException("Unknown or duplicate prerequisite: "+n.Id);
                Requirements(n.Activation);Requirements(n.Trigger);Effects(n.OnActivate);Effects(n.OnComplete);
            }
            IEnumerable<string> Dependencies(GraphNodeDefinition n)=>n.Prerequisites.Concat((n.Activation?.Walk()??Enumerable.Empty<GraphRequirement>()).Concat(n.Trigger?.Walk()??Enumerable.Empty<GraphRequirement>()).Where(r=>r.Kind==GraphRequirementKind.NodeCompleted).Select(r=>r.Key));
            var visited=new HashSet<string>();var visiting=new HashSet<string>();
            void Visit(string id)
            {
                if(visited.Contains(id))return;if(!visiting.Add(id))throw new ArgumentException("Graph cycle at "+id);
                var n=nodes[id];
                foreach(var p in Dependencies(n))Visit(p);
                visiting.Remove(id);visited.Add(id);
            }
            foreach(var n in definition.Nodes)Visit(n.Id);
            var canEnd=new HashSet<string>(definition.Nodes.Where(n=>n.IsEnd).Select(n=>n.Id));
            bool changed;do{changed=false;foreach(var n in definition.Nodes)if(canEnd.Contains(n.Id))foreach(var p in Dependencies(n))changed|=canEnd.Add(p);}while(changed);
            if(canEnd.Count!=nodes.Count)throw new ArgumentException("Every node must have a path to an end node.");
            if(definition.Nodes.Any(n=>n.IsEnd&&definition.Nodes.Any(other=>Dependencies(other).Contains(n.Id))))throw new ArgumentException("End nodes cannot have successors.");
            string EffectsKey(IEnumerable<GraphEffect> effects)=>Pack(effects.Select(e=>e.Signature).ToArray());
            var signature=Pack(definition.Id,definition.ContentVersion,definition.Title,definition.Activation?.Signature,definition.ActivationMode.ToString(),definition.PrestigePolicy.ToString(),
                definition.RestartCooldown?.ToString("R",System.Globalization.CultureInfo.InvariantCulture),definition.EvaluationInterval.ToString("R",System.Globalization.CultureInfo.InvariantCulture),EffectsKey(definition.OnActivate),EffectsKey(definition.OnComplete),
                Pack(definition.Nodes.OrderBy(n=>n.Id,StringComparer.Ordinal).Select(n=>Pack(n.Id,n.Title,n.Description,n.Kind.ToString(),n.Completion.ToString(),n.ActivationMode.ToString(),n.PrerequisiteMode.ToString(),Pack(n.Prerequisites.ToArray()),n.Activation?.Signature,n.Trigger?.Signature,n.IsEnd.ToString(),EffectsKey(n.OnActivate),EffectsKey(n.OnComplete))).ToArray()));
            using var hash=SHA256.Create();return new CompiledGraph(definition,BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(signature))).Replace("-",""));
        }
    }
}
