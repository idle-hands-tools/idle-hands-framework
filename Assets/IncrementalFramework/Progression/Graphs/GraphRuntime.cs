using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.Progression.Graphs
{
    public sealed class GraphRuntime : ISimulationWorkSource, IPrestigeResetParticipant,
        ICommandHandler<StartGraphCommand>,ICommandHandler<CompleteGraphNodeCommand>,ICommandHandler<RestartGraphCommand>,
        ICommandHandler<PublishGraphSignalCommand>,ICommandHandler<GraphWorkCommand>,ICommandHandler<GraphErrorCommand>,ICommandFailureHandler<CompleteGraphNodeCommand>
    {
        private readonly IReadOnlyDictionary<string,CompiledGraph> graphs;
        private readonly GraphExtensions extensions;
        private readonly DenseStateStore<GraphCell> store=new DenseStateStore<GraphCell>();
        private Simulation simulation;
        private double? invalidatedAt;
        private bool effectsStaged;
        private readonly Dictionary<string,GraphNodeView> views=new Dictionary<string,GraphNodeView>(StringComparer.Ordinal);
        public string Id { get; }
        public ResetPolicy Policy=>ResetPolicy.Transform;
        public event Action Changed;
        public EntityContext ViewWorld { get; }=new EntityContext();
        private GraphSnapshot World=>store[0].Value;
        public GraphRuntime(IEnumerable<GraphDefinition> definitions,GraphExtensions extensions=null,string id="framework.graphs")
        {
            Id=GraphCompiler.Id(id);this.extensions=extensions??new GraphExtensions();
            var compiled=(definitions??throw new ArgumentNullException(nameof(definitions))).Select(d=>GraphCompiler.Compile(d,this.extensions)).ToArray();
            graphs=new System.Collections.ObjectModel.ReadOnlyDictionary<string,CompiledGraph>(compiled.ToDictionary(c=>c.Definition.Id,StringComparer.Ordinal));
            store.Register("world",new GraphCell{Value=new GraphSnapshot{Definitions=compiled.ToDictionary(c=>c.Definition.Id,c=>c.Fingerprint,StringComparer.Ordinal)}});
        }
        public void Register(ModuleBuilder builder)
        {
            foreach(var group in graphs.Values.SelectMany(g=>g.Definition.OnActivate.Concat(g.Definition.OnComplete).Concat(g.Definition.Nodes.SelectMany(n=>n.OnActivate.Concat(n.OnComplete))))
                .Select(e=>e.Action).OfType<IncrementalFramework.Economy.SetModifierAction>().Where(a=>a.Definition!=null).GroupBy(a=>a.ModifierId,StringComparer.Ordinal))
            {
                var first=group.First().Definition;
                if(group.Any(a=>a.Definition.Target!=first.Target||a.Definition.TargetId!=first.TargetId||a.Definition.Value!=first.Value||a.Definition.Kind!=first.Kind||a.Definition.Priority!=first.Priority))throw new ArgumentException("Conflicting graph modifier definitions.");
                builder.Modifier(first);
            }
            builder.State(Id,store);builder.Command<StartGraphCommand>(this);builder.Command<CompleteGraphNodeCommand>(this);
            builder.Command<RestartGraphCommand>(this);builder.Command<PublishGraphSignalCommand>(this);
            builder.Command<GraphWorkCommand>(this);builder.Command<GraphErrorCommand>(this);builder.Reset(this);
            builder.Configure(s=>
            {
                if(simulation!=null)throw new InvalidOperationException("Graph runtime already attached.");
                simulation=s;ValidateContent(s);s.RegisterWorkSource(this);
                s.Entities.ValueChanged+=OnValueChanged;s.Entities.Added+=OnEntityChanged;s.Entities.Removed+=OnEntityChanged;
                s.Changed+=_=>PublishViews();
            });
        }
        private void ValidateContent(Simulation s)
        {
            var transaction=new SimulationTransaction(s.Content);
            foreach(var graph in graphs.Values)
            {
                var d=graph.Definition;
                foreach(var effect in d.OnActivate.Concat(d.OnComplete).Concat(d.Nodes.SelectMany(n=>n.OnActivate.Concat(n.OnComplete))))if(effect.Action!=null)transaction.Stage(effect.Action);
                foreach(var requirement in new[]{d.Activation}.Concat(d.Nodes.SelectMany(n=>new[]{n.Activation,n.Trigger})))
                    foreach(var r in requirement?.Walk()??Enumerable.Empty<GraphRequirement>())
                    {if(r.Kind==GraphRequirementKind.Resource)s.Content.ResourceIndex(r.Key);if(r.Kind==GraphRequirementKind.Flag)s.Content.FlagIndex(r.Key);}
            }
        }
        private void OnValueChanged(Value value,object before,object after)=>Invalidate(simulation.Time);
        private void OnEntityChanged(EntityInstance entity)=>Invalidate(simulation.Time);
        public void Invalidate(double time)
        {
            if(!GraphCompiler.Finite(time)||time<0||simulation!=null&&time!=simulation.Time)throw new ArgumentOutOfRangeException(nameof(time));
            if(World.Instances.Any(i=>i.Status!=GraphStatus.Completed))invalidatedAt=invalidatedAt.HasValue?Math.Min(invalidatedAt.Value,time):time;
        }
        public double? NextTime
        {
            get
            {
                double? next=invalidatedAt;
                void Consider(double? value){if(value.HasValue&&(!next.HasValue||value.Value<next.Value))next=value;}
                foreach(var i in World.Instances)
                {Consider(i.NextEvaluation);Consider(i.RestartAt);foreach(var n in i.Nodes)Consider(n.NextEvaluation);}
                return next;
            }
        }
        public void Process(SimulationWorkContext context)
        {
            var command=new GraphWorkCommand();
            if(!invalidatedAt.HasValue)
            {
                var candidates=World.Instances.SelectMany(i=>new[]{(Time:i.NextEvaluation,Instance:i.Id,Node:(string)null),(Time:i.RestartAt,Instance:i.Id,Node:(string)null)}
                    .Concat(i.Nodes.Select(n=>(Time:n.NextEvaluation,Instance:i.Id,Node:n.Id))));
                var work=candidates.Where(c=>c.Time.HasValue&&c.Time.Value<=context.Time).OrderBy(c=>c.Time.Value).ThenBy(c=>c.Instance,StringComparer.Ordinal).ThenBy(c=>c.Node,StringComparer.Ordinal).First();
                command.InstanceId=work.Instance;command.NodeId=work.Node;
            }
            CommandResult result;
            try {result=context.Execute(command);}
            catch(Exception error) when(error is ArgumentException||error is ArithmeticException||error is InvalidOperationException)
            {result=CommandResult.Fail(error.Message);}
            if(result.Success&&command.InstanceId==null)invalidatedAt=null;
            if(!result.Success&&command.InstanceId!=null)
                context.Execute(new GraphErrorCommand{InstanceId=command.InstanceId,NodeId=command.NodeId,Error=result.Error});
            PublishViews();
        }
        private GraphEvaluationContext Context(GraphInstanceState instance,GraphNodeState node=null)=>new GraphEvaluationContext(simulation,instance,node,extensions);
        private void Save(GraphSnapshot world,SimulationTransaction transaction)=>transaction.Set(store,0,new GraphCell{Value=world});
        private static void Wake(GraphInstanceState instance,double time)
        {
            if(instance.Status==GraphStatus.Completed)return;
            instance.NextEvaluation=time;
            foreach(var n in instance.Nodes)if(n.Status!=GraphNodeStatus.Completed&&n.Status!=GraphNodeStatus.Skipped)n.NextEvaluation=time;
        }
        private static void Stop(GraphNodeState node,double time)
        {if(node.ActiveSince.HasValue){node.ActiveSeconds+=time-node.ActiveSince.Value;node.ActiveSince=null;}}
        private static void Stop(GraphInstanceState instance,double time)
        {if(instance.ActiveSince.HasValue){instance.ActiveSeconds+=time-instance.ActiveSince.Value;instance.ActiveSince=null;}}
        private GraphInstanceState Fresh(string instanceId,string definitionId,string ownerId,long run,string previous)
        {
            var d=graphs[definitionId].Definition;
            return new GraphInstanceState{Id=instanceId,DefinitionId=definitionId,OwnerId=ownerId,Run=run,StartedAt=simulation.Time,PreviousEnding=previous,NextEvaluation=simulation.Time,
                Nodes=d.Nodes.OrderBy(n=>n.Id,StringComparer.Ordinal).Select(n=>new GraphNodeState{Id=n.Id,NextEvaluation=simulation.Time}).ToList()};
        }
        private void Stage(IEnumerable<GraphEffect> effects,GraphInstanceState instance,GraphNodeState node,SimulationTransaction transaction)
        {
            foreach(var effect in effects)
            {
                effectsStaged=true;
                if(effect.Action!=null)transaction.Stage(effect.Action);
                else extensions.Effect(effect.ExtensionId).Stage(Context(instance,node),transaction);
            }
        }
        private bool Prerequisites(GraphInstanceState instance,GraphNodeDefinition definition)
        {
            if(definition.Prerequisites.Count==0)return true;
            bool Done(string id)=>instance.Nodes.Single(n=>n.Id==id).Status==GraphNodeStatus.Completed;
            return definition.PrerequisiteMode==GraphPrerequisiteMode.All?definition.Prerequisites.All(Done):definition.Prerequisites.Any(Done);
        }
        private bool GraphEligible(GraphInstanceState instance,GraphDefinition definition)=>instance.Status!=GraphStatus.Completed&&
            (instance.Activated&&definition.ActivationMode==GraphActivationMode.Latched||Context(instance).Evaluate(definition.Activation));
        private bool NodeEligible(GraphInstanceState instance,GraphNodeState node,GraphNodeDefinition definition)=>Prerequisites(instance,definition)&&
            (node.Activated&&definition.ActivationMode==GraphActivationMode.Latched||Context(instance,node).Evaluate(definition.Activation));
        private static double? Earlier(double? a,double? b)=>!a.HasValue?b:!b.HasValue?a:Math.Min(a.Value,b.Value);
        private double Later(double interval)
        {double next=simulation.Time+interval;if(!GraphCompiler.Finite(next))throw new ArithmeticException("Graph deadline overflow.");return next>simulation.Time?next:GraphEvaluationContext.NextDouble(simulation.Time);}

        public CommandResult Build(StartGraphCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {try{return BuildCommand(command,model,transaction);}catch(Exception error) when(error is ArgumentException||error is ArithmeticException||error is InvalidOperationException){return CommandResult.Fail(error.Message);}}
        private CommandResult BuildCommand(StartGraphCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {
            if(string.IsNullOrWhiteSpace(command.InstanceId)||!graphs.ContainsKey(command.DefinitionId??"")||!simulation.Entities.Entities.ContainsKey(command.OwnerId??""))return CommandResult.Fail("Invalid graph, instance, or owner.");
            if(World.Instances.Count>=10000||World.Instances.Any(i=>i.Id==command.InstanceId))return CommandResult.Fail("Graph instance already exists or capacity reached.");
            ValidateOwner(graphs[command.DefinitionId].Definition,command.OwnerId);
            var world=World.Copy();world.Instances.Add(Fresh(command.InstanceId,command.DefinitionId,command.OwnerId,1,null));Save(world,transaction);return CommandResult.Ok();
        }
        private void ValidateOwner(GraphDefinition definition,string ownerId)
        {
            var owner=simulation.Entities.Find(ownerId);
            foreach(var requirement in new[]{definition.Activation}.Concat(definition.Nodes.SelectMany(n=>new[]{n.Activation,n.Trigger})))
                foreach(var r in requirement?.Walk()??Enumerable.Empty<GraphRequirement>())
                    foreach(var reference in r.Kind==GraphRequirementKind.State?r.Expression.References:r.Kind==GraphRequirementKind.Custom?extensions.Requirement(r.Key).Dependencies??Array.Empty<PropertyReference>():Array.Empty<PropertyReference>())
                        if(reference.Scope==EntityScope.Self&&(!owner.Values.TryGetValue(reference.Name,out var value)||value.DataType!=reference.Type))throw new ArgumentException("Owner has no compatible property: "+reference.Name);
        }
        public CommandResult Build(RestartGraphCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {try{return BuildCommand(command,model,transaction);}catch(Exception error) when(error is ArgumentException||error is ArithmeticException||error is InvalidOperationException){return CommandResult.Fail(error.Message);}}
        private CommandResult BuildCommand(RestartGraphCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {
            var world=World.Copy();var instance=world.Instances.SingleOrDefault(i=>i.Id==command.InstanceId);
            if(instance==null||instance.Run!=command.Run||instance.Status!=GraphStatus.Completed)return CommandResult.Fail("Stale or unfinished graph run.");
            if(instance.RestartAt.HasValue&&model.Time<instance.RestartAt.Value)return CommandResult.Fail("Graph cooldown has not elapsed.");
            if(!simulation.Entities.Entities.ContainsKey(instance.OwnerId))return CommandResult.Fail("Graph owner no longer exists.");
            world.Instances[world.Instances.IndexOf(instance)]=Fresh(instance.Id,instance.DefinitionId,instance.OwnerId,checked(instance.Run+1),instance.Ending);Save(world,transaction);return CommandResult.Ok();
        }
        public CommandResult Build(PublishGraphSignalCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {try{return BuildCommand(command,model,transaction);}catch(Exception error) when(error is ArgumentException||error is ArithmeticException||error is InvalidOperationException){return CommandResult.Fail(error.Message);}}
        private CommandResult BuildCommand(PublishGraphSignalCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {
            if(string.IsNullOrWhiteSpace(command.Key)||command.Count<1||!simulation.Entities.Entities.ContainsKey(command.OwnerId??""))return CommandResult.Fail("Invalid graph signal.");
            var world=World.Copy();
            try
            {
                void Count(Dictionary<string,long> counts){counts.TryGetValue(command.Key,out var count);counts[command.Key]=checked(count+command.Count);}
                foreach(var i in world.Instances.Where(i=>i.OwnerId==command.OwnerId&&i.Status!=GraphStatus.Completed))
                {
                    // Track only authored keys, bounding saved signal storage independently of publisher input.
                    var d=graphs[i.DefinitionId].Definition;
                    var requirements=new[]{d.Activation}.Concat(d.Nodes.SelectMany(n=>new[]{n.Activation,n.Trigger})).Where(r=>r!=null).SelectMany(r=>r.Walk()).Where(r=>r.Kind==GraphRequirementKind.Signal&&r.Key==command.Key).ToArray();
                    if(requirements.Any(r=>r.ProgressScope==GraphProgressScope.Run))Count(i.Signals);
                    if(i.Status==GraphStatus.Active&&GraphEligible(i,d))
                    {
                        if(requirements.Any(r=>r.ProgressScope==GraphProgressScope.Active))Count(i.ActiveSignals);
                        foreach(var n in i.Nodes.Where(n=>n.Status==GraphNodeStatus.Active||n.Status==GraphNodeStatus.Ready))
                        {
                            var nd=graphs[i.DefinitionId].Nodes[n.Id];
                            if(NodeEligible(i,n,nd)&&new[]{nd.Activation,nd.Trigger}.Where(r=>r!=null).SelectMany(r=>r.Walk()).Any(r=>r.Kind==GraphRequirementKind.Signal&&r.Key==command.Key&&r.ProgressScope==GraphProgressScope.Active))Count(n.Signals);
                        }
                    }
                    Wake(i,model.Time);
                }
            }
            catch(OverflowException){return CommandResult.Fail("Graph signal counter overflow.");}
            Save(world,transaction);return CommandResult.Ok();
        }
        public CommandResult Build(CompleteGraphNodeCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {try{return BuildCommand(command,model,transaction);}catch(Exception error) when(error is ArgumentException||error is ArithmeticException||error is InvalidOperationException){return CommandResult.Fail(error.Message);}}
        private CommandResult BuildCommand(CompleteGraphNodeCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {
            var world=World.Copy();var instance=world.Instances.SingleOrDefault(i=>i.Id==command.InstanceId);
            if(instance==null||instance.Run!=command.Run||!graphs[instance.DefinitionId].Nodes.TryGetValue(command.NodeId??"",out var definition))return CommandResult.Fail("Unknown node or stale graph run.");
            var node=instance.Nodes.Single(n=>n.Id==definition.Id);
            if(instance.Status!=GraphStatus.Active||(node.Status!=GraphNodeStatus.Active&&node.Status!=GraphNodeStatus.Ready)||!instance.Activated||!node.Activated||!GraphEligible(instance,graphs[instance.DefinitionId].Definition)||!NodeEligible(instance,node,definition)||node.Status==GraphNodeStatus.Completed||node.Status==GraphNodeStatus.Skipped||!Context(instance,node).Evaluate(definition.Trigger))return CommandResult.Fail("Node cannot complete now.");
            effectsStaged=false;Complete(instance,node,definition,transaction);
            if(effectsStaged)foreach(var other in world.Instances)Wake(other,model.Time);
            Save(world,transaction);return CommandResult.Ok();
        }
        public void RecordFailure(CompleteGraphNodeCommand command,CommandResult result,StateTransaction transaction)
        {
            var world=World.Copy();var instance=world.Instances.SingleOrDefault(i=>i.Id==command.InstanceId);
            var node=instance?.Nodes.SingleOrDefault(n=>n.Id==command.NodeId);
            if(node==null||instance.Run!=command.Run||node.Status==GraphNodeStatus.Completed||node.Status==GraphNodeStatus.Skipped)return;
            node.LastError=result.Error;transaction.Set(store,0,new GraphCell{Value=world});
        }
        private void Complete(GraphInstanceState instance,GraphNodeState node,GraphNodeDefinition definition,SimulationTransaction transaction)
        {
            Stage(definition.OnComplete,instance,node,transaction);Stop(node,simulation.Time);node.Status=GraphNodeStatus.Completed;node.NextEvaluation=null;node.LastError=null;
            if(definition.IsEnd)
            {
                var d=graphs[instance.DefinitionId].Definition;Stage(d.OnComplete,instance,null,transaction);
                Stop(instance,simulation.Time);instance.Status=GraphStatus.Completed;instance.Ending=node.Id;instance.LastError=null;instance.NextEvaluation=null;
                instance.RestartAt=d.RestartCooldown.HasValue?Later(d.RestartCooldown.Value):(double?)null;
                foreach(var other in instance.Nodes)if(other.Status!=GraphNodeStatus.Completed){Stop(other,simulation.Time);other.Status=GraphNodeStatus.Skipped;other.NextEvaluation=null;other.LastError=null;}
            }
            else Wake(instance,simulation.Time);
        }
        CommandResult ICommandHandler<GraphWorkCommand>.Build(GraphWorkCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {
            effectsStaged=false;
            var world=World.Copy();
            if(command.InstanceId==null)
            {foreach(var i in world.Instances)Wake(i,model.Time);Save(world,transaction);return CommandResult.Ok();}
            var instance=world.Instances.Single(i=>i.Id==command.InstanceId);var d=graphs[instance.DefinitionId].Definition;
            if(instance.Status==GraphStatus.Completed)
            {
                if(instance.RestartAt.HasValue&&instance.RestartAt<=model.Time)
                {
                    if(!simulation.Entities.Entities.ContainsKey(instance.OwnerId)){instance.RestartAt=null;instance.LastError="Graph owner no longer exists.";}
                    else world.Instances[world.Instances.IndexOf(instance)]=Fresh(instance.Id,instance.DefinitionId,instance.OwnerId,checked(instance.Run+1),instance.Ending);
                }
                Save(world,transaction);return CommandResult.Ok();
            }
            if(command.NodeId==null)EvaluateGraph(instance,d,transaction);
            else EvaluateNode(instance,instance.Nodes.Single(n=>n.Id==command.NodeId),graphs[instance.DefinitionId].Nodes[command.NodeId],transaction);
            if(effectsStaged)foreach(var other in world.Instances)Wake(other,model.Time);
            Save(world,transaction);return CommandResult.Ok();
        }
        private void EvaluateGraph(GraphInstanceState instance,GraphDefinition definition,SimulationTransaction transaction)
        {
            var before=instance.Status;instance.LastError=null;
            if(!simulation.Entities.Entities.ContainsKey(instance.OwnerId))
            {
                Stop(instance,simulation.Time);instance.Status=instance.Activated?GraphStatus.Suspended:GraphStatus.Dormant;
                instance.LastError="Graph owner no longer exists.";instance.NextEvaluation=null;
                foreach(var node in instance.Nodes)if(node.Status!=GraphNodeStatus.Completed&&node.Status!=GraphNodeStatus.Skipped)
                {Stop(node,simulation.Time);node.Status=node.Activated?GraphNodeStatus.Suspended:GraphNodeStatus.Dormant;node.NextEvaluation=null;}
                return;
            }
            if(!GraphEligible(instance,definition))
            {
                Stop(instance,simulation.Time);instance.Status=instance.Activated?GraphStatus.Suspended:GraphStatus.Dormant;
                foreach(var node in instance.Nodes)if(node.Status==GraphNodeStatus.Active||node.Status==GraphNodeStatus.Ready){Stop(node,simulation.Time);node.Status=GraphNodeStatus.Suspended;node.NextEvaluation=null;}
            }
            else
            {
                if(!instance.Activated){Stage(definition.OnActivate,instance,null,transaction);instance.Activated=true;}
                instance.Status=GraphStatus.Active;if(!instance.ActiveSince.HasValue)instance.ActiveSince=simulation.Time;
            }
            if(before!=instance.Status)Wake(instance,simulation.Time);
            instance.NextEvaluation=(!instance.Activated||definition.ActivationMode==GraphActivationMode.Maintained)?Context(instance).Next(definition.Activation,definition.EvaluationInterval):null;
        }
        private void EvaluateNode(GraphInstanceState instance,GraphNodeState node,GraphNodeDefinition definition,SimulationTransaction transaction)
        {
            if(node.Status==GraphNodeStatus.Completed||node.Status==GraphNodeStatus.Skipped){node.NextEvaluation=null;return;}
            var before=node.Status;node.LastError=null;node.NextEvaluation=null;
            var d=graphs[instance.DefinitionId].Definition;
            if(instance.Status!=GraphStatus.Active||!GraphEligible(instance,d))
            {Stop(node,simulation.Time);node.Status=node.Activated?GraphNodeStatus.Suspended:GraphNodeStatus.Dormant;return;}
            if(!NodeEligible(instance,node,definition))
            {
                Stop(node,simulation.Time);node.Status=node.Activated?GraphNodeStatus.Suspended:GraphNodeStatus.Dormant;
                if(Prerequisites(instance,definition))node.NextEvaluation=Context(instance,node).Next(definition.Activation,d.EvaluationInterval);
                return;
            }
            if(!node.Activated)
            {
                Stage(definition.OnActivate,instance,node,transaction);node.Activated=true;
                node.ActiveSince=simulation.Time;node.Status=GraphNodeStatus.Active;node.NextEvaluation=simulation.Time;return;
            }
            if(!node.ActiveSince.HasValue)node.ActiveSince=simulation.Time;
            var context=Context(instance,node);node.Status=context.Evaluate(definition.Trigger)?GraphNodeStatus.Ready:GraphNodeStatus.Active;
            if(node.Status==GraphNodeStatus.Ready&&definition.Completion==GraphCompletionMode.Automatic)
            {
                if(before!=GraphNodeStatus.Ready){node.NextEvaluation=simulation.Time;return;}
                Complete(instance,node,definition,transaction);return;
            }
            if(before!=node.Status)foreach(var other in instance.Nodes)if(other.Id!=node.Id&&other.Status!=GraphNodeStatus.Completed&&other.Status!=GraphNodeStatus.Skipped)other.NextEvaluation=simulation.Time;
            node.NextEvaluation=Earlier(context.Next(definition.Trigger,d.EvaluationInterval),definition.ActivationMode==GraphActivationMode.Maintained?context.Next(definition.Activation,d.EvaluationInterval):null);
        }
        CommandResult ICommandHandler<GraphErrorCommand>.Build(GraphErrorCommand command,SimulationReadModel model,SimulationTransaction transaction)
        {
            var world=World.Copy();var instance=world.Instances.Single(i=>i.Id==command.InstanceId);double next=Later(graphs[instance.DefinitionId].Definition.EvaluationInterval);
            if(command.NodeId==null){instance.LastError=command.Error;instance.NextEvaluation=next;}
            else{var node=instance.Nodes.Single(n=>n.Id==command.NodeId);node.LastError=command.Error;node.NextEvaluation=next;}
            Save(world,transaction);return CommandResult.Ok();
        }
        public CommandResult Prepare(SimulationReadModel model,StateTransaction transaction)
        {
            var world=World.Copy();
            for(int index=0;index<world.Instances.Count;index++)
            {var i=world.Instances[index];if(graphs[i.DefinitionId].Definition.PrestigePolicy==ResetPolicy.Reset)world.Instances[index]=Fresh(i.Id,i.DefinitionId,i.OwnerId,checked(i.Run+1),i.Ending??i.PreviousEnding);}
            transaction.Set(store,0,new GraphCell{Value=world});return CommandResult.Ok();
        }
        public GraphSnapshot Export()
        {var snapshot=World.Copy();snapshot.InvalidatedAt=invalidatedAt;return snapshot;}
        public void ValidateSnapshot(GraphSnapshot snapshot)
        {
            if(snapshot==null||snapshot.Definitions==null||snapshot.Definitions.Count!=graphs.Count||graphs.Any(g=>!snapshot.Definitions.TryGetValue(g.Key,out var hash)||hash!=g.Value.Fingerprint))throw new ArgumentException("Graph definitions differ; migrate graph content explicitly.");
            if(snapshot.Instances==null||snapshot.Instances.Count>10000||snapshot.Instances.Any(i=>i==null)||snapshot.Instances.Select(i=>i.Id).Distinct().Count()!=snapshot.Instances.Count)throw new ArgumentException("Invalid graph instances.");
            void Time(double value){if(!GraphCompiler.Finite(value)||value<0)throw new ArgumentException("Invalid graph time.");}
            void Optional(double? value){if(value.HasValue)Time(value.Value);}
            void Signals(Dictionary<string,long> values){if(values==null||values.Count>10000||values.Any(v=>string.IsNullOrWhiteSpace(v.Key)||v.Value<0))throw new ArgumentException("Invalid graph counters.");}
            Optional(snapshot.InvalidatedAt);
            foreach(var i in snapshot.Instances)
            {
                if(string.IsNullOrWhiteSpace(i.Id)||string.IsNullOrWhiteSpace(i.OwnerId)||i.Run<1||!graphs.TryGetValue(i.DefinitionId??"",out var graph)||!Enum.IsDefined(typeof(GraphStatus),i.Status)||i.Nodes==null||i.Nodes.Count!=graph.Nodes.Count||i.Nodes.Any(n=>n==null)||i.Nodes.Select(n=>n.Id).Distinct().Count()!=i.Nodes.Count||i.Nodes.Any(n=>!graph.Nodes.ContainsKey(n.Id??"")))throw new ArgumentException("Invalid graph instance state.");
                Time(i.StartedAt);Time(i.ActiveSeconds);Optional(i.ActiveSince);Optional(i.NextEvaluation);Optional(i.RestartAt);Signals(i.Signals);Signals(i.ActiveSignals);
                if((i.Status==GraphStatus.Active||i.Status==GraphStatus.Suspended||i.Status==GraphStatus.Completed)&&!i.Activated||i.ActiveSince.HasValue!=(i.Status==GraphStatus.Active)||i.Status==GraphStatus.Completed&&(i.Ending==null||!graph.Nodes.TryGetValue(i.Ending,out var ending)||!ending.IsEnd||i.Nodes.Single(n=>n.Id==i.Ending).Status!=GraphNodeStatus.Completed||i.NextEvaluation.HasValue||i.Nodes.Any(n=>n.Status!=GraphNodeStatus.Completed&&n.Status!=GraphNodeStatus.Skipped)))throw new ArgumentException("Inconsistent graph lifecycle.");
                foreach(var n in i.Nodes)
                {
                    Time(n.ActiveSeconds);Optional(n.ActiveSince);Optional(n.NextEvaluation);Signals(n.Signals);
                    if(n.Status==GraphNodeStatus.Skipped&&i.Status!=GraphStatus.Completed||!Enum.IsDefined(typeof(GraphNodeStatus),n.Status)||n.ActiveSince.HasValue!=(n.Status==GraphNodeStatus.Active||n.Status==GraphNodeStatus.Ready)||!n.Activated&&(n.Status==GraphNodeStatus.Active||n.Status==GraphNodeStatus.Ready||n.Status==GraphNodeStatus.Completed)|| (n.Status==GraphNodeStatus.Completed||n.Status==GraphNodeStatus.Skipped)&&n.NextEvaluation.HasValue)throw new ArgumentException("Inconsistent graph node state.");
                }
            }
        }
        public void Import(GraphSnapshot snapshot)
        {
            ValidateSnapshot(snapshot);var copy=snapshot.Copy();
            if(simulation==null)throw new InvalidOperationException("Attach graphs before restoring.");
            foreach(var i in copy.Instances)
            {
                if(simulation.Entities.Entities.ContainsKey(i.OwnerId))ValidateOwner(graphs[i.DefinitionId].Definition,i.OwnerId);
                else copy.InvalidatedAt=simulation.Time;
                foreach(var due in new[]{i.NextEvaluation,i.RestartAt}.Concat(i.Nodes.Select(n=>n.NextEvaluation)))if(due.HasValue&&due.Value<simulation.Time)throw new ArgumentException("Graph work precedes restored time.");
                if(i.StartedAt>simulation.Time||i.ActiveSince>simulation.Time||i.Nodes.Any(n=>n.ActiveSince>simulation.Time))throw new ArgumentException("Graph clock exceeds restored time.");
            }
            if(copy.InvalidatedAt.HasValue&&copy.InvalidatedAt.Value<simulation.Time)throw new ArgumentException("Stale graph invalidation.");
            var tx=new StateTransaction();tx.Set(store,0,new GraphCell{Value=copy});if(!tx.Commit())throw new InvalidOperationException("Graph restore conflict.");invalidatedAt=copy.InvalidatedAt;PublishViews();
        }
        public CompiledGraph Definition(string id)=>id!=null&&graphs.TryGetValue(id,out var graph)?graph:throw new ArgumentException("Unknown graph definition: "+id,nameof(id));
        public GraphInstanceState Instance(string id)=>World.Instances.Single(i=>i.Id==id).Copy();
        public IReadOnlyList<GraphInstanceState> Instances=>World.Instances.OrderBy(i=>i.Id,StringComparer.Ordinal).Select(i=>i.Copy()).ToArray();
        public GraphNodeView View(string instanceId,string nodeId)
        {
            string key=GraphCompiler.Pack(instanceId,nodeId);
            if(!views.TryGetValue(key,out var view)){view=new GraphNodeView(ViewWorld,key,instanceId,nodeId);views.Add(key,view);}
            UpdateView(view);return view;
        }
        private void UpdateView(GraphNodeView view)
        {
            var i=World.Instances.SingleOrDefault(i=>i.Id==view.InstanceId);var n=i?.Nodes.SingleOrDefault(n=>n.Id==view.NodeId);
            if(n==null){view.Update(null,0,false,"Graph node is unavailable.",0);return;}
            var d=graphs[i.DefinitionId].Nodes[n.Id];var c=Context(i,n);bool can=false;string reason="";double progress=0;
            try
            {
                can=i.Status==GraphStatus.Active&&(n.Status==GraphNodeStatus.Active||n.Status==GraphNodeStatus.Ready)&&n.Activated&&GraphEligible(i,graphs[i.DefinitionId].Definition)&&NodeEligible(i,n,d)&&n.Status!=GraphNodeStatus.Completed&&n.Status!=GraphNodeStatus.Skipped&&c.Evaluate(d.Trigger);
                string Describe(GraphRequirement r)=>r.Children.Count==0?r.Key??r.Kind.ToString():r.Kind+"("+string.Join(", ",r.Children.Select(Describe))+")";
                var unmet=new List<string>();
                var graphDefinition=graphs[i.DefinitionId].Definition;
                if(!GraphEligible(i,graphDefinition))unmet.Add("Graph activation: "+(graphDefinition.Activation==null?i.Status.ToString():Describe(graphDefinition.Activation)));
                if(!Prerequisites(i,d))unmet.Add("Prerequisites: "+d.PrerequisiteMode+"("+string.Join(", ",d.Prerequisites)+")");
                if((!n.Activated||d.ActivationMode==GraphActivationMode.Maintained)&&!c.Evaluate(d.Activation))unmet.Add("Activation: "+Describe(d.Activation));
                if(!c.Evaluate(d.Trigger))unmet.Add("Trigger: "+Describe(d.Trigger));
                foreach(var r in d.Trigger?.Walk()??Enumerable.Empty<GraphRequirement>())
                {if(r.Kind==GraphRequirementKind.Signal)progress=Math.Max(progress,Math.Min(1,(double)c.SignalCount(r.Key,r.ProgressScope)/r.Count));if(r.Kind==GraphRequirementKind.Time&&r.Seconds>0)progress=Math.Max(progress,Math.Min(1,(r.TimeScope==GraphTimeScope.Absolute?c.Time:r.TimeScope==GraphTimeScope.RunElapsed?c.RunElapsed:c.ActiveElapsed)/r.Seconds));}
                if(n.Status==GraphNodeStatus.Completed||n.Status==GraphNodeStatus.Skipped)unmet.Add(n.Status.ToString());
                reason=can?"":string.Join(", ",unmet);
            }
            catch(Exception error) when(error is ArgumentException||error is ArithmeticException||error is InvalidOperationException){reason=error.Message;}
            view.Update(n,i.Run,can,reason,progress);
        }
        private void PublishViews()
        {
            foreach(var view in views.Values.ToArray())UpdateView(view);
            if(Changed!=null)foreach(Action callback in Changed.GetInvocationList())try{callback();}catch(Exception error){simulation?.Entities.Warn("graph-listener",error.Message,error);}
        }
    }
    public sealed class GraphNodeView
    {
        public string InstanceId { get; } public string NodeId { get; }
        public EntityInstance Entity { get; }
        public Value<GraphNodeStatus> Status { get; } public Value<long> Run { get; }
        public Value<bool> CanComplete { get; } public Value<string> UnmetRequirements { get; }
        public Value<string> LastError { get; } public Value<double> Progress { get; }
        internal GraphNodeView(EntityContext world,string key,string instanceId,string nodeId)
        {
            InstanceId=instanceId;NodeId=nodeId;Entity=new EntityInstance(key,"graph-view",world,"graph-view");
            Status=Entity.Define("Status",GraphNodeStatus.Dormant);Run=Entity.Define("Run",0L);CanComplete=Entity.Define("CanComplete",false);
            UnmetRequirements=Entity.Define("UnmetRequirements","");LastError=Entity.Define("LastError","");Progress=Entity.Define("Progress",0d);world.Add(Entity);
        }
        internal void Update(GraphNodeState state,long run,bool can,string reason,double progress)
        {using(Entity.Context.Batch()){Status.Current=state?.Status??GraphNodeStatus.Dormant;Run.Current=run;CanComplete.Current=can;UnmetRequirements.Current=reason;LastError.Current=state?.LastError??"";Progress.Current=progress;}}
    }
}
