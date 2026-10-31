using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Progression.Graphs;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    public enum GraphAssetRequirementKind { All, Any, Not, Resource, Signal, Time, NodeCompleted, BooleanProperty, IntegerProperty, Flag, Custom }
    [Serializable]
    public sealed class GraphRequirementEntry
    {
        public GraphAssetRequirementKind kind;
        public string key;
        public string amount="1";
        public bool lifetime;
        public long count=1;
        public double seconds;
        public GraphProgressScope progressScope=GraphProgressScope.Active;
        public GraphTimeScope timeScope=GraphTimeScope.RunElapsed;
        public bool expected=true;
        public int integer=1;
        public int[] children=Array.Empty<int>();
    }
    [Serializable]
    public sealed class GraphRequirementTree
    {
        [Tooltip("-1 means no requirement (true). Otherwise indexes entries.")]
        public int root=-1;
        public GraphRequirementEntry[] entries=Array.Empty<GraphRequirementEntry>();
        public GraphRequirement Compile()
        {
            if(entries==null)throw new ArgumentException("Missing requirement entries.");
            if(root==-1)return null;
            var visiting=new HashSet<int>();var compiled=new Dictionary<int,GraphRequirement>();
            GraphRequirement Visit(int index)
            {
                if(index<0||index>=entries.Length||entries[index]==null)throw new ArgumentException("Invalid requirement index.");
                if(compiled.TryGetValue(index,out var cached))return cached;
                if(!visiting.Add(index))throw new ArgumentException("Requirement tree cycle.");
                var e=entries[index];GraphRequirement result;
                switch(e.kind)
                {
                    case GraphAssetRequirementKind.All:result=GraphRequirements.All((e.children??throw new ArgumentException("Missing children.")).Select(Visit).ToArray());break;
                    case GraphAssetRequirementKind.Any:result=GraphRequirements.Any((e.children??throw new ArgumentException("Missing children.")).Select(Visit).ToArray());break;
                    case GraphAssetRequirementKind.Not:if(e.children==null||e.children.Length!=1)throw new ArgumentException("Not requires one child.");result=GraphRequirements.Not(Visit(e.children[0]));break;
                    case GraphAssetRequirementKind.Resource:result=GraphRequirements.Resource(e.key,BigDouble.Parse(e.amount),e.lifetime);break;
                    case GraphAssetRequirementKind.Signal:result=GraphRequirements.Signal(e.key,e.count,e.progressScope);break;
                    case GraphAssetRequirementKind.Time:result=GraphRequirements.Time(e.seconds,e.timeScope);break;
                    case GraphAssetRequirementKind.NodeCompleted:result=GraphRequirements.NodeCompleted(e.key);break;
                    case GraphAssetRequirementKind.BooleanProperty:result=GraphRequirements.State("bool:"+e.key+":"+e.expected,Expr.Equal(Expr.Property(new PropertyKey<bool>(e.key)),Expr.Constant(e.expected)));break;
                    case GraphAssetRequirementKind.IntegerProperty:result=GraphRequirements.State("int:"+e.key+":"+e.integer,Expr.AtLeast(Expr.Property(new PropertyKey<int>(e.key)),Expr.Constant(e.integer)));break;
                    case GraphAssetRequirementKind.Flag:result=GraphRequirements.Flag(e.key,e.expected);break;
                    case GraphAssetRequirementKind.Custom:result=GraphRequirements.Custom(e.key);break;
                    default:throw new ArgumentException("Unknown asset requirement kind.");
                }
                visiting.Remove(index);compiled.Add(index,result);return result;
            }
            return Visit(root);
        }
    }
    [Serializable]
    public sealed class GraphEffectEntry
    {
        public bool useExtension;
        public string extensionId;
        public RuleActionEntry action=new RuleActionEntry();
        public GraphEffect Compile()=>useExtension?GraphEffect.Custom(extensionId):GraphEffect.Native(ContentCompiler.CompileAction(action??throw new ArgumentException("Missing graph action.")));
    }
    [Serializable]
    public sealed class GraphNodeEntry
    {
        public string id;
        public string title;
        [TextArea] public string description;
        public GraphNodeKind kind=GraphNodeKind.Objective;
        public bool isEnd=true;
        public string[] prerequisites=Array.Empty<string>();
        public GraphPrerequisiteMode prerequisiteMode;
        public bool overrideCompletion;
        public GraphCompletionMode completion=GraphCompletionMode.Explicit;
        public GraphActivationMode activationMode;
        public GraphRequirementTree activation=new GraphRequirementTree();
        public GraphRequirementTree trigger=new GraphRequirementTree();
        public GraphEffectEntry[] onActivate=Array.Empty<GraphEffectEntry>();
        public GraphEffectEntry[] onComplete=Array.Empty<GraphEffectEntry>();
        internal GraphNodeDefinition Compile()=>new GraphNodeDefinition(id,kind,isEnd,prerequisites,prerequisiteMode,
            activation?.Compile(),trigger?.Compile(),overrideCompletion?completion:(GraphCompletionMode?)null,activationMode,
            GraphAsset.Effects(onActivate),GraphAsset.Effects(onComplete),title,description);
    }
    [CreateAssetMenu(menuName="Idle Hands/Event and Objective Graph")]
    public sealed class GraphAsset : ScriptableObject
    {
        public string id;
        public string contentVersion="1";
        public string title;
        public GraphNodeEntry[] nodes=Array.Empty<GraphNodeEntry>();
        public GraphRequirementTree activation=new GraphRequirementTree();
        public GraphActivationMode activationMode;
        public GraphEffectEntry[] onActivate=Array.Empty<GraphEffectEntry>();
        public GraphEffectEntry[] onComplete=Array.Empty<GraphEffectEntry>();
        public ResetPolicy prestigePolicy=ResetPolicy.Preserve;
        public bool autoRestart;
        [Min(0.001f)] public double restartCooldown=60;
        [Min(0.001f)] public double evaluationInterval=1;
        internal static IEnumerable<GraphEffect> Effects(GraphEffectEntry[] entries)=>(entries??throw new ArgumentException("Missing effect entries.")).Select(e=>(e??throw new ArgumentException("Null effect entry.")).Compile()).ToArray();
        public GraphDefinition Definition()=>new GraphDefinition(id,(nodes??throw new ArgumentException("Missing nodes.")).Select(n=>(n??throw new ArgumentException("Null node.")).Compile()),contentVersion,title,
            activation?.Compile(),activationMode,Effects(onActivate),Effects(onComplete),prestigePolicy,autoRestart?restartCooldown:(double?)null,evaluationInterval);
        public CompiledGraph Compile(GraphExtensions extensions=null)=>GraphCompiler.Compile(Definition(),extensions);
        public IReadOnlyList<string> Validate(GraphExtensions extensions=null)
        {try{Compile(extensions);return Array.Empty<string>();}catch(Exception error) when(error is ArgumentException||error is FormatException||error is ArithmeticException){return new[]{error.Message};}}
        [ContextMenu("Validate Graph")]
        private void ValidateGraph(){var errors=Validate();if(errors.Count==0)Debug.Log("Graph is valid: "+name,this);else Debug.LogError(string.Join("\n",errors),this);}
    }
}
