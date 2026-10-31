using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;

namespace IncrementalFramework.Progression.Graphs
{
    /// <summary>Read-only facts for requirement/effect extensions. Simulation mutation belongs in the supplied transaction.</summary>
    public sealed class GraphEvaluationContext
    {
        private readonly Simulation simulation;
        private readonly GraphInstanceState instance;
        private readonly GraphNodeState node;
        private readonly GraphExtensions extensions;
        public double Time=>simulation.Time;
        public string InstanceId=>instance.Id;
        public string OwnerId=>instance.OwnerId;
        public string NodeId=>node?.Id;
        public long Run=>instance.Run;
        public double RunElapsed=>Time-instance.StartedAt;
        public double ActiveElapsed=>node==null?instance.ActiveSeconds+(instance.ActiveSince.HasValue?Time-instance.ActiveSince.Value:0):node.ActiveSeconds+(node.ActiveSince.HasValue?Time-node.ActiveSince.Value:0);
        public SimulationReadModel State=>simulation.GetReadModel();
        public T Read<T>(string entityId,PropertyKey<T> property)=>simulation.Entities.Find(entityId).GetValue(property).Current;
        public long SignalCount(string key,GraphProgressScope scope)
        {var values=scope==GraphProgressScope.Run?instance.Signals:node==null?instance.ActiveSignals:node.Signals;return values.TryGetValue(key,out var count)?count:0;}
        public bool Completed(string id)=>instance.Nodes.Single(n=>n.Id==id).Status==GraphNodeStatus.Completed;
        internal GraphEvaluationContext(Simulation simulation,GraphInstanceState instance,GraphNodeState node,GraphExtensions extensions)
        {this.simulation=simulation;this.instance=instance;this.node=node;this.extensions=extensions;}
        internal EvaluationContext Expressions()=>new EvaluationContext(simulation.Entities.Find(OwnerId),extensions.Expressions){Time=Time};
        internal bool Evaluate(GraphRequirement requirement)
        {
            if(requirement==null)return true;
            switch(requirement.Kind)
            {
                case GraphRequirementKind.All:return requirement.Children.All(Evaluate);
                case GraphRequirementKind.Any:return requirement.Children.Any(Evaluate);
                case GraphRequirementKind.Not:return !Evaluate(requirement.Children[0]);
                case GraphRequirementKind.State:return requirement.Expression.Evaluate(Expressions());
                case GraphRequirementKind.Signal:return SignalCount(requirement.Key,requirement.ProgressScope)>=requirement.Count;
                case GraphRequirementKind.Time:return (requirement.TimeScope==GraphTimeScope.Absolute?Time:requirement.TimeScope==GraphTimeScope.RunElapsed?RunElapsed:ActiveElapsed)>=requirement.Seconds;
                case GraphRequirementKind.NodeCompleted:return Completed(requirement.Key);
                case GraphRequirementKind.Resource:var resource=simulation.GetResource(requirement.Key);return (requirement.Lifetime?resource.LifetimeEarned:resource.Balance)>=requirement.Amount;
                case GraphRequirementKind.Flag:return simulation.GetFlag(requirement.Key)==requirement.Expected;
                case GraphRequirementKind.Custom:return extensions.Requirement(requirement.Key).Evaluate(this);
                default:throw new ArgumentException("Unknown requirement.");
            }
        }
        internal double? Next(GraphRequirement requirement,double interval)
        {
            if(requirement==null)return null;
            double? best=null;
            foreach(var r in requirement.Walk())
            {
                double? next=null;
                if(r.Kind==GraphRequirementKind.State)next=Time+interval;
                else if(r.Kind==GraphRequirementKind.Custom)next=extensions.Requirement(r.Key).NextEvaluationTime(this)??Time+interval;
                else if(r.Kind==GraphRequirementKind.Time)
                {
                    double current=r.TimeScope==GraphTimeScope.Absolute?Time:r.TimeScope==GraphTimeScope.RunElapsed?RunElapsed:ActiveElapsed;
                    bool running=r.TimeScope!=GraphTimeScope.ActiveElapsed||(node==null?instance.ActiveSince.HasValue:node.ActiveSince.HasValue);
                    if(r.Seconds>current&&running)next=Time+(r.Seconds-current);
                }
                else if(r.Kind==GraphRequirementKind.Resource)
                {
                    var resource=simulation.GetResource(r.Key);var current=r.Lifetime?resource.LifetimeEarned:resource.Balance;
                    if(current<r.Amount&&resource.Rate>BigDouble.Zero&&(!resource.Cap.HasValue||resource.Cap.Value>=r.Amount||r.Lifetime))
                        next=Time+Number.RatioToDouble(r.Amount-current,resource.Rate);
                }
                if(next.HasValue)
                {
                    if(!GraphCompiler.Finite(next.Value))continue;
                    if(next.Value<=Time)next=NextDouble(Time);
                    if(!best.HasValue||next.Value<best.Value)best=next;
                }
            }
            return best;
        }
        internal static double NextDouble(double value)=>BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value)+1);
    }
}
