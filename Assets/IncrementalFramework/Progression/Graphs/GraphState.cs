using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;

namespace IncrementalFramework.Progression.Graphs
{
    // Detached snapshot DTOs. Runtime imports/exports copy these; callers never receive live state.
    public sealed class GraphNodeState
    {
        public string Id { get; set; }
        public GraphNodeStatus Status { get; set; }
        public bool Activated { get; set; }
        public double ActiveSeconds { get; set; }
        public double? ActiveSince { get; set; }
        public double? NextEvaluation { get; set; }
        public string LastError { get; set; }
        public Dictionary<string,long> Signals { get; set; }=new Dictionary<string,long>(StringComparer.Ordinal);
        internal GraphNodeState Copy()=>new GraphNodeState{Id=Id,Status=Status,Activated=Activated,ActiveSeconds=ActiveSeconds,ActiveSince=ActiveSince,NextEvaluation=NextEvaluation,LastError=LastError,Signals=new Dictionary<string,long>(Signals,StringComparer.Ordinal)};
    }
    public sealed class GraphInstanceState
    {
        public string Id { get; set; }
        public string DefinitionId { get; set; }
        public string OwnerId { get; set; }
        public long Run { get; set; }
        public GraphStatus Status { get; set; }
        public bool Activated { get; set; }
        public double StartedAt { get; set; }
        public double ActiveSeconds { get; set; }
        public double? ActiveSince { get; set; }
        public double? NextEvaluation { get; set; }
        public double? RestartAt { get; set; }
        public string Ending { get; set; }
        public string PreviousEnding { get; set; }
        public string LastError { get; set; }
        public Dictionary<string,long> Signals { get; set; }=new Dictionary<string,long>(StringComparer.Ordinal);
        public Dictionary<string,long> ActiveSignals { get; set; }=new Dictionary<string,long>(StringComparer.Ordinal);
        public List<GraphNodeState> Nodes { get; set; }=new List<GraphNodeState>();
        internal GraphInstanceState Copy()=>new GraphInstanceState{Id=Id,DefinitionId=DefinitionId,OwnerId=OwnerId,Run=Run,Status=Status,Activated=Activated,StartedAt=StartedAt,ActiveSeconds=ActiveSeconds,ActiveSince=ActiveSince,NextEvaluation=NextEvaluation,RestartAt=RestartAt,Ending=Ending,PreviousEnding=PreviousEnding,LastError=LastError,Signals=new Dictionary<string,long>(Signals,StringComparer.Ordinal),ActiveSignals=new Dictionary<string,long>(ActiveSignals,StringComparer.Ordinal),Nodes=Nodes.Select(n=>n.Copy()).ToList()};
    }
    public sealed class GraphSnapshot
    {
        public Dictionary<string,string> Definitions { get; set; }=new Dictionary<string,string>(StringComparer.Ordinal);
        public double? InvalidatedAt { get; set; }
        public List<GraphInstanceState> Instances { get; set; }=new List<GraphInstanceState>();
        internal GraphSnapshot Copy()=>new GraphSnapshot{Definitions=new Dictionary<string,string>(Definitions,StringComparer.Ordinal),InvalidatedAt=InvalidatedAt,Instances=Instances.Select(i=>i.Copy()).ToList()};
    }
    internal struct GraphCell { public GraphSnapshot Value; }
    public sealed class StartGraphCommand : ISimulationCommand
    {
        public string InstanceId { get; } public string DefinitionId { get; } public string OwnerId { get; }
        public StartGraphCommand(string instanceId,string definitionId,string ownerId){InstanceId=instanceId;DefinitionId=definitionId;OwnerId=ownerId;}
    }
    public sealed class CompleteGraphNodeCommand : ISimulationCommand
    {
        public string InstanceId { get; } public string NodeId { get; } public long Run { get; }
        public CompleteGraphNodeCommand(string instanceId,string nodeId,long run){InstanceId=instanceId;NodeId=nodeId;Run=run;}
    }
    public sealed class RestartGraphCommand : ISimulationCommand
    {
        public string InstanceId { get; } public long Run { get; }
        public RestartGraphCommand(string instanceId,long run){InstanceId=instanceId;Run=run;}
    }
    public sealed class PublishGraphSignalCommand : ISimulationCommand
    {
        public string OwnerId { get; } public string Key { get; } public long Count { get; }
        public PublishGraphSignalCommand(string ownerId,string key,long count=1){OwnerId=ownerId;Key=key;Count=count;}
    }
    public sealed class GraphWorkCommand : ISimulationCommand
    {
        internal GraphWorkCommand() {}
        internal string InstanceId; internal string NodeId;
    }
    public sealed class GraphErrorCommand : ISimulationCommand
    { internal GraphErrorCommand() {} internal string InstanceId;internal string NodeId;internal string Error; }
}
